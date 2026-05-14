import { AlertTriangle, CheckCircle2, X } from 'lucide-react';

export interface ToastMessage {
  id: number;
  tone: 'success' | 'warning' | 'error';
  title: string;
  detail?: string;
}

const TONE_BORDER: Record<ToastMessage['tone'], string> = {
  success: 'border-l-emerald-500',
  warning: 'border-l-amber-500',
  error: 'border-l-red-500',
};

const ICON_COLOR: Record<ToastMessage['tone'], string> = {
  success: 'text-emerald-500',
  warning: 'text-amber-500',
  error: 'text-red-500',
};

export function ToastStack({ toasts, dismiss }: { toasts: ToastMessage[]; dismiss: (id: number) => void }) {
  return (
    <div className="fixed bottom-5 right-5 z-50 flex w-[min(420px,calc(100vw-32px))] flex-col gap-2">
      {toasts.map((toast) => (
        <div
          key={toast.id}
          className={
            'toast flex items-start gap-3 rounded-lg border border-slate-200 border-l-4 bg-white px-4 py-3 shadow-lg ' +
            TONE_BORDER[toast.tone]
          }
        >
          {toast.tone === 'success' ? (
            <CheckCircle2 className={'mt-0.5 h-5 w-5 shrink-0 ' + ICON_COLOR[toast.tone]} />
          ) : (
            <AlertTriangle className={'mt-0.5 h-5 w-5 shrink-0 ' + ICON_COLOR[toast.tone]} />
          )}
          <div className="min-w-0 flex-1">
            <div className="text-sm font-semibold text-slate-900">{toast.title}</div>
            {toast.detail && <div className="mt-0.5 text-xs leading-5 text-slate-600">{toast.detail}</div>}
          </div>
          <button
            type="button"
            className="text-slate-400 transition-colors hover:text-slate-600"
            onClick={() => dismiss(toast.id)}
            title="Dismiss"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
      ))}
    </div>
  );
}
