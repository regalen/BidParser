import { FileWarning } from 'lucide-react';

interface Props {
  message: string;
  onClose: () => void;
}

export function FileTypeErrorModal({ message, onClose }: Props) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="filetype-err-title"
        aria-describedby="filetype-err-body"
        className="w-full max-w-lg rounded-xl border border-amber-300 bg-white shadow-2xl"
      >
        <div className="flex items-center gap-3 rounded-t-xl bg-amber-50 px-6 py-4 border-b border-amber-200">
          <FileWarning className="h-6 w-6 shrink-0 text-amber-500" aria-hidden="true" />
          <span id="filetype-err-title" className="text-base font-bold text-amber-800 uppercase tracking-wide">
            Wrong File Type
          </span>
        </div>

        <div className="px-6 py-5">
          <p id="filetype-err-body" className="text-sm leading-6 text-slate-700">
            {message}
          </p>
        </div>

        <div className="flex justify-end rounded-b-xl bg-slate-50 px-6 py-4 border-t border-slate-200">
          <button type="button" onClick={onClose} className="button button-primary px-6" autoFocus>
            OK
          </button>
        </div>
      </div>
    </div>
  );
}
