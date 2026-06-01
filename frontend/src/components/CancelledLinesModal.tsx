import { AlertTriangle } from 'lucide-react';

import type { CancelledLineInfo } from '../api/client';

interface Props {
  cancelledLines: CancelledLineInfo[];
  onAcknowledge: () => void;
}

export function CancelledLinesModal({ cancelledLines, onAcknowledge }: Props) {
  return (
    /* Backdrop */
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      {/* Dialog card */}
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="cancelled-warn-title"
        aria-describedby="cancelled-warn-body"
        className="w-full max-w-lg rounded-xl border border-amber-300 bg-white shadow-2xl"
      >
        {/* Header strip */}
        <div className="flex items-center gap-3 rounded-t-xl bg-amber-50 px-6 py-4 border-b border-amber-200">
          <AlertTriangle className="h-6 w-6 shrink-0 text-amber-500" aria-hidden="true" />
          <span id="cancelled-warn-title" className="text-base font-bold text-amber-800 uppercase tracking-wide">
            Warning
          </span>
        </div>

        {/* Body */}
        <div className="px-6 py-5">
          <p id="cancelled-warn-body" className="text-sm leading-6 text-slate-700">
            One or more lines on this PCR are flagged as cancelled and will be imported with a standard
            price. The downstream system will retrieve current pricing from SAP for these lines.
          </p>

          {/* Cancelled line list */}
          <ul className="mt-4 space-y-1 rounded-lg border border-slate-200 bg-slate-50 px-4 py-3 text-xs text-slate-600">
            {cancelledLines.map((cl) => (
              <li key={cl.line} className="flex gap-3">
                <span className="w-8 shrink-0 font-semibold text-slate-400">#{cl.line}</span>
                <span className="font-mono">{cl.vpn}</span>
              </li>
            ))}
          </ul>
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
