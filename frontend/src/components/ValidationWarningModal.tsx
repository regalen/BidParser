import { AlertTriangle } from 'lucide-react';

interface Props {
  currency: string;
  computedTotal: string;
  quotedTotal: string;
  onAcknowledge: () => void;
}

export function ValidationWarningModal({ currency, computedTotal, quotedTotal, onAcknowledge }: Props) {
  return (
    /* Backdrop */
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      {/* Dialog card */}
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="val-warn-title"
        aria-describedby="val-warn-body"
        className="w-full max-w-lg rounded-xl border border-amber-300 bg-white shadow-2xl"
      >
        {/* Header strip */}
        <div className="flex items-center gap-3 rounded-t-xl bg-amber-50 px-6 py-4 border-b border-amber-200">
          <AlertTriangle className="h-6 w-6 shrink-0 text-amber-500" aria-hidden="true" />
          <span id="val-warn-title" className="text-base font-bold text-amber-800 uppercase tracking-wide">
            Warning
          </span>
        </div>

        {/* Body */}
        <div className="px-6 py-5">
          <p id="val-warn-body" className="text-sm leading-6 text-slate-700">
            Validation of total quote price against the line items extracted did not match. This indicates the quote
            was not parsed fully. Please check the output before using this to create a new quote.
          </p>

          {/* Totals detail */}
          <p className="mt-3 text-xs text-slate-500">
            Computed&nbsp;{currency}&nbsp;<strong className="text-slate-700">{computedTotal || '—'}</strong>
            &nbsp;·&nbsp;Quoted&nbsp;{currency}&nbsp;<strong className="text-slate-700">{quotedTotal || '—'}</strong>
          </p>
        </div>

        {/* Footer */}
        <div className="flex justify-end rounded-b-xl bg-slate-50 px-6 py-4 border-t border-slate-200">
          <button
            type="button"
            onClick={onAcknowledge}
            className="button button-primary px-6"
            autoFocus
          >
            OK
          </button>
        </div>
      </div>
    </div>
  );
}
