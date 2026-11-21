import { Bold, CheckCircle2, Italic, Link2, Megaphone, Save, Underline } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';

import { api, ApiError } from '../api/client';
import type { AnnouncementAdmin } from '../types';
import { renderAnnouncementHtml } from '../utils/announcementHtml';
import { announcementChangedEvent } from './AnnouncementBanner';

interface Draft {
  enabled: boolean;
  html: string;
}

/** Administrator editor for the announcement banner shown beneath every page header. */
export function AnnouncementEditor({ onDirtyChange }: { onDirtyChange(dirty: boolean): void }) {
  const [saved, setSaved] = useState<AnnouncementAdmin | null>(null);
  const [draft, setDraft] = useState<Draft>({ enabled: false, html: '' });
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const load = useCallback(() => {
    let active = true;
    setLoading(true);
    setLoadError(null);
    api.adminAnnouncement()
      .then((value) => {
        if (!active) return;
        setSaved(value);
        setDraft({ enabled: value.enabled, html: value.html });
      })
      .catch((caught) => {
        if (active) setLoadError(message(caught, 'Could not load the announcement.'));
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => { active = false; };
  }, []);

  useEffect(() => load(), [load]);

  const dirty = saved !== null && (draft.enabled !== saved.enabled || draft.html !== saved.html);
  useEffect(() => onDirtyChange(dirty), [dirty, onDirtyChange]);

  const preview = useMemo(() => renderAnnouncementHtml(draft.html), [draft.html]);

  function update(next: Partial<Draft>) {
    setDraft((current) => ({ ...current, ...next }));
    setNotice(null);
  }

  /** Wraps the selected text (or inserts an empty pair at the cursor) in an allowed tag. */
  function wrapSelection(open: string, close: string) {
    const textarea = textareaRef.current;
    if (!textarea) return;
    const { selectionStart: start, selectionEnd: end, value } = textarea;
    const next = value.slice(0, start) + open + value.slice(start, end) + close + value.slice(end);
    update({ html: next });
    requestAnimationFrame(() => {
      textarea.focus();
      textarea.setSelectionRange(start + open.length, end + open.length);
    });
  }

  function insertLink() {
    const url = window.prompt('Link address (https://, http:// or mailto:)', 'https://');
    if (!url) return;
    wrapSelection(`<a href="${url.trim().replace(/"/g, '&quot;')}">`, '</a>');
  }

  async function save() {
    setSaving(true);
    setError(null);
    setNotice(null);
    try {
      const value = await api.updateAnnouncement(draft.enabled, draft.html);
      setSaved(value);
      setDraft({ enabled: value.enabled, html: value.html });
      setNotice(value.enabled ? 'Announcement saved and shown to every user.' : 'Announcement saved. It is hidden from users.');
      window.dispatchEvent(new Event(announcementChangedEvent));
    } catch (caught) {
      setError(message(caught, 'Could not save the announcement.'));
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="card p-6" aria-labelledby="announcement-heading">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <h2 id="announcement-heading" className="font-bold text-slate-900">Announcement</h2>
            {dirty && <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold uppercase text-amber-700">Unsaved</span>}
          </div>
          <p className="mt-1 text-xs text-slate-500">
            Shown beneath the page header for every user. It reappears at each login (and every 24 hours) and after each save, even if a user dismissed it.
          </p>
        </div>
        <Megaphone className="h-5 w-5 text-slate-400" aria-hidden="true" />
      </div>

      {loading && <p className="mt-5 text-sm text-slate-500">Loading announcement…</p>}
      {!loading && loadError && (
        <div className="mt-5">
          <p className="text-sm text-red-600">{loadError}</p>
          <button type="button" className="button mt-3" onClick={load}>Retry</button>
        </div>
      )}

      {!loading && saved && (
        <>
          <label className="mt-5 flex items-center gap-3">
            <button
              type="button"
              role="switch"
              aria-checked={draft.enabled}
              onClick={() => update({ enabled: !draft.enabled })}
              className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-accent focus-visible:ring-offset-2 ${draft.enabled ? 'bg-accent' : 'bg-slate-300'}`}
            >
              <span className={`inline-block h-5 w-5 rounded-full bg-white shadow-sm transition-transform ${draft.enabled ? 'translate-x-5' : 'translate-x-0.5'}`} />
            </button>
            <span className="text-sm font-semibold text-slate-700">Show announcement</span>
          </label>

          <div className="mt-5 overflow-hidden rounded-lg border border-slate-200">
            <div className="flex items-center gap-1 border-b border-slate-200 bg-slate-50 px-2 py-1.5" role="toolbar" aria-label="Formatting">
              <ToolbarButton label="Bold" onClick={() => wrapSelection('<strong>', '</strong>')}><Bold className="h-4 w-4" /></ToolbarButton>
              <ToolbarButton label="Italic" onClick={() => wrapSelection('<em>', '</em>')}><Italic className="h-4 w-4" /></ToolbarButton>
              <ToolbarButton label="Underline" onClick={() => wrapSelection('<u>', '</u>')}><Underline className="h-4 w-4" /></ToolbarButton>
              <ToolbarButton label="Link" onClick={insertLink}><Link2 className="h-4 w-4" /></ToolbarButton>
            </div>
            <textarea
              ref={textareaRef}
              id="announcement-html"
              aria-labelledby="announcement-heading"
              aria-describedby="announcement-allowed"
              aria-invalid={Boolean(error)}
              className="block min-h-24 w-full resize-y border-0 px-3 py-2 font-mono text-xs leading-5 text-slate-800 focus:outline-none focus:ring-0"
              spellCheck
              maxLength={4096}
              placeholder='Scheduled maintenance on <strong>Saturday</strong>. <a href="https://example.invalid">Details</a>'
              value={draft.html}
              onChange={(event) => update({ html: event.target.value })}
            />
          </div>
          <p id="announcement-allowed" className="mt-2 text-xs text-slate-500">
            Allowed HTML: {saved.htmlAllowlist.join(', ')}. Links open in a new tab.
          </p>

          <div className="mt-5">
            <h3 className="label">Preview</h3>
            <div className="mt-2 rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
              {draft.html.trim()
                ? <p className="flex items-center justify-center text-sm text-slate-600">
                    <span className="me-3 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-slate-200" aria-hidden="true">
                      <Megaphone className="h-3.5 w-3.5 text-slate-500" />
                    </span>
                    <span>{preview}</span>
                  </p>
                : <p className="text-center text-sm text-slate-400">Nothing to preview yet.</p>}
            </div>
          </div>

          {saved.validationError && (
            <p role="alert" className="mt-3 text-sm text-amber-700">Stored announcement needs repair: {saved.validationError}</p>
          )}
          {error && <p role="alert" className="mt-3 text-sm text-red-600">{error}</p>}
          {notice && <p role="status" className="mt-3 flex items-center gap-2 text-sm text-emerald-700"><CheckCircle2 className="h-4 w-4" />{notice}</p>}

          <div className="mt-4 flex items-center justify-between gap-3">
            <span className="text-xs text-slate-500">{saved.updatedAt ? `Last saved ${new Date(saved.updatedAt).toLocaleString()}` : 'Not saved yet'}</span>
            <button type="button" className="button button-primary" disabled={saving || !dirty} onClick={() => void save()}>
              <Save className="h-4 w-4" />
              {saving ? 'Saving' : 'Save'}
            </button>
          </div>
        </>
      )}
    </section>
  );
}

function ToolbarButton({ label, onClick, children }: { label: string; onClick(): void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      onClick={onClick}
      className="inline-flex h-7 w-7 items-center justify-center rounded-md text-slate-500 transition-colors hover:bg-slate-200 hover:text-slate-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-accent"
    >
      {children}
    </button>
  );
}

function message(error: unknown, fallback: string): string {
  return error instanceof ApiError && typeof error.detail === 'string' ? error.detail : fallback;
}
