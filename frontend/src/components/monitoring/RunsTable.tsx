import { ChevronDown, ChevronRight, Download } from 'lucide-react';
import { Fragment, useState } from 'react';

import type { MonitoringRun } from '../../types';
import { relativeTime } from '../../utils/relativeTime';
import { CategoryBadge } from './CategoryBadge';
import { FailureRowDetail } from './FailureRowDetail';

function sourceHref(row: MonitoringRun): string {
  return row.kind === 'job'
    ? `/api/monitoring/jobs/${row.id}/source`
    : `/api/monitoring/failures/${row.id}/source`;
}

export function RunsTable({
  rows,
  onFilter,
}: {
  rows: MonitoringRun[];
  onFilter?: (key: 'vendor' | 'userId', value: string) => void;
}) {
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  function toggle(key: string) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  return (
    <table className="w-full text-left text-sm">
      <thead className="border-b border-slate-200 bg-slate-50 text-xs font-bold uppercase tracking-wider text-slate-500">
        <tr>
          <th className="w-10 px-4 py-3" />
          <th className="px-4 py-3">When</th>
          <th className="px-4 py-3">User</th>
          <th className="px-4 py-3">Vendor</th>
          <th className="px-4 py-3">File type</th>
          <th className="px-4 py-3">Filename</th>
          <th className="px-4 py-3">Status</th>
          <th className="px-4 py-3 text-right">Files</th>
        </tr>
      </thead>
      <tbody className="divide-y divide-slate-100">
        {rows.map((row) => {
          const key = `${row.kind}-${row.id}`;
          const isFailure = row.kind === 'failure';
          const isOpen = expanded.has(key);
          return (
            <Fragment key={key}>
              <tr className="transition-colors hover:bg-slate-50/50">
                <td className="px-4 py-3">
                  {isFailure ? (
                    <button
                      type="button"
                      onClick={() => toggle(key)}
                      aria-expanded={isOpen}
                      aria-label={isOpen ? 'Collapse details' : 'Expand details'}
                      className="rounded p-1 text-slate-500 hover:bg-slate-200 hover:text-slate-900"
                    >
                      {isOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                    </button>
                  ) : null}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-slate-600" title={new Date(row.created_at).toLocaleString()}>
                  {relativeTime(row.created_at)}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-slate-700">
                  {onFilter && row.user_id !== null ? (
                    <button
                      type="button"
                      onClick={() => onFilter('userId', String(row.user_id))}
                      title={`Filter by @${row.username}`}
                      className="hover:text-accent hover:underline"
                    >
                      {row.name ?? `@${row.username}`}
                    </button>
                  ) : row.name ? (
                    <span title={`@${row.username}`}>{row.name}</span>
                  ) : (
                    <span>@{row.username}</span>
                  )}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                  {onFilter ? (
                    <button
                      type="button"
                      onClick={() => onFilter('vendor', row.vendor)}
                      title={`Filter by ${row.vendor}`}
                      className="hover:text-accent hover:underline"
                    >
                      {row.vendor}
                    </button>
                  ) : (
                    row.vendor
                  )}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-slate-600">{row.parser_display_name}</td>
                <td className="max-w-[240px] truncate px-4 py-3 text-slate-600" title={row.source_filename}>
                  {row.source_filename}
                </td>
                <td className="whitespace-nowrap px-4 py-3">
                  <CategoryBadge category={row.status} />
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-right">
                  <div className="inline-flex items-center justify-end gap-3">
                    {row.source_available ? (
                      <a
                        href={sourceHref(row)}
                        download={row.source_filename}
                        className="inline-flex items-center gap-1 text-sm font-medium text-accent hover:underline"
                      >
                        <Download className="h-4 w-4" />
                        Input
                      </a>
                    ) : (
                      <span className="text-xs text-slate-400">Input purged</span>
                    )}
                    {row.kind === 'job' && row.output_available && (
                      <a
                        href={`/api/monitoring/jobs/${row.id}/output`}
                        className="inline-flex items-center gap-1 text-sm font-medium text-accent hover:underline"
                      >
                        <Download className="h-4 w-4" />
                        Output
                      </a>
                    )}
                  </div>
                </td>
              </tr>
              {isFailure && isOpen && (
                <tr>
                  <td colSpan={8} className="p-0">
                    <FailureRowDetail
                      failure={{
                        ...row,
                        category: row.status,
                        error_detail: row.error_detail ?? '',
                      }}
                    />
                  </td>
                </tr>
              )}
            </Fragment>
          );
        })}
      </tbody>
    </table>
  );
}
