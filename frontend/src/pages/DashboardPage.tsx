import { RotateCcw } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';

import { api, ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { AppHeader } from '../components/AppHeader';
import { Dropzone, type UploadState } from '../components/Dropzone';
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
  const [margin, setMargin] = useState(user?.margin ?? '');
  const [file, setFile] = useState<File | null>(null);
  const [uploadState, setUploadState] = useState<UploadState>('idle');
  const [dropError, setDropError] = useState<string | null>(null);
  const [history, setHistory] = useState<HistoryRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(5);
  const [toasts, setToasts] = useState<ToastMessage[]>([]);

  const loadHistory = useCallback(async () => {
    const response = await api.history(pageSize, page * pageSize);
    setHistory(response.rows);
    setTotal(response.total);
  }, [page, pageSize]);

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
      if (items.length === 1) {
        setVendor(items[0].vendor);
        setParserSlug(items[0].slug);
      }
    });
  }, []);

  useEffect(() => {
    void loadHistory();
  }, [loadHistory]);

  useEffect(() => {
    setFxRate(user?.fx_rate ?? '');
    setMargin(user?.margin ?? '');
  }, [user?.fx_rate, user?.margin]);

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

  function resetForm() {
    setVendor('');
    setParserSlug('');
    setFile(null);
    setDropError(null);
    setUploadState('idle');
    setFxRate(user?.fx_rate ?? '');
    setMargin(user?.margin ?? '');
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

  return (
    <div className="min-h-screen bg-paper">
      <AppHeader />
      <main className="mx-auto flex min-h-[calc(100vh-56px)] max-w-[1280px] flex-col px-5 py-7 md:px-12 md:py-8">
        <div className="flex items-end justify-between gap-4">
          <div>
            <h1 className="text-[26px] font-semibold leading-none tracking-normal text-ink">New quote</h1>
            <div className="label label-faint mt-2">Upload a vendor quote / bid to parse</div>
          </div>
          <button type="button" className="button border-red-200 bg-red-50 text-red-600" onClick={resetForm}>
            <RotateCcw size={13} />
            Reset
          </button>
        </div>

        <div className="mt-8 flex flex-1 flex-col gap-6 md:flex-row md:items-stretch">
          <ParseSettingsCard
            parsers={parsers}
            vendor={vendor}
            parserSlug={parserSlug}
            fxRate={fxRate}
            margin={margin}
            canSubmit={canSubmit}
            parsing={uploadState === 'parsing'}
            onVendor={(value) => {
              setVendor(value);
              setParserSlug('');
            }}
            onParser={setParserSlug}
            onFxRate={setFxRate}
            onMargin={setMargin}
            onSubmit={submit}
          />
          <section className="flex min-w-0 flex-1 flex-col">
            <Dropzone file={file} state={uploadState} error={dropError} onFile={handleFile} onClear={() => setFile(null)} />
            <div className="mt-2 flex justify-between">
              <span className="label label-faint">Drag multiple files to batch parse</span>
            </div>
            <RecentUploadsTable
              rows={history}
              total={total}
              page={page}
              pageSize={pageSize}
              onPage={setPage}
              onPageSize={handlePageSize}
            />
          </section>
        </div>
      </main>
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
