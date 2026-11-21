import { Megaphone, X } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';

import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { ActiveAnnouncement } from '../types';
import { renderAnnouncementHtml } from '../utils/announcementHtml';

const dismissedStorageKey = 'bidparser.announcementDismissed';
export const announcementChangedEvent = 'bidparser:announcement-changed';

// Every page mounts its own header, so remember the last answer per user. A page change then paints
// the banner immediately (no layout shift) while a fresh copy is fetched in the background.
let cache: { userId: number; value: ActiveAnnouncement } | null = null;

function readDismissed(): string | null {
  try {
    return window.localStorage.getItem(dismissedStorageKey);
  } catch {
    return null;
  }
}

function writeDismissed(key: string) {
  try {
    window.localStorage.setItem(dismissedStorageKey, key);
  } catch {
    // Storage can be unavailable (private mode, blocked site data); dismissal then lasts for this page only.
  }
}

/**
 * Administrator announcement shown beneath the page header. The server issues a dismissal key that
 * changes on every login, every 24 hours within a longer session, and whenever the announcement
 * is saved; dismissing remembers only that key, so the banner returns at the next of those events.
 */
export function AnnouncementBanner({ contentClassName }: { contentClassName: string }) {
  const { user } = useAuth();
  const userId = user && !user.mustChangePassword ? user.id : null;
  const [announcement, setAnnouncement] = useState<ActiveAnnouncement | null>(
    () => (cache && cache.userId === userId ? cache.value : null),
  );
  const [dismissed, setDismissed] = useState<string | null>(readDismissed);

  useEffect(() => {
    if (userId === null) {
      setAnnouncement(null);
      return;
    }
    let active = true;
    function load() {
      api.announcement()
        .then((value) => {
          if (!active) return;
          cache = { userId: userId!, value };
          setAnnouncement(value);
        })
        .catch(() => {
          // An announcement is never worth an error on the page; keep whatever was showing.
        });
    }
    load();
    window.addEventListener(announcementChangedEvent, load);
    return () => {
      active = false;
      window.removeEventListener(announcementChangedEvent, load);
    };
  }, [userId]);

  // Dismissing in one tab hides the banner in the user's other open tabs too.
  useEffect(() => {
    function onStorage(event: StorageEvent) {
      if (event.key === dismissedStorageKey) setDismissed(event.newValue);
    }
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, []);

  const content = useMemo(
    () => (announcement?.html ? renderAnnouncementHtml(announcement.html) : null),
    [announcement?.html],
  );

  if (!announcement?.html || !announcement.dismissalKey || dismissed === announcement.dismissalKey) return null;
  const key = announcement.dismissalKey;

  function dismiss() {
    writeDismissed(key);
    setDismissed(key);
  }

  return (
    <div role="region" aria-label="Announcement" className="border-t border-slate-200 bg-slate-50">
      <div className={`mx-auto flex w-full items-center gap-3 py-3 ${contentClassName}`}>
        <div className="flex flex-1 items-center justify-center">
          <p className="flex items-center text-sm font-normal text-slate-600">
            <span className="me-3 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-slate-200" aria-hidden="true">
              <Megaphone className="h-3.5 w-3.5 text-slate-500" />
            </span>
            <span className="announcement-content">{content}</span>
          </p>
        </div>
        <button
          type="button"
          onClick={dismiss}
          className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-slate-200 hover:text-slate-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-accent"
        >
          <X className="h-3.5 w-3.5" aria-hidden="true" />
          <span className="sr-only">Dismiss announcement</span>
        </button>
      </div>
    </div>
  );
}
