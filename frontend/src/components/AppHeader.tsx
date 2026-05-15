import { AlertTriangle, FileText } from 'lucide-react';
import { Link } from 'react-router-dom';

import { useAuth } from '../auth/AuthContext';
import { AccountChip } from './AccountChip';

export function AppHeader({ bare = false }: { bare?: boolean }) {
  const { user } = useAuth();

  return (
    <header className="sticky top-0 z-40 flex h-16 items-center justify-between border-b border-slate-200 bg-white px-8 shadow-sm">
      <div className="flex min-w-0 items-center gap-4">
        <Link to="/dashboard" className="flex items-center gap-3 transition-opacity hover:opacity-80">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-accent shadow-lg shadow-accent/20">
            <FileText className="h-5 w-5 text-white" />
          </div>
          <h1 className="text-lg font-semibold tracking-tight text-slate-900">BidParser</h1>
        </Link>
        {!bare && user && <FxRateBadge rate={user.fx_rate} updatedAt={user.fx_rate_updated_at} />}
      </div>
      {!bare && <AccountChip />}
    </header>
  );
}

function FxRateBadge({ rate, updatedAt }: { rate: string | null; updatedAt: string | null }) {
  const stale = isStale(updatedAt);
  const title = updatedAt
    ? `AUD:USD rate last updated ${new Date(updatedAt).toLocaleString()}`
    : 'AUD:USD rate has not been refreshed from Bloomberg yet.';

  return (
    <div
      className={
        'inline-flex shrink-0 items-center gap-2 rounded-md border px-2.5 py-1.5 text-[11px] font-bold uppercase tracking-wider ' +
        (stale ? 'border-amber-200 bg-amber-50 text-amber-700' : 'border-slate-200 bg-slate-50 text-slate-600')
      }
      title={title}
    >
      <span className="text-slate-400">AUD:USD</span>
      <span className="text-slate-900">{rate ?? 'Pending'}</span>
      {stale && <AlertTriangle className="h-3.5 w-3.5 text-amber-500" aria-label="Rate stale" />}
    </div>
  );
}

function isStale(updatedAt: string | null) {
  if (!updatedAt) return true;
  const timestamp = new Date(updatedAt).getTime();
  if (Number.isNaN(timestamp)) return true;
  return Date.now() - timestamp > 24 * 60 * 60 * 1000;
}
