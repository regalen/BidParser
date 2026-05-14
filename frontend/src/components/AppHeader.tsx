import { Link } from 'react-router-dom';

import { AccountChip } from './AccountChip';

export function AppHeader({ bare = false }: { bare?: boolean }) {
  return (
    <header className="flex h-14 items-center justify-between border-b-[1.5px] border-ink bg-paper px-7">
      <Link to="/dashboard" className="flex items-center gap-2.5 text-ink no-underline">
        <span className="grid h-7 w-7 place-items-center rounded-md border-[1.5px] border-ink bg-paper-tint text-sm font-bold">B</span>
        <span className="text-base font-bold tracking-normal">BidParser</span>
      </Link>
      {!bare && <AccountChip />}
    </header>
  );
}
