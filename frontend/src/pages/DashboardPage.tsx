import { useCallback, useEffect, useMemo, useRef, useState } from 'react';

import { api, ApiError, type CancelledLineInfo } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { AppHeader } from '../components/AppHeader';
import { CurrencyErrorModal } from '../components/CurrencyErrorModal';
import { DellApiErrorModal } from '../components/DellApiErrorModal';
import { DellQuoteInput } from '../components/DellQuoteInput';
import { FileTypeErrorModal } from '../components/FileTypeErrorModal';
import { Dropzone, type UploadState } from '../components/Dropzone';
import { Footer } from '../components/Footer';
import { ParseResultModal } from '../components/ParseResultModal';
import { ParseSettingsCard } from '../components/ParseSettingsCard';
import { RecentUploadsTable } from '../components/RecentUploadsTable';
import { ToastStack, type ToastMessage } from '../components/Toast';
import {
  CRM_TEMPLATE_NO_CALCULATION,
  CRM_TEMPLATE_PERCENT_OFF_WITH_UPLIFT,
  CRM_TEMPLATE_UPLIFT,
  DELL_QUOTE_ID_PATTERN,
  MAX_UPLOAD_BYTES,
  MAX_UPLOAD_MB,
  VENDOR_DELL,
  isAutoParserSlug,
} from '../constants';
import type { ApiErrorDetail, HistoryRow, ParserInfo, ParseUiConfig } from '../types';

function defaultParserFor(vendorParsers: ParserInfo[]): ParserInfo | null {
  return vendorParsers.find((p) => isAutoParserSlug(p.slug))
    ?? (vendorParsers.length === 1 ? vendorParsers[0] : null);
}

function configuredNumber(value: string | null | undefined): string {
  return value ?? '';
}

