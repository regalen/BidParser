import { Activity, AlertCircle } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { api } from '../../api/client';
import { AppHeader } from '../../components/AppHeader';
import { Footer } from '../../components/Footer';
import { DateRangeControl } from '../../components/metrics/DateRangeControl';
import { FilterChips } from '../../components/metrics/FilterChips';
import { RunsTable } from '../../components/monitoring/RunsTable';
import type { MonitoringRun } from '../../types';

const PAGE_SIZE = 25;

const STATUS_OPTIONS = [
  { value: '', label: 'All statuses' },
  { value: 'success', label: 'Success' },
  { value: 'validation_mismatch', label: 'Validation mismatch' },
  { value: 'magic_byte_mismatch', label: 'Magic-byte mismatch' },
  { value: 'parser_error', label: 'Parser error' },
  { value: 'unhandled_exception', label: 'Unhandled exception' },
];

export function MonitoringPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [runs, setRuns] = useState<MonitoringRun[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const pageParam = Number(searchParams.get('page') ?? '1');
  const page = Number.isFinite(pageParam) && pageParam > 0 ? Math.floor(pageParam) : 1;
  const offset = (page - 1) * PAGE_SIZE;
  const status = searchParams.get('status') ?? '';

  // Filters live in the URL; rebuild the query string the API expects.
  const queryKey = useMemo(() => {
    const params = new URLSearchParams();
    for (const key of ['from', 'to', 'vendor', 'userId', 'parserSlug', 'status']) {
      const value = searchParams.get(key);
      if (value) params.set(key, value);
    }
    params.set('limit', String(PAGE_SIZE));
    params.set('offset', String(offset));
    return params.toString();
  }, [searchParams, offset]);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);
    api
      .monitoringRuns(new URLSearchParams(queryKey))
      .then((res) => {
        if (!active) return;
        setRuns(res.items);
        setTotal(res.total);
      })
      .catch((err: unknown) => {
        if (active) setError(err instanceof Error ? err.message : 'Failed to load runs.');
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [queryKey]);

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  function goToPage(target: number) {
    const clamped = Math.max(1, Math.min(totalPages, target));
    const next = new URLSearchParams(searchParams);
    if (clamped === 1) next.delete('page');
    else next.set('page', String(clamped));
    setSearchParams(next);
  }

  function setFilter(key: string, value: string) {
    const next = new URLSearchParams(searchParams);
    if (value) next.set(key, value);
    else next.delete(key);
    next.delete('page'); // any filter change resets to the first page
    setSearchParams(next);
  }

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <AppHeader />
      <main className="mx-auto w-full max-w-7xl flex-1 px-6 py-8 lg:px-8">
        <div className="flex flex-col items-start justify-between gap-4 sm:flex-row sm:items-end">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight text-slate-900">Parser runs</h1>
            <p className="mt-1 text-sm text-slate-500">
              Every parse attempt across all users — successes and failures. Source and output files are retained for
              the parse-history window.
            </p>
          </div>
          <div className="flex items-center gap-2 text-sm text-slate-500">
            <Activity className="h-4 w-4" />
            <span className="font-medium">{total} total</span>
          </div>
        </div>

        <div className="mt-6 flex flex-wrap items-center gap-4">
          <div className="flex flex-wrap items-center gap-2">
            <label className="label" htmlFor="run-status">Status</label>
            <select
              id="run-status"
              className="field w-48 !min-h-[32px] !py-1 !text-sm"
              value={status}
              onChange={(event) => setFilter('status', event.target.value)}
            >
              {STATUS_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>
          <DateRangeControl />
        </div>

        <FilterChips data={null} />

        {error && (
          <div className="mt-4 flex items-center gap-2 rounded-xl border border-red-100 bg-red-50 px-4 py-3 text-xs font-bold text-red-600">
            <AlertCircle className="h-4 w-4" />
            {error}
          </div>
        )}

        <div className="mt-6 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
          <div className="overflow-x-auto">
            {runs.length === 0 ? (
              <div className="flex flex-col items-center gap-1 px-4 py-12 text-center">
                <p className="text-sm font-medium text-slate-700">
                  {loading ? 'Loading runs…' : 'No parser runs match these filters.'}
                </p>
                {!loading && !error && (
                  <p className="text-xs text-slate-500">
                    Rows are purged alongside their files at the retention cutoff.
                  </p>
                )}
              </div>
            ) : (
              <RunsTable rows={runs} onFilter={setFilter} />
            )}
          </div>

          {totalPages > 1 && (
            <div className="flex items-center justify-between border-t border-slate-200 bg-slate-50 px-4 py-3">
              <span className="text-sm text-slate-500">
                Page <span className="font-medium text-slate-900">{page}</span> of{' '}
                <span className="font-medium text-slate-900">{totalPages}</span>
              </span>
              <div className="flex gap-2">
                <button
                  type="button"
                  className="button"
                  disabled={page === 1 || loading}
                  onClick={() => goToPage(page - 1)}
                >
                  Previous
                </button>
                <button
                  type="button"
                  className="button"
                  disabled={page === totalPages || loading}
                  onClick={() => goToPage(page + 1)}
                >
                  Next
                </button>
              </div>
            </div>
          )}
        </div>
      </main>
      <Footer />
    </div>
  );
}
