import { Activity, Download } from 'lucide-react';
import { useEffect, useState } from 'react';

import { api } from '../../api/client';
import { AppHeader } from '../../components/AppHeader';
import { Footer } from '../../components/Footer';
import type { FailedParseJob } from '../../types';

const MAX_PAGE_SIZE = 25;

export function MonitoringPage() {
  const [failures, setFailures] = useState<FailedParseJob[]>([]);
  const [total, setTotal] = useState(0);
  const [offset, setOffset] = useState(0);
  const [loading, setLoading] = useState(true);

  async function loadPage(newOffset: number) {
    setLoading(true);
    try {
      const res = await api.monitoringFailures(MAX_PAGE_SIZE, newOffset);
      setFailures(res.items);
      setTotal(res.total);
      setOffset(newOffset);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadPage(0);
  }, []);

  const totalPages = Math.max(1, Math.ceil(total / MAX_PAGE_SIZE));
  const currentPage = Math.floor(offset / MAX_PAGE_SIZE) + 1;

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <AppHeader />
      <main className="mx-auto w-full max-w-7xl flex-1 px-6 py-8 lg:px-8">
        <div className="flex items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-slate-900">Monitoring Ledger</h1>
            <p className="mt-1 text-sm text-slate-500">View and debug extraction failures across the system.</p>
          </div>
          <div className="flex items-center gap-2 text-sm text-slate-500">
            <Activity className="h-4 w-4" />
            <span className="font-medium">{total} total failures</span>
          </div>
        </div>

        <div className="mt-8 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-slate-200 bg-slate-50 text-xs font-bold uppercase tracking-wider text-slate-500">
                <tr>
                  <th className="px-4 py-3">Timestamp</th>
                  <th className="px-4 py-3">User</th>
                  <th className="px-4 py-3">Parser</th>
                  <th className="px-4 py-3">Filename</th>
                  <th className="px-4 py-3">Category</th>
                  <th className="px-4 py-3">Message</th>
                  <th className="px-4 py-3 text-right">Source</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {failures.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="px-4 py-8 text-center text-sm text-slate-500">
                      {loading ? 'Loading failures...' : 'No failed parse jobs recorded.'}
                    </td>
                  </tr>
                ) : (
                  failures.map((row) => (
                    <tr key={row.id} className="transition-colors hover:bg-slate-50/50">
                      <td className="whitespace-nowrap px-4 py-3 font-medium text-slate-900">
                        {new Date(row.created_at).toLocaleString()}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                        <span title={row.name || row.username}>
                          @{row.username}
                        </span>
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                        {row.parser_display_name}
                      </td>
                      <td className="max-w-[200px] truncate px-4 py-3 text-slate-600" title={row.source_filename}>
                        {row.source_filename}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                        <span className="inline-flex items-center rounded-full bg-red-50 px-2 py-0.5 text-xs font-medium text-red-700 ring-1 ring-inset ring-red-600/10">
                          {row.category}
                        </span>
                      </td>
                      <td className="max-w-[300px] truncate px-4 py-3 text-slate-500" title={row.message || row.error_detail || ''}>
                        {row.message || 'No detail provided.'}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-right">
                        {row.source_available ? (
                          <a
                            href={`/api/monitoring/failures/${row.id}/source`}
                            download
                            className="inline-flex items-center gap-1 text-sm font-medium text-accent hover:underline"
                          >
                            <Download className="h-4 w-4" />
                            File
                          </a>
                        ) : (
                          <span className="text-xs text-slate-400">Purged</span>
                        )}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
          
          {totalPages > 1 && (
            <div className="flex items-center justify-between border-t border-slate-200 bg-slate-50 px-4 py-3">
              <span className="text-sm text-slate-500">
                Page <span className="font-medium text-slate-900">{currentPage}</span> of{' '}
                <span className="font-medium text-slate-900">{totalPages}</span>
              </span>
              <div className="flex gap-2">
                <button
                  type="button"
                  className="button"
                  disabled={currentPage === 1 || loading}
                  onClick={() => loadPage(offset - MAX_PAGE_SIZE)}
                >
                  Previous
                </button>
                <button
                  type="button"
                  className="button"
                  disabled={currentPage === totalPages || loading}
                  onClick={() => loadPage(offset + MAX_PAGE_SIZE)}
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