export function DashboardPage() {
  const { user, refresh } = useAuth();
  const [parsers, setParsers] = useState<ParserInfo[]>([]);
  const [parseUiConfig, setParseUiConfig] = useState<ParseUiConfig>({ vendorDefaults: {}, guidanceByParserSlug: {} });
  const [parsersLoaded, setParsersLoaded] = useState(false);
  const [parseUiConfigLoaded, setParseUiConfigLoaded] = useState(false);
  const initialVendorResolved = useRef(false);
  const [vendor, setVendor] = useState('');
  const [parserSlug, setParserSlug] = useState('');
  const [selectedTemplate, setSelectedTemplate] = useState('');
  const [splitBySolutionId, setSplitBySolutionId] = useState(false);
  // Numeric values are never persisted per user. They may be prefilled from the
  // administrator-managed vendor defaults and reset only when the vendor changes.
  const [fxRate, setFxRate] = useState('');
  const [margin, setMargin] = useState('');
  const [imPercent, setImPercent] = useState('');
  const [onCostPct, setOnCostPct] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [quoteId, setQuoteId] = useState('');
  const [quoteValidationRequested, setQuoteValidationRequested] = useState(false);
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
  const [dellApiError, setDellApiError] = useState<string | null>(null);
  const [fileTypeError, setFileTypeError] = useState<string | null>(null);
  const [resultPending, setResultPending] = useState<{
    blob: Blob;
    filename: string;
    validation: 'match' | 'mismatch';
    currency: string;
    computedTotal: string;
    quotedTotal: string;
    cancelledLines: CancelledLineInfo[];
    hasRebateIneligibleItems: boolean;
    guidanceHtml: string | null;
    detectedFormat?: string | null;
    outputWorkbookCount: number | null;
  } | null>(null);

  const pushToast = useCallback((toast: Omit<ToastMessage, 'id'>) => {
    const id = Date.now();
    setToasts((items) => [...items, { ...toast, id }]);
    window.setTimeout(() => setToasts((items) => items.filter((item) => item.id !== id)), 6000);
  }, []);

  const applyVendorDefaults = useCallback((nextVendor: string, config = parseUiConfig) => {
    const defaults = config.vendorDefaults[nextVendor];
    setFxRate(configuredNumber(defaults?.fxRate));
    setMargin(configuredNumber(defaults?.margin));
    setImPercent(configuredNumber(defaults?.imPercent));
    setOnCostPct(configuredNumber(defaults?.onCostPct));
  }, [parseUiConfig]);

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
    let active = true;
    api
      .parsers()
      .then((items) => {
        if (!active) return;
        setParsers(items);
        setParsersLoaded(true);
      })
      .catch(() => {
        if (active) pushToast({ tone: 'error', title: 'Could not load file types' });
      });
    return () => {
      active = false;
    };
  }, [pushToast]);

  useEffect(() => {
    let active = true;
    api.parseUiConfig()
      .then((config) => {
        if (!active) return;
        setParseUiConfig(config);
      })
      .catch(() => {
        if (!active) return;
        setParseUiConfig({ vendorDefaults: {}, guidanceByParserSlug: {} });
        pushToast({ tone: 'error', title: 'Could not load parse defaults', detail: 'You can still enter values manually.' });
      })
      .finally(() => {
        if (active) setParseUiConfigLoaded(true);
      });
    return () => { active = false; };
  }, [pushToast]);

  useEffect(() => {
    if (!parsersLoaded || !parseUiConfigLoaded || initialVendorResolved.current) return;
    initialVendorResolved.current = true;

    const vendors = Array.from(new Set(parsers.map((item) => item.vendor)));
    const preferredVendor = user?.defaultVendor && vendors.includes(user.defaultVendor)
      ? user.defaultVendor
      : vendors.length === 1 ? vendors[0] : '';
    if (!preferredVendor) return;

    setVendor(preferredVendor);
    applyVendorDefaults(preferredVendor);
    const defaultParser = defaultParserFor(parsers.filter((item) => item.vendor === preferredVendor));
    if (defaultParser) {
      setParserSlug(defaultParser.slug);
      setSelectedTemplate(defaultParser.availableTemplates[0] ?? '');
    }
  }, [applyVendorDefaults, parseUiConfigLoaded, parsers, parsersLoaded, user?.defaultVendor]);

  // Inline + guarded (rather than calling loadHistory) so a slow page-1 response
  // can't overwrite a fast page-2 response while typing in the search box.
  useEffect(() => {
    let active = true;
    api
      .history(pageSize, page * pageSize, debouncedQuery)
      .then((response) => {
        if (!active) return;
        setHistory(response.rows);
        setTotal(response.total);
      })
      .catch(() => {
        if (active) pushToast({ tone: 'error', title: 'Could not load history' });
      });
    return () => {
      active = false;
    };
  }, [page, pageSize, debouncedQuery, pushToast]);

  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedQuery(query), 250);
    return () => window.clearTimeout(handle);
  }, [query]);

  useEffect(() => {
    setPage(0);
  }, [debouncedQuery]);

  const selectedParser = useMemo(() => parsers.find((p) => p.slug === parserSlug), [parsers, parserSlug]);
  const isDellVendor = vendor === VENDOR_DELL;

  const canSubmit = useMemo(() => {
    const hasSource = isDellVendor ? DELL_QUOTE_ID_PATTERN.test(quoteId) : Boolean(file);
    if (!vendor || !parserSlug || !hasSource || uploadState !== 'idle') return false;
    // Uplift requires margin; On Cost % is optional for parsers that support it.
    // Multi-template (HP Bid, HPE, Dell, …): Uplift needs margin; No Calculation needs neither
    if (selectedParser && selectedParser.availableTemplates.length > 1) {
      const requiresMargin = selectedTemplate === CRM_TEMPLATE_UPLIFT;
      return !requiresMargin || Boolean(margin);
    }
    // Single-template HP (HP OneConfig XLSX): % Off RRP with Uplift needs both margin and imPercent
    if (selectedTemplate === CRM_TEMPLATE_PERCENT_OFF_WITH_UPLIFT) {
      return Boolean(margin && imPercent);
    }
    // Single-template No Calculation (Cisco): needs no inputs
    if (selectedParser?.crmTemplate === CRM_TEMPLATE_NO_CALCULATION) {
      return true;
    }
    // Single-template Foreign Uplift (Nutanix): needs both fxRate and margin
    return Boolean(fxRate && margin);
  }, [vendor, parserSlug, fxRate, margin, imPercent, selectedTemplate, file, quoteId, uploadState, selectedParser, isDellVendor]);

  const handleFile = useCallback((next: File) => {
    if (!next.name.match(/\.(pdf|xlsx|xls|json)$/i)) {
      setDropError('Only PDF, XLSX, XLS, and JSON files are supported.');
      setFile(null);
      return;
    }
    if (next.size > MAX_UPLOAD_BYTES) {
      setDropError(`File is larger than the ${MAX_UPLOAD_MB} MB limit.`);
      setFile(null);
      return;
    }
    setDropError(null);
    // Deliberately does NOT reset splitBySolutionId. The split choice belongs to the parser,
    // not the file, and the checkbox renders above the drop zone — so ticking it and then
    // dropping a file is the natural order, and clearing it here silently discarded the
    // request (one workbook instead of one per Solution ID/SAID, with no warning). It is
    // still reset on vendor and parser change below, where the capability really can vanish.
    setFile(next);
  }, []);

  async function submit() {
    if (isDellVendor) {
      setQuoteValidationRequested(true);
      if (!DELL_QUOTE_ID_PATTERN.test(quoteId)) return;
    } else if (!file) {
      return;
    }
    setUploadState('parsing');
    setDropError(null);
    const form = new FormData();
    if (isDellVendor) {
      form.set('quoteId', quoteId);
    } else if (file) {
      form.set('file', file);
    }
    form.set('vendor', vendor);
    form.set('parserSlug', parserSlug);
    form.set('fxRate', fxRate);
    form.set('margin', margin);
    if (imPercent) {
      form.set('imPercent', imPercent);
    }
    if (selectedParser?.supportsOnCost && onCostPct) {
      form.set('onCostPct', onCostPct);
    }
    if (selectedTemplate) {
      form.set('crmTemplate', selectedTemplate);
    }
    if (splitBySolutionId) {
      form.set('splitBySolutionId', 'true');
    }

    try {
      const result = await api.parse(form);
      setUploadState('parsed');
      await refresh();
      await loadHistory();

      const resolvedSlug = result.parserSlug ?? parserSlug;
      const resolvedParser = parsers.find((p) => p.slug === resolvedSlug);
      const guidanceHtml = parseUiConfig.guidanceByParserSlug[resolvedSlug] ?? null;
      const detectedFormat = isAutoParserSlug(parserSlug) ? (resolvedParser?.displayName ?? resolvedSlug) : null;
      setResultPending({
        blob: result.blob,
        filename: result.filename,
        validation: result.validation,
        currency: result.currency ?? '',
        computedTotal: result.computedTotal ?? '',
        quotedTotal: result.quotedTotal ?? '',
        cancelledLines: result.cancelledLines,
        hasRebateIneligibleItems: result.hasRebateIneligibleItems,
        guidanceHtml,
        detectedFormat,
        outputWorkbookCount: result.outputWorkbookCount,
      });
    } catch (caught) {
      setUploadState('idle');
      const fileTypeMessage = fileTypeErrorMessage(caught);
      if (isCurrencyError(caught)) {
        setCurrencyError(true);
      } else if (isDellApiError(caught)) {
        setDellApiError(errorMessage(caught));
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
    setQuoteId('');
    setQuoteValidationRequested(false);
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
            splitBySolutionId={splitBySolutionId}
            canSubmit={canSubmit}
            parsing={uploadState === 'parsing'}
            onVendor={(value) => {
              initialVendorResolved.current = true;
              setVendor(value);
              applyVendorDefaults(value);
              setFile(null);
              setQuoteId('');
              setQuoteValidationRequested(false);
              setDropError(null);
              setSplitBySolutionId(false);
              const vendorParsers = parsers.filter((item) => item.vendor === value);
              const defaultParser = defaultParserFor(vendorParsers);
              if (defaultParser) {
                setParserSlug(defaultParser.slug);
                setSelectedTemplate(defaultParser.availableTemplates[0] ?? '');
              } else {
                setParserSlug('');
                setSelectedTemplate('');
              }
            }}
            onParser={(slug) => {
              setParserSlug(slug);
              setSplitBySolutionId(false);
              const p = parsers.find((x) => x.slug === slug);
              setSelectedTemplate(p?.availableTemplates[0] ?? '');
            }}
            onFxRate={setFxRate}
            onMargin={setMargin}
            onImPercent={setImPercent}
            onOnCostPct={setOnCostPct}
            onTemplate={setSelectedTemplate}
            onSplitBySolutionId={setSplitBySolutionId}
            onSubmit={submit}
          />
          <section className="flex min-w-0 flex-1 flex-col">
            {isDellVendor ? (
              <DellQuoteInput
                quoteId={quoteId}
                state={uploadState}
                validationRequested={quoteValidationRequested}
                onQuoteId={(value) => {
                  setQuoteId(value);
                  setQuoteValidationRequested(false);
                }}
              />
            ) : (
              <Dropzone file={file} state={uploadState} error={dropError} onFile={handleFile} onClear={() => setFile(null)} />
            )}
            {!isDellVendor && (
              <div className="mt-2 flex justify-between">
                <span className="label-faint">Drag multiple files to batch parse</span>
              </div>
            )}
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
        <p className="label-faint mt-6 leading-relaxed">
          This application is a Proof of Concept (POC) environment used to test and validate new features for Ingram
          Micro production systems. All outputs are generated for testing purposes and should be independently verified.
          To maintain data security, all user-uploaded data is automatically purged at regular intervals.
        </p>
      </main>
      <Footer />
      <ToastStack toasts={toasts} dismiss={(id) => setToasts((items) => items.filter((item) => item.id !== id))} />
      {currencyError && (
        <CurrencyErrorModal onClose={() => setCurrencyError(false)} />
      )}
      {dellApiError && (
        <DellApiErrorModal message={dellApiError} onClose={() => setDellApiError(null)} />
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
          hasRebateIneligibleItems={resultPending.hasRebateIneligibleItems}
          guidanceHtml={resultPending.guidanceHtml}
          detectedFormat={resultPending.detectedFormat}
          outputWorkbookCount={resultPending.outputWorkbookCount}
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

function isDellApiError(error: unknown): boolean {
  if (error instanceof ApiError && typeof error.detail === 'object' && error.detail !== null) {
    return (error.detail as ApiErrorDetail).stage === 'dellApi';
  }
  return false;
}

// Wrong file-type selection: the server composes the full guidance message and tags it
// with stage "fileType". Returns the message to show, or null when not this error.
function fileTypeErrorMessage(error: unknown): string | null {
  if (error instanceof ApiError && typeof error.detail === 'object' && error.detail !== null) {
    const detail = error.detail as ApiErrorDetail;
    if (detail.stage === 'fileType') {
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
