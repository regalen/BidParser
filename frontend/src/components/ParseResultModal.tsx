import { AlertTriangle, CheckCircle2, Download, FileText } from 'lucide-react';

import type { CancelledLineInfo } from '../api/client';

interface Props {
  validation: 'match' | 'mismatch';
  currency: string;
  computedTotal: string;
  quotedTotal: string;
  validationWarnings: string[];
  cancelledLines: CancelledLineInfo[];
  hasRebateIneligibleItems: boolean;
  guidanceHtml?: string | null;
  detectedFormat?: string | null;
  outputWorkbookCount: number | null;
  onDownload: () => void;
  onClose: () => void;
}

export function ParseResultModal({
  validation,
  currency,
  computedTotal,
  quotedTotal,
  validationWarnings,
  cancelledLines,
  hasRebateIneligibleItems,
  guidanceHtml,
  detectedFormat,
  outputWorkbookCount,
  onDownload,
  onClose,
}: Props) {
  const hasMismatch = validation !== 'match';
  const hasCancelled = cancelledLines.length > 0;
  const hasIssues = hasMismatch || hasCancelled || hasRebateIneligibleItems;
  const isSplitArchive = outputWorkbookCount !== null;
  const hasMultipleWorkbooks = (outputWorkbookCount ?? 0) > 1;

  return (
    /* Backdrop */
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      {/* Dialog card */}
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="parse-result-title"
        aria-describedby="parse-result-body"
        className={`w-full max-w-2xl rounded-xl border bg-white shadow-2xl ${
          hasIssues ? 'border-amber-300' : 'border-emerald-300'
        }`}
      >
        {/* Header strip */}
        <div
          className={`flex items-center gap-3 rounded-t-xl px-6 py-4 border-b ${
            hasIssues ? 'bg-amber-50 border-amber-200' : 'bg-emerald-50 border-emerald-200'
          }`}
        >
          {hasIssues ? (
            <AlertTriangle className="h-6 w-6 shrink-0 text-amber-500" aria-hidden="true" />
          ) : (
            <CheckCircle2 className="h-6 w-6 shrink-0 text-emerald-500" aria-hidden="true" />
          )}
          <span
            id="parse-result-title"
            className={`text-base font-bold uppercase tracking-wide ${
              hasIssues ? 'text-amber-800' : 'text-emerald-800'
            }`}
          >
            {hasIssues ? 'Parsed with warnings' : 'Parse successful'}
          </span>
        </div>

        {/* Body */}
        <div id="parse-result-body" className="px-6 py-5">
          {!hasIssues && (
            <p className="text-sm leading-6 text-slate-700">
              {hasMultipleWorkbooks ? (
                <>
                  Your quote was parsed successfully. Download <strong>{outputWorkbookCount}</strong> workbooks (one
                  per Solution ID) as a .zip.
                </>
              ) : isSplitArchive ? (
                'Your quote was parsed successfully. Download the workbook as a .zip.'
              ) : (
                'Your quote was parsed successfully. Download the parsed workbook below.'
              )}
            </p>
          )}

          {hasMismatch && (
            <>
              <p className="text-sm leading-6 text-slate-700">
                {quotedTotal
                  ? 'The extracted line totals differ from the quoted total. Check the output before importing to CRM.'
                  : 'One or more extracted line totals differ from the source line totals. Check the affected lines before importing to CRM.'}
              </p>
              <p className="mt-3 text-xs text-slate-500">
                Computed&nbsp;{currency}&nbsp;<strong className="text-slate-700">{computedTotal || '—'}</strong>
                {quotedTotal && <>&nbsp;·&nbsp;Quoted&nbsp;{currency}&nbsp;<strong className="text-slate-700">{quotedTotal}</strong></>}
              </p>
              {validationWarnings.length > 0 && (
                <ul aria-label="Validation warnings" className="mt-3 max-h-48 list-disc overflow-y-auto pl-5 text-sm text-slate-700">
                  {validationWarnings.map((warning, index) => <li key={index}>{warning}</li>)}
                </ul>
              )}
            </>
          )}

          {hasCancelled && (
            <div className={hasMismatch ? 'mt-4' : ''}>
              <p className="text-sm leading-6 text-slate-700">
                One or more lines on this PCR are flagged as cancelled and will be imported with a standard
                price. The downstream system will retrieve current pricing from SAP for these lines.
              </p>
              <ul className="mt-4 space-y-1 rounded-lg border border-slate-200 bg-slate-50 px-4 py-3 text-xs text-slate-600">
                {cancelledLines.map((cl) => (
                  <li key={cl.line} className="flex gap-3">
                    <span className="w-8 shrink-0 font-semibold text-slate-400">#{cl.line}</span>
                    <span className="font-mono">{cl.vpn}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {hasRebateIneligibleItems && (
            <div className={hasMismatch || hasCancelled ? 'mt-4' : ''}>
              <p className="text-sm leading-6 text-slate-700">
                This quote contains items that are not eligible for Rebate/MDF from Dell due to special pricing.
                Review the comments on the impacted line items in your quote.
              </p>
            </div>
          )}

          {/* Split callout — the success copy above carries this when there are no issues,
              so only repeat it here when an issue block replaced that paragraph. */}
          {hasIssues && isSplitArchive && (
            <div className="mt-5 flex items-start gap-3 rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
              <Download className="mt-0.5 h-5 w-5 shrink-0 text-slate-500" aria-hidden="true" />
              <p className="text-sm leading-6 text-slate-700">
                {hasMultipleWorkbooks ? (
                  <>
                    The download is a .zip containing&nbsp;
                    <strong className="text-slate-900">{outputWorkbookCount}</strong>&nbsp;workbooks, one per
                    Solution ID.
                  </>
                ) : (
                  'The download is a .zip containing one workbook per Solution ID.'
                )}
              </p>
            </div>
          )}

          {/* Detected format callout */}
          {detectedFormat && detectedFormat.trim().length > 0 && (
            <div className="mt-5 flex items-start gap-3 rounded-lg border border-indigo-200 bg-indigo-50 px-4 py-3">
              <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-indigo-500" aria-hidden="true" />
              <p className="text-sm leading-6 text-slate-700">
                Detected file type:&nbsp;
                <strong className="text-slate-900">{detectedFormat}</strong>
              </p>
            </div>
          )}

          {guidanceHtml && guidanceHtml.trim().length > 0 && (
            <div className="mt-5 flex items-start gap-3 rounded-lg border border-sky-200 bg-sky-50 px-4 py-3">
              <FileText className="mt-0.5 h-5 w-5 shrink-0 text-sky-500" aria-hidden="true" />
              <div className="guidance-content text-sm leading-6 text-slate-700" dangerouslySetInnerHTML={{ __html: guidanceHtml }} />
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 rounded-b-xl bg-slate-50 px-6 py-4 border-t border-slate-200">
          <button type="button" onClick={onClose} className="button px-6">
            Close
          </button>
          <button type="button" onClick={onDownload} className="button button-primary px-6" autoFocus>
            <Download className="mr-2 h-4 w-4" aria-hidden="true" />
            {isSplitArchive ? 'Download .zip' : 'Download'}
          </button>
        </div>
      </div>
    </div>
  );
}
