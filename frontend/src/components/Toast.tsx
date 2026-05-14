import { AlertTriangle, CheckCircle2, X } from 'lucide-react';

export interface ToastMessage {
  id: number;
  tone: 'success' | 'warning' | 'error';
  title: string;
  detail?: string;
}

export function ToastStack({ toasts, dismiss }: { toasts: ToastMessage[]; dismiss: (id: number) => void }) {
  return (
    <div className="fixed bottom-5 right-5 z-50 flex w-[min(420px,calc(100vw-32px))] flex-col gap-2">
      {toasts.map((toast) => (
        <div
          key={toast.id}
          className={[
            'toast flex items-start gap-3 rounded-lg border-[1.5px] bg-paper px-4 py-3 shadow-panel',
            toast.tone === 'success' ? 'border-emerald-500' : toast.tone === 'warning' ? 'border-amber-500' : 'border-red-500',
          ].join(' ')}
        >
          {toast.tone === 'success' ? <CheckCircle2 size={18} className="mt-0.5 text-emerald-600" /> : <AlertTriangle size={18} className="mt-0.5 text-amber-600" />}
          <div className="min-w-0 flex-1">
            <div className="text-sm font-semibold text-ink">{toast.title}</div>
            {toast.detail && <div className="mt-0.5 text-xs leading-5 text-ink-soft">{toast.detail}</div>}
          </div>
          <button type="button" className="text-ink-mute" onClick={() => dismiss(toast.id)} title="Dismiss">
            <X size={15} />
          </button>
        </div>
      ))}
    </div>
  );
}
