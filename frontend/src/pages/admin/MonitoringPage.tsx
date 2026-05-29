import { Activity, AlertCircle } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { api } from '../../api/client';
import { AppHeader } from '../../components/AppHeader';
import { Footer } from '../../components/Footer';
import { FailuresTable } from '../../components/monitoring/FailuresTable';
import type { FailedParseJob } from '../../types';

const PAGE_SIZE = 25;

export function MonitoringPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [failures, setFailures] = useState<FailedParseJob[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const pageParam = Number(searchParams.get('page') ?? '1');
  const page = Number.isFinite(pageParam) && pageParam > 0 ? Math.floor(pageParam) : 1;
  const offset = (page - 1) * PAGE_SIZE;

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);
    api
      .monitoringFailures(PAGE_SIZE, offset)
      .then((res) => {
        if (!active) return;
        setFailures(res.items);
        setTotal(res.total);
      })
      .catch((err: unknown) => {
        if (active) setError(err instanceof Error ? err.message : 'Failed to load failures.');
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [offset]);

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  function goToPage(target: number) {
    const clamped = Math.max(1, Math.min(totalPages, target));
    const next = new URLSearchParams(searchParams);
    if (clamped === 1) next.delete('page');
    else next.set('page', String(clamped));
    setSearchParams(next);
  }

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <AppHeader />
      <main className="mx-auto w-full max-w-7xl flex-1 px-6 py-8 lg:px-8">
        <div className="flex items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight text-slate-900">Failed parser runs</h1>
            <p className="mt-1 text-sm text-slate-500">
              Forensic view of post-save parse failures. Rows and source files are retained for the same window as parse history.
            </p>
          </div>
          <div className="flex items-center gap-2 text-sm text-slate-500">
            <Activity className="h-4 w-4" />
            <span className="font-medium">{total} total</span>
          </div>
        </div>

        {error && (
          <div className="mt-6 flex items-center gap-2 rounded-xl border border-red-100 bg-red-50 px-4 py-3 text-xs font-bold text-red-600">
            <AlertCircle className="h-4 w-4" />
            {error}
          </div>
        )}

        <div className="mt-8 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
          <div className="overflow-x-auto">
            {failures.length === 0 ? (
              <div className="flex flex-col items-center gap-1 px-4 py-12 text-center">
                <p className="text-sm font-medium text-slate-700">
                  {loading ? 'Loading failures…' : 'No failed parses recorded.'}
                </p>
                {!loading && !error && (
                  <p className="text-xs text-slate-500">
                    Rows are purged alongside the source file at the retention cutoff.
                  </p>
                )}
              </div>
            ) : (
              <FailuresTable rows={failures} />
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
