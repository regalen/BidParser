import { ChevronDown, ChevronRight, Download } from 'lucide-react';
import { Fragment, useState } from 'react';

import type { FailedParseJob } from '../../types';
import { relativeTime } from '../../utils/relativeTime';
import { CategoryBadge } from './CategoryBadge';
import { FailureRowDetail } from './FailureRowDetail';

export function FailuresTable({ rows }: { rows: FailedParseJob[] }) {
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  function toggle(id: number) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
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
          <th className="px-4 py-3">Category</th>
          <th className="px-4 py-3 text-right">Source</th>
        </tr>
      </thead>
      <tbody className="divide-y divide-slate-100">
        {rows.map((row) => {
          const isOpen = expanded.has(row.id);
          return (
            <Fragment key={row.id}>
              <tr className="transition-colors hover:bg-slate-50/50">
                <td className="px-4 py-3">
                  <button
                    type="button"
                    onClick={() => toggle(row.id)}
                    aria-expanded={isOpen}
                    aria-label={isOpen ? 'Collapse details' : 'Expand details'}
                    className="rounded p-1 text-slate-500 hover:bg-slate-200 hover:text-slate-900"
                  >
                    {isOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                  </button>
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-slate-600" title={new Date(row.created_at).toLocaleString()}>
                  {relativeTime(row.created_at)}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-slate-700">
                  {row.name ? (
                    <span title={`@${row.username}`}>{row.name}</span>
                  ) : (
                    <span>@{row.username}</span>
                  )}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-slate-600">{row.vendor}</td>
                <td className="whitespace-nowrap px-4 py-3 text-slate-600">{row.parser_display_name}</td>
                <td className="max-w-[240px] truncate px-4 py-3 text-slate-600" title={row.source_filename}>
                  {row.source_filename}
                </td>
                <td className="whitespace-nowrap px-4 py-3">
                  <CategoryBadge category={row.category} />
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-right">
                  {row.source_available ? (
                    <a
                      href={`/api/monitoring/failures/${row.id}/source`}
                      download={row.source_filename}
                      className="inline-flex items-center gap-1 text-sm font-medium text-accent hover:underline"
                    >
                      <Download className="h-4 w-4" />
                      Download
                    </a>
                  ) : (
                    <span className="text-xs text-slate-400">Purged</span>
                  )}
                </td>
              </tr>
              {isOpen && (
                <tr>
                  <td colSpan={8} className="p-0">
                    <FailureRowDetail failure={row} />
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
