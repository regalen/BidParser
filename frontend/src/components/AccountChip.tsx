import { ChevronDown, LogOut, Settings } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useAuth } from '../auth/AuthContext';

export function AccountChip() {
  const { user, logout } = useAuth();
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();
  if (!user) return null;

  const initials = user.username.slice(0, 2).toUpperCase();

  return (
    <div className="relative">
      <button type="button" className="flex items-center gap-2 rounded-full border-[1.5px] border-ink bg-paper py-1 pl-1 pr-2.5" onClick={() => setOpen((value) => !value)}>
        <span className="grid h-[22px] w-[22px] place-items-center rounded-full border-[1.5px] border-ink bg-paper-tint text-[10px] font-semibold">{initials}</span>
        <span className="max-w-40 truncate text-xs font-semibold text-ink">{user.username}</span>
        <ChevronDown size={14} className="text-ink-mute" />
      </button>
      {open && (
        <div className="absolute right-0 z-20 mt-2 w-48 overflow-hidden rounded-lg border-[1.5px] border-ink bg-paper shadow-panel">
          {user.role === 'admin' && (
            <button
              type="button"
              className="flex w-full items-center gap-2 px-3 py-2 text-left text-xs font-semibold text-ink hover:bg-paper-tint"
              onClick={() => {
                setOpen(false);
                navigate('/settings');
              }}
            >
              <Settings size={14} />
              Settings
            </button>
          )}
          <button
            type="button"
            className="flex w-full items-center gap-2 px-3 py-2 text-left text-xs font-semibold text-ink hover:bg-paper-tint"
            onClick={async () => {
              await logout();
              navigate('/login');
            }}
          >
            <LogOut size={14} />
            Logout
          </button>
        </div>
      )}
    </div>
  );
}
