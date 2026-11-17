import { Link } from 'react-router';

import { AccountChip } from './AccountChip';
import { AdminMenu } from './AdminMenu';

/**
 * `contentClassName` must match the page's <main> max width and horizontal padding, so the logo
 * lines up with the content's left edge and the account controls with its right edge.
 */
export function AppHeader({
  bare = false,
  contentClassName = 'max-w-7xl px-6 lg:px-8',
}: {
  bare?: boolean;
  contentClassName?: string;
}) {
  return (
    <header className="sticky top-0 z-40 border-b border-slate-200 bg-white shadow-xs">
      <div className={`mx-auto flex h-16 w-full items-center justify-between ${contentClassName}`}>
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
      </div>
    </header>
  );
}
