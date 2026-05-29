import { useCallback, useEffect, useMemo, useState } from 'react';

import { api, ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { AppHeader } from '../components/AppHeader';
import { Dropzone, type UploadState } from '../components/Dropzone';
import { Footer } from '../components/Footer';
import { ParseSettingsCard } from '../components/ParseSettingsCard';
import { RecentUploadsTable } from '../components/RecentUploadsTable';
import { ToastStack, type ToastMessage } from '../components/Toast';
import { ValidationWarningModal } from '../components/ValidationWarningModal';
import { CRM_TEMPLATE_PERCENT_OFF_WITH_UPLIFT, CRM_TEMPLATE_UPLIFT } from '../constants';
import type { ApiErrorDetail, HistoryRow, ParserInfo } from '../types';

const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;

export function DashboardPage() {
  const { user, refresh } = useAuth();
  const [parsers, setParsers] = useState<ParserInfo[]>([]);
  const [vendor, setVendor] = useState('');
  const [parserSlug, setParserSlug] = useState('');
  const [selectedTemplate, setSelectedTemplate] = useState('');
  // Uplift, Exchange rate, and Discount Off MSRP are intentionally NOT pre-filled
  // from saved user defaults — the user must enter them every parse so they
  // never get a stale value silently applied.
  const [fxRate, setFxRate] = useState('');
  const [margin, setMargin] = useState('');
  const [imPercent, setImPercent] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [uploadState, setUploadState] = useState<UploadState>('idle');
  const [dropError, setDropError] = useState<string | null>(null);
  const [history, setHistory] = useState<HistoryRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(10);
  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  const [toasts, setToasts] = useState<ToastMessage[]>([]);
  const [mismatchPending, setMismatchPending] = useState<{
    blob: Blob;
    filename: string;
    currency: string;
    computedTotal: string;
    quotedTotal: string;
  } | null>(null);

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
          setSelectedTemplate(preferredParsers[0].available_templates[0] ?? '');
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

  const selectedParser = useMemo(() => parsers.find((p) => p.slug === parserSlug), [parsers, parserSlug]);

  const canSubmit = useMemo(() => {
    if (!vendor || !parserSlug || !file || uploadState !== 'idle') return false;
    // Multi-template HP (HP Bid XLSX): Uplift needs margin; No Calculation needs neither
    if (selectedParser && selectedParser.available_templates.length > 1) {
      const requiresMargin = selectedTemplate === CRM_TEMPLATE_UPLIFT;
      return !requiresMargin || Boolean(margin);
    }
    // Single-template HP (HP OneConfig XLSX): % Off RRP with Uplift needs both margin and im_percent
    if (selectedTemplate === CRM_TEMPLATE_PERCENT_OFF_WITH_UPLIFT) {
      return Boolean(margin && imPercent);
    }
    // Single-template non-HP (Nutanix, Lenovo): needs both fxRate and margin
    return Boolean(fxRate && margin);
  }, [vendor, parserSlug, fxRate, margin, imPercent, selectedTemplate, file, uploadState, selectedParser]);

  const pushToast = useCallback((toast: Omit<ToastMessage, 'id'>) => {
    const id = Date.now();
    setToasts((items) => [...items, { ...toast, id }]);
    window.setTimeout(() => setToasts((items) => items.filter((item) => item.id !== id)), 6000);
  }, []);

  const handleFile = useCallback((next: File) => {
    if (!next.name.match(/\.(pdf|xlsx|xls)$/i)) {
      setDropError('Only PDF, XLSX, and XLS files are supported.');
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
  }, []);

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
    if (imPercent) {
      form.set('im_percent', imPercent);
    }
    if (selectedTemplate) {
      form.set('crm_template', selectedTemplate);
    }

    try {
      const result = await api.parse(form);
      setUploadState('parsed');
      await refresh();
      await loadHistory();

      if (result.validation !== 'match') {
        // Hold the blob and let the user acknowledge the warning before downloading.
        setMismatchPending({
          blob: result.blob,
          filename: result.filename,
          currency: result.currency ?? '',
          computedTotal: result.computedTotal ?? '',
          quotedTotal: result.quotedTotal ?? '',
        });
      } else {
        downloadBlob(result.blob, result.filename);
        pushToast({
          tone: 'success',
          title: 'Parsed workbook downloaded',
          detail: `Computed ${result.currency} ${result.computedTotal || '-'} · Quoted ${result.currency} ${result.quotedTotal || '-'}`,
        });
        window.setTimeout(() => {
          setFile(null);
          setUploadState('idle');
        }, 1800);
      }
    } catch (caught) {
      setUploadState('idle');
      const message = errorMessage(caught);
      setDropError(message);
      pushToast({ tone: 'error', title: 'Could not parse file', detail: message });
    }
  }

  function acknowledgeMismatch() {
    if (!mismatchPending) return;
    downloadBlob(mismatchPending.blob, mismatchPending.filename);
    setMismatchPending(null);
    window.setTimeout(() => {
      setFile(null);
      setUploadState('idle');
    }, 1800);
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
            margin={margin}
            imPercent={imPercent}
            selectedTemplate={selectedTemplate}
            canSubmit={canSubmit}
            parsing={uploadState === 'parsing'}
            onVendor={(value) => {
              setVendor(value);
              setParserSlug('');
              setSelectedTemplate('');
            }}
            onParser={(slug) => {
              setParserSlug(slug);
              const p = parsers.find((x) => x.slug === slug);
              setSelectedTemplate(p?.available_templates[0] ?? '');
            }}
            onFxRate={setFxRate}
            onMargin={setMargin}
            onImPercent={setImPercent}
            onTemplate={setSelectedTemplate}
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
      {mismatchPending && (
        <ValidationWarningModal
          currency={mismatchPending.currency}
          computedTotal={mismatchPending.computedTotal}
          quotedTotal={mismatchPending.quotedTotal}
          onAcknowledge={acknowledgeMismatch}
        />
      )}
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
