import { useCallback, useEffect, useMemo, useState } from 'react';

import { api, ApiError, type CancelledLineInfo } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { AppHeader } from '../components/AppHeader';
import { CurrencyErrorModal } from '../components/CurrencyErrorModal';
import { FileTypeErrorModal } from '../components/FileTypeErrorModal';
import { Dropzone, type UploadState } from '../components/Dropzone';
import { Footer } from '../components/Footer';
import { ParseResultModal } from '../components/ParseResultModal';
import { ParseSettingsCard } from '../components/ParseSettingsCard';
import { RecentUploadsTable } from '../components/RecentUploadsTable';
import { ToastStack, type ToastMessage } from '../components/Toast';
import { CRM_TEMPLATE_PERCENT_OFF_WITH_UPLIFT, CRM_TEMPLATE_UPLIFT, VENDOR_ZEBRA } from '../constants';
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
  const [onCostPct, setOnCostPct] = useState('');
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
  const [currencyError, setCurrencyError] = useState(false);
  const [fileTypeError, setFileTypeError] = useState<string | null>(null);
  const [resultPending, setResultPending] = useState<{
    blob: Blob;
    filename: string;
    validation: 'match' | 'mismatch';
    currency: string;
    computedTotal: string;
    quotedTotal: string;
    cancelledLines: CancelledLineInfo[];
    reportType: string | null;
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
    // Zebra: Uplift requires margin; No Calculation has no required fields (On Cost % is optional).
    if (selectedParser?.vendor === VENDOR_ZEBRA) {
      return selectedTemplate !== CRM_TEMPLATE_UPLIFT || Boolean(margin);
    }
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
    if (onCostPct) {
      form.set('on_cost_pct', onCostPct);
    }
    if (selectedTemplate) {
      form.set('crm_template', selectedTemplate);
    }

    try {
      const result = await api.parse(form);
      setUploadState('parsed');
      await refresh();
      await loadHistory();

      // Show a single result popup. The file is NOT auto-downloaded; the user
      // downloads explicitly from the modal. The report type to use when sending
      // the quote to the customer is admin-configured per parser slug.
      const reportType = parsers.find((p) => p.slug === parserSlug)?.report_type ?? null;
      setResultPending({
        blob: result.blob,
        filename: result.filename,
        validation: result.validation,
        currency: result.currency ?? '',
        computedTotal: result.computedTotal ?? '',
        quotedTotal: result.quotedTotal ?? '',
        cancelledLines: result.cancelledLines,
        reportType,
      });
    } catch (caught) {
      setUploadState('idle');
      const fileTypeMessage = fileTypeErrorMessage(caught);
      if (isCurrencyError(caught)) {
        setCurrencyError(true);
      } else if (fileTypeMessage) {
        setFileTypeError(fileTypeMessage);
      } else {
        const message = errorMessage(caught);
        setDropError(message);
        pushToast({ tone: 'error', title: 'Could not parse file', detail: message });
      }
    }
  }

  function downloadResult() {
    if (!resultPending) return;
    downloadBlob(resultPending.blob, resultPending.filename);
  }

  function closeResult() {
    setResultPending(null);
    setFile(null);
    setUploadState('idle');
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
            onCostPct={onCostPct}
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
            onOnCostPct={setOnCostPct}
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
      {currencyError && (
        <CurrencyErrorModal onClose={() => setCurrencyError(false)} />
      )}
      {fileTypeError && (
        <FileTypeErrorModal message={fileTypeError} onClose={() => setFileTypeError(null)} />
      )}
      {resultPending && (
        <ParseResultModal
          validation={resultPending.validation}
          currency={resultPending.currency}
          computedTotal={resultPending.computedTotal}
          quotedTotal={resultPending.quotedTotal}
          cancelledLines={resultPending.cancelledLines}
          reportType={resultPending.reportType}
          onDownload={downloadResult}
          onClose={closeResult}
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

function isCurrencyError(error: unknown): boolean {
  if (error instanceof ApiError && typeof error.detail === 'object' && error.detail !== null) {
    return (error.detail as ApiErrorDetail).stage === 'currency';
  }
  return false;
}

// Wrong file-type selection: the server composes the full guidance message and tags it
// with stage "file_type". Returns the message to show, or null when not this error.
function fileTypeErrorMessage(error: unknown): string | null {
  if (error instanceof ApiError && typeof error.detail === 'object' && error.detail !== null) {
    const detail = error.detail as ApiErrorDetail;
    if (detail.stage === 'file_type') {
      return detail.message ?? detail.hint ?? 'The selected file type is incorrect.';
    }
  }
  return null;
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
