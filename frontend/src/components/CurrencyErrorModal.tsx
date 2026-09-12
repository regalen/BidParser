import { XCircle } from 'lucide-react';

interface Props {
  onClose: () => void;
}

export function CurrencyErrorModal({ onClose }: Props) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="currency-err-title"
        aria-describedby="currency-err-body"
        className="w-full max-w-lg rounded-xl border border-red-300 bg-white shadow-2xl"
      >
        <div className="flex items-center gap-3 rounded-t-xl bg-red-50 px-6 py-4 border-b border-red-200">
          <XCircle className="h-6 w-6 shrink-0 text-red-500" aria-hidden="true" />
          <span id="currency-err-title" className="text-base font-bold text-red-800 uppercase tracking-wide">
            Unsupported Currency
          </span>
        </div>

        <div className="px-6 py-5">
          <p id="currency-err-body" className="text-sm leading-6 text-slate-700">
            The input file does not appear to be in AUD, please upload an AUD version of this quote.
          </p>
        </div>

        <div className="flex justify-end rounded-b-xl bg-slate-50 px-6 py-4 border-t border-slate-200">
          <button
            type="button"
            onClick={onClose}
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
