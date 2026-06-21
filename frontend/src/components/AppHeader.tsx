import { Link } from 'react-router-dom';

import { AccountChip } from './AccountChip';
import { AdminMenu } from './AdminMenu';

export function AppHeader({ bare = false }: { bare?: boolean }) {
  return (
    <header className="sticky top-0 z-40 flex h-16 items-center justify-between border-b border-slate-200 bg-white px-8 shadow-sm">
      <Link to="/dashboard" className="flex items-center gap-3 transition-opacity hover:opacity-80">
        <img src="/logo.png" alt="" className="h-8 w-8 rounded-lg shadow-lg shadow-accent/20" />
        <h1 className="text-lg font-semibold tracking-tight text-slate-900">BidParser</h1>
      </Link>
      {!bare && (
        <div className="flex items-center gap-4">
          <AdminMenu />
          <AccountChip />
        </div>
      )}
    </header>
  );
}
