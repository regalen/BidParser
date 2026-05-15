import { useCallback, useEffect, useMemo, useState } from 'react';

import { api, ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { AppHeader } from '../components/AppHeader';
import { Dropzone, type UploadState } from '../components/Dropzone';
import { Footer } from '../components/Footer';
import { ParseSettingsCard } from '../components/ParseSettingsCard';
import { RecentUploadsTable } from '../components/RecentUploadsTable';
import { ToastStack, type ToastMessage } from '../components/Toast';
import type { ApiErrorDetail, HistoryRow, ParserInfo } from '../types';

const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;

export function DashboardPage() {
  const { user, refresh } = useAuth();
  const [parsers, setParsers] = useState<ParserInfo[]>([]);
  const [vendor, setVendor] = useState('');
  const [parserSlug, setParserSlug] = useState('');
  const [fxRate, setFxRate] = useState(user?.fx_rate ?? '');
  const [fxRatePegged, setFxRatePegged] = useState(user?.fx_rate_pegged ?? false);
  const [margin, setMargin] = useState(user?.margin ?? '');
  const [file, setFile] = useState<File | null>(null);
  const [uploadState, setUploadState] = useState<UploadState>('idle');
  const [savingDefaults, setSavingDefaults] = useState(false);
  const [dropError, setDropError] = useState<string | null>(null);
  const [history, setHistory] = useState<HistoryRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(5);
  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  const [toasts, setToasts] = useState<ToastMessage[]>([]);

  const loadHistory = useCallback(async () => {
    const response = await api.history(pageSize, page * pageSize, debouncedQuery);
    setHistory(response.rows);
    setTotal(response.total);
  }, [page, pageSize, debouncedQuery]);

  const handlePageSize = useCallback(
    (nextPageSize: number) => {
      setPageSize(nextPageSize);
      setPage((currentPage) => {
        const maxPage = Math.max(0, Math.ceil(total / nextPageSize) - 1);
        return Math.min(currentPage, maxPage);
      });
    },
    [total],
  );

  useEffect(() => {
    api.parsers().then((items) => {
      setParsers(items);
      const vendors = Array.from(new Set(items.map((item) => item.vendor)));
      const preferredVendor = user?.default_vendor && vendors.includes(user.default_vendor) ? user.default_vendor : vendors.length === 1 ? vendors[0] : '';
      if (preferredVendor) {
        setVendor(preferredVendor);
        const preferredParsers = items.filter((item) => item.vendor === preferredVendor);
        if (preferredParsers.length === 1) {
          setParserSlug(preferredParsers[0].slug);
        }
      }
    });
  }, [user?.default_vendor]);

  useEffect(() => {
    void loadHistory();
  }, [loadHistory]);

  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedQuery(query), 250);
    return () => window.clearTimeout(handle);
  }, [query]);

  useEffect(() => {
    setPage(0);
  }, [debouncedQuery]);

  useEffect(() => {
    setFxRate(user?.fx_rate ?? '');
    setFxRatePegged(user?.fx_rate_pegged ?? false);
    setMargin(user?.margin ?? '');
  }, [user?.fx_rate, user?.fx_rate_pegged, user?.margin]);

  const defaultsDirty = useMemo(() => {
    return (
      vendor !== (user?.default_vendor ?? '') ||
      margin !== (user?.margin ?? '') ||
      fxRatePegged !== (user?.fx_rate_pegged ?? false) ||
      (!fxRatePegged && fxRate !== (user?.fx_rate ?? ''))
    );
  }, [vendor, margin, fxRate, fxRatePegged, user?.default_vendor, user?.margin, user?.fx_rate, user?.fx_rate_pegged]);

  const canSubmit = useMemo(() => {
    return Boolean(vendor && parserSlug && fxRate && margin && file && uploadState === 'idle');
  }, [vendor, parserSlug, fxRate, margin, file, uploadState]);

  function pushToast(toast: Omit<ToastMessage, 'id'>) {
    const id = Date.now();
    setToasts((items) => [...items, { ...toast, id }]);
    window.setTimeout(() => setToasts((items) => items.filter((item) => item.id !== id)), 6000);
  }

  function handleFile(next: File) {
    if (!next.name.match(/\.(pdf|xlsx)$/i)) {
      setDropError('Only PDF and XLSX files are supported.');
      setFile(null);
      return;
    }
    if (next.size > MAX_UPLOAD_BYTES) {
      setDropError('File is larger than the 10 MB limit.');
      setFile(null);
      return;
    }
    setDropError(null);
    setFile(next);
  }

  async function submit() {
    if (!file) return;
    setUploadState('parsing');
    setDropError(null);
    const form = new FormData();
    form.set('file', file);
    form.set('vendor', vendor);
    form.set('parser_slug', parserSlug);
    form.set('fx_rate', fxRate);
    form.set('margin', margin);

    try {
      const result = await api.parse(form);
      setUploadState('parsed');
      downloadBlob(result.blob, result.filename);
      pushToast({
        tone: result.validation === 'match' ? 'success' : 'warning',
        title: result.validation === 'match' ? 'Parsed workbook downloaded' : 'Parsed with a total mismatch',
        detail: `Computed USD ${result.computedTotal || '-'} · Quoted USD ${result.quotedTotal || '-'}`,
      });
      await refresh();
      await loadHistory();
      window.setTimeout(() => {
        setFile(null);
        setUploadState('idle');
      }, 1800);
    } catch (caught) {
      setUploadState('idle');
      const message = errorMessage(caught);
      setDropError(message);
      pushToast({ tone: 'error', title: 'Could not parse file', detail: message });
    }
  }

  async function saveDefaults() {
    setSavingDefaults(true);
    try {
      const payload: { default_vendor?: string; fx_rate_pegged: boolean; fx_rate?: string; margin?: string } = {
        fx_rate_pegged: fxRatePegged,
      };
      if (vendor) payload.default_vendor = vendor;
      if (!fxRatePegged && fxRate) payload.fx_rate = fxRate;
      if (margin) payload.margin = margin;
      await api.updateSettings(payload);
      await refresh();
      pushToast({
        tone: 'success',
        title: 'Defaults saved',
        detail: fxRatePegged ? 'Your default FX rate will follow the latest Bloomberg refresh.' : 'Your saved defaults will be pre-filled next time.',
      });
    } catch (caught) {
      pushToast({ tone: 'error', title: 'Could not save defaults', detail: errorMessage(caught) });
    } finally {
      setSavingDefaults(false);
    }
  }

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <AppHeader />
      <main className="mx-auto flex w-full max-w-7xl flex-1 flex-col px-6 py-8 lg:px-8">
        <div className="flex flex-1 flex-col gap-6 md:flex-row md:items-stretch">
          <ParseSettingsCard
            parsers={parsers}
            vendor={vendor}
            parserSlug={parserSlug}
            fxRate={fxRate}
            fxRatePegged={fxRatePegged}
            margin={margin}
            defaultsDirty={defaultsDirty}
            canSubmit={canSubmit}
            savingDefaults={savingDefaults}
            parsing={uploadState === 'parsing'}
            onVendor={(value) => {
              setVendor(value);
              setParserSlug('');
            }}
            onParser={setParserSlug}
            onFxRate={setFxRate}
            onFxRatePegged={setFxRatePegged}
            onMargin={setMargin}
            onSaveDefaults={saveDefaults}
            onSubmit={submit}
          />
          <section className="flex min-w-0 flex-1 flex-col">
            <Dropzone file={file} state={uploadState} error={dropError} onFile={handleFile} onClear={() => setFile(null)} />
            <div className="mt-2 flex justify-between">
              <span className="label-faint">Drag multiple files to batch parse</span>
            </div>
            <RecentUploadsTable
              rows={history}
              total={total}
              page={page}
              pageSize={pageSize}
              onPage={setPage}
              onPageSize={handlePageSize}
              query={query}
              onQuery={setQuery}
            />
          </section>
        </div>
      </main>
      <Footer />
      <ToastStack toasts={toasts} dismiss={(id) => setToasts((items) => items.filter((item) => item.id !== id))} />
    </div>
  );
}

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

function errorMessage(error: unknown) {
  if (error instanceof ApiError) {
    if (typeof error.detail === 'object' && error.detail !== null) {
      const detail = error.detail as ApiErrorDetail;
      return detail.hint ?? detail.message ?? 'The parser could not read this file.';
    }
    if (typeof error.detail === 'string') {
      return error.detail;
    }
  }
  return 'The parser could not read this file.';
}
