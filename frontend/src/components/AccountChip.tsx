import { LogOut, Settings } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useAuth } from '../auth/AuthContext';

export function AccountChip() {
  const { user, logout } = useAuth();
  const [open, setOpen] = useState(false);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  useEffect(() => {
    if (!open) return;
    function onClickOutside(event: MouseEvent) {
      if (wrapperRef.current && !wrapperRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener('mousedown', onClickOutside);
    return () => document.removeEventListener('mousedown', onClickOutside);
  }, [open]);

  if (!user) return null;

  return (
    <div className="flex items-center gap-4" ref={wrapperRef}>
      {user.role === 'admin' && (
        <button
          type="button"
          className="hidden items-center gap-2 rounded-md px-3 py-2 text-slate-500 transition-colors hover:bg-slate-50 hover:text-slate-900 sm:inline-flex"
          onClick={() => navigate('/settings')}
        >
          <Settings className="h-4 w-4" />
          <span className="text-[11px] font-bold uppercase tracking-wider">Settings</span>
        </button>
      )}

      <div className="hidden h-8 w-px bg-slate-200 sm:block" />

      <div className="hidden text-right sm:block">
        <p className="text-xs font-bold leading-none text-slate-900">{user.name || user.username}</p>
        <p className="mt-1 text-[10px] font-bold uppercase tracking-widest text-slate-400">
          @{user.username}
        </p>
      </div>

      <button
        type="button"
        className="flex h-9 w-9 items-center justify-center rounded-full text-slate-400 transition-colors hover:bg-red-50 hover:text-red-500"
        onClick={async () => {
          setOpen(false);
          await logout();
          navigate('/login');
        }}
        title="Sign out"
      >
        <LogOut className="h-5 w-5" />
      </button>
    </div>
  );
}
