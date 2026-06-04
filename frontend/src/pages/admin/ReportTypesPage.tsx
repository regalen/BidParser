import { AlertCircle, Check, FileText, X } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';

import { api, ApiError } from '../../api/client';
import { AppHeader } from '../../components/AppHeader';
import { Footer } from '../../components/Footer';
import type { ParserInfo } from '../../types';

function apiErrorMessage(caught: unknown, fallback: string): string {
  return caught instanceof ApiError && typeof (caught as ApiError).detail === 'string'
    ? ((caught as ApiError).detail as string)
    : fallback;
}

export function ReportTypesPage() {
  const [parsers, setParsers] = useState<ParserInfo[]>([]);
  // Local draft values keyed by parser slug. Seeded from the saved report types.
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [savingSlug, setSavingSlug] = useState<string | null>(null);
  const [savedSlug, setSavedSlug] = useState<string | null>(null);
  const [error, setError] = useState('');

  async function load() {
    const items = await api.parsers();
    setParsers(items);
    setDrafts(Object.fromEntries(items.map((p) => [p.slug, p.report_type ?? ''])));
  }

  useEffect(() => {
    void load();
  }, []);

  const byVendor = useMemo(() => {
    const groups = new Map<string, ParserInfo[]>();
    for (const p of parsers) {
      const list = groups.get(p.vendor) ?? [];
      list.push(p);
      groups.set(p.vendor, list);
    }
    return Array.from(groups.entries());
  }, [parsers]);

  function savedValue(slug: string): string {
    return parsers.find((p) => p.slug === slug)?.report_type ?? '';
  }

  async function save(slug: string) {
    setError('');
    setSavingSlug(slug);
    try {
      const value = (drafts[slug] ?? '').trim();
      await api.updateReportType(slug, value);
      // Reflect the saved value locally so the dirty indicator clears.
      setParsers((prev) => prev.map((p) => (p.slug === slug ? { ...p, report_type: value || null } : p)));
      setDrafts((prev) => ({ ...prev, [slug]: value }));
      setSavedSlug(slug);
      window.setTimeout(() => setSavedSlug((s) => (s === slug ? null : s)), 1800);
    } catch (caught) {
      setError(apiErrorMessage(caught, 'Could not save report type.'));
    } finally {
      setSavingSlug(null);
    }
  }

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <AppHeader />
      <main className="mx-auto w-full max-w-5xl flex-1 px-6 py-8 lg:px-8">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900">Report Types</h1>
          <p className="mt-1 text-sm text-slate-500">
            Configure the report type users are told to use when sending each quote to the customer. Leave a field
            blank to show no guidance for that combination.
          </p>
        </div>

        {error && (
          <div className="mt-6 flex items-center gap-2 rounded-xl border border-red-100 bg-red-50 px-4 py-3 text-xs font-bold text-red-600">
            <AlertCircle className="h-4 w-4" />
            {error}
            <button
              type="button"
              className="ml-auto flex h-6 w-6 items-center justify-center rounded-full text-red-400 hover:bg-red-100"
              onClick={() => setError('')}
            >
              <X className="h-3 w-3" />
            </button>
          </div>
        )}

        <div className="mt-8 space-y-8">
          {byVendor.map(([vendor, items]) => (
            <section key={vendor}>
              <h2 className="text-xs font-bold uppercase tracking-wider text-slate-400">{vendor}</h2>
              <div className="mt-3 divide-y divide-slate-100 rounded-xl border border-slate-200 bg-white shadow-sm">
                {items.map((p) => {
                  const draft = drafts[p.slug] ?? '';
                  const dirty = draft.trim() !== savedValue(p.slug);
                  return (
                    <div key={p.slug} className="flex flex-col gap-3 p-5 sm:flex-row sm:items-center">
                      <div className="flex min-w-0 flex-1 items-center gap-3">
                        <FileText className="h-5 w-5 shrink-0 text-slate-300" />
                        <div className="min-w-0">
                          <h3 className="truncate font-semibold text-slate-900">{p.display_name}</h3>
                          <p className="truncate text-[11px] text-slate-400">{p.slug}</p>
                        </div>
                      </div>
                      <div className="flex items-center gap-2 sm:w-96">
                        <input
                          className="field flex-1"
                          type="text"
                          maxLength={512}
                          placeholder="No report type configured"
                          value={draft}
                          onChange={(e) => setDrafts((prev) => ({ ...prev, [p.slug]: e.target.value }))}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter' && dirty) void save(p.slug);
                          }}
                        />
                        <button
                          type="button"
                          className="button button-primary shrink-0"
                          disabled={!dirty || savingSlug === p.slug}
                          onClick={() => void save(p.slug)}
                        >
                          {savedSlug === p.slug ? <Check className="h-3.5 w-3.5" /> : 'Save'}
                        </button>
                      </div>
                    </div>
                  );
                })}
              </div>
            </section>
          ))}
        </div>
      </main>
      <Footer />
    </div>
  );
}
