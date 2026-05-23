import { Settings, Users, BarChart3, Activity } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

export function AdminMenu() {
  const { user } = useAuth();
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
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setOpen(false);
      }
    }
    document.addEventListener('mousedown', onClickOutside);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('mousedown', onClickOutside);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  if (!user || user.role !== 'admin') return null;

  return (
    <div className="relative" ref={wrapperRef}>
      <button
        type="button"
        className="icon-button"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((prev) => !prev)}
      >
        <Settings className="h-5 w-5" />
      </button>

      {open && (
        <div
          role="menu"
          className="absolute right-0 mt-2 w-48 origin-top-right rounded-md bg-white py-1 shadow-lg ring-1 ring-black ring-opacity-5 focus:outline-none"
        >
          <button
            role="menuitem"
            className="flex w-full items-center gap-3 px-4 py-2 text-sm text-slate-700 transition-colors hover:bg-slate-100"
            onClick={() => {
              setOpen(false);
              navigate('/admin/users');
            }}
          >
            <Users className="h-4 w-4 text-slate-400" />
            <span className="font-medium">Users</span>
          </button>
          <button
            role="menuitem"
            className="flex w-full items-center gap-3 px-4 py-2 text-sm text-slate-700 transition-colors hover:bg-slate-100"
            onClick={() => {
              setOpen(false);
              navigate('/admin/metrics');
            }}
          >
            <BarChart3 className="h-4 w-4 text-slate-400" />
            <span className="font-medium">Metrics</span>
          </button>
          <button
            role="menuitem"
            className="flex w-full items-center gap-3 px-4 py-2 text-sm text-slate-700 transition-colors hover:bg-slate-100"
            onClick={() => {
              setOpen(false);
              navigate('/admin/monitoring');
            }}
          >
            <Activity className="h-4 w-4 text-slate-400" />
            <span className="font-medium">Monitoring</span>
          </button>
        </div>
      )}
    </div>
  );
}
