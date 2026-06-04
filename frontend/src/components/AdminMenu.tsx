import { Activity, BarChart3, FileText, Settings, Users } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useAuth } from '../auth/AuthContext';

interface MenuItem {
  label: string;
  path: string;
  icon: React.ComponentType<{ className?: string }>;
}

const ITEMS: MenuItem[] = [
  { label: 'Users', path: '/admin/users', icon: Users },
  { label: 'Report Types', path: '/admin/report-types', icon: FileText },
  { label: 'Metrics', path: '/admin/metrics', icon: BarChart3 },
  { label: 'Monitoring', path: '/admin/monitoring', icon: Activity },
];

export function AdminMenu() {
  const { user } = useAuth();
  const [open, setOpen] = useState(false);
  const [focusIndex, setFocusIndex] = useState(0);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const itemRefs = useRef<Array<HTMLButtonElement | null>>([]);
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

  useEffect(() => {
    if (open) {
      setFocusIndex(0);
      requestAnimationFrame(() => itemRefs.current[0]?.focus());
    }
  }, [open]);

  if (!user || user.role !== 'admin') return null;

  function handleSelect(item: MenuItem) {
    setOpen(false);
    navigate(item.path);
  }

  function handleKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') {
      event.preventDefault();
      setOpen(false);
      return;
    }
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      const next = (focusIndex + 1) % ITEMS.length;
      setFocusIndex(next);
      itemRefs.current[next]?.focus();
      return;
    }
    if (event.key === 'ArrowUp') {
      event.preventDefault();
      const next = (focusIndex - 1 + ITEMS.length) % ITEMS.length;
      setFocusIndex(next);
      itemRefs.current[next]?.focus();
      return;
    }
    if (event.key === 'Home') {
      event.preventDefault();
      setFocusIndex(0);
      itemRefs.current[0]?.focus();
      return;
    }
    if (event.key === 'End') {
      event.preventDefault();
      const last = ITEMS.length - 1;
      setFocusIndex(last);
      itemRefs.current[last]?.focus();
    }
  }

  return (
    <div className="relative" ref={wrapperRef} onKeyDown={handleKeyDown}>
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
          {ITEMS.map((item, index) => {
            const Icon = item.icon;
            return (
              <button
                key={item.path}
                ref={(el) => {
                  itemRefs.current[index] = el;
                }}
                role="menuitem"
                tabIndex={focusIndex === index ? 0 : -1}
                className="flex w-full items-center gap-3 px-4 py-2 text-sm text-slate-700 transition-colors hover:bg-slate-100 focus:bg-slate-100 focus:outline-none"
                onClick={() => handleSelect(item)}
              >
                <Icon className="h-4 w-4 text-slate-400" />
                <span className="font-medium">{item.label}</span>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
