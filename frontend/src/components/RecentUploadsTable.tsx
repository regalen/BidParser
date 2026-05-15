import { ChevronLeft, ChevronRight, Download, FileDown, Search, X } from 'lucide-react';
import { useEffect, useRef } from 'react';

import type { HistoryRow } from '../types';

export function RecentUploadsTable({
  rows,
  total,
  page,
  pageSize,
  onPage,
  onPageSize,
  query,
  onQuery,
}: {
  rows: HistoryRow[];
  total: number;
  page: number;
  pageSize: number;
  onPage: (page: number) => void;
  onPageSize: (pageSize: number) => void;
  query: string;
  onQuery: (value: string) => void;
}) {
  const tableRef = useRef<HTMLElement | null>(null);
  const pageCount = Math.max(1, Math.ceil(total / pageSize));
  const start = total === 0 ? 0 : page * pageSize + 1;
  const end = Math.min(total, page * pageSize + rows.length);
  const pageButtons = visiblePages(page, pageCount);

  useEffect(() => {
    const table = tableRef.current;
    if (!table || typeof ResizeObserver === 'undefined') return;

    const observer = new ResizeObserver(([entry]) => {
      const availableBodyHeight = entry.contentRect.height - 124;
      const nextPageSize = Math.max(1, Math.floor(availableBodyHeight / 48));
      if (nextPageSize !== pageSize) {
        onPageSize(nextPageSize);
      }
    });
    observer.observe(table);
    return () => observer.disconnect();
  }, [onPageSize, pageSize]);

  return (
    <section ref={tableRef} className="card mt-5 flex min-h-[360px] flex-1 flex-col overflow-hidden">
      <div className="relative flex items-center justify-center border-b border-slate-200 bg-slate-50 px-4 py-3">
        <span className="label absolute left-4 top-1/2 -translate-y-1/2">Recent uploads</span>
        <div className="relative w-1/2">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-400" />
          <input
            type="text"
            value={query}
            onChange={(event) => onQuery(event.target.value)}
            placeholder="Search by file name"
            aria-label="Search recent uploads by file name"
            className="h-8 w-full rounded-md border border-slate-200 bg-white pl-8 pr-8 text-[13px] text-slate-700 placeholder:text-slate-400 focus:border-accent focus:outline-none focus:ring-1 focus:ring-accent"
          />
          {query && (
            <button
              type="button"
              onClick={() => onQuery('')}
              aria-label="Clear search"
              className="absolute right-1.5 top-1/2 flex h-5 w-5 -translate-y-1/2 items-center justify-center rounded text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
            >
              <X className="h-3 w-3" />
            </button>
          )}
        </div>
      </div>
      <div className="hidden grid-cols-[minmax(180px,2fr)_0.9fr_1.2fr_0.8fr_0.7fr_0.7fr_84px] gap-3 border-b border-slate-200 bg-slate-50 px-4 py-2 md:grid">
        <span className="label-faint">File name</span>
        <span className="label-faint">Vendor</span>
        <span className="label-faint">File type</span>
        <span className="label-faint text-right">FX rate</span>
        <span className="label-faint text-right">Margin</span>
        <span className="label-faint text-right">When</span>
        <span className="label-faint text-right">Files</span>
      </div>
      <div className="min-h-0 flex-1 overflow-hidden">
        {rows.length === 0 ? (
          <div className="grid h-full min-h-48 place-items-center px-6 text-center">
            {query ? (
              <div>
                <div className="label">No matches</div>
                <div className="mt-2 text-sm text-slate-500">
                  No uploads match <span className="font-semibold text-slate-700">"{query}"</span>.
                </div>
              </div>
            ) : (
              <div>
                <div className="label">No uploads yet</div>
                <div className="mt-2 text-sm text-slate-500">Parsed files will appear here after the first download.</div>
              </div>
            )}
          </div>
        ) : (
          rows.map((row, index) => <UploadRow key={row.id} row={row} last={index === rows.length - 1} />)
        )}
      </div>
      <div className="flex items-center justify-between border-t border-slate-200 bg-slate-50 px-4 py-2.5">
        <span className="label-faint">
          Showing {start} - {end} of {total}
        </span>
        <div className="flex items-center gap-1">
          <button
            type="button"
            className="flex h-7 w-7 items-center justify-center rounded-md border border-slate-200 bg-white text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40"
            disabled={page === 0}
            onClick={() => onPage(page - 1)}
            title="Previous page"
          >
            <ChevronLeft className="h-3.5 w-3.5" />
          </button>
          {pageButtons.map((pageIndex) => (
            <button
              key={pageIndex}
              type="button"
              className={
                'h-7 min-w-7 rounded-md border px-2 text-[11px] font-bold transition-colors ' +
                (pageIndex === page
                  ? 'border-accent bg-accent text-white'
                  : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-100')
              }
              onClick={() => onPage(pageIndex)}
            >
              {pageIndex + 1}
            </button>
          ))}
          <button
            type="button"
            className="flex h-7 w-7 items-center justify-center rounded-md border border-slate-200 bg-white text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40"
            disabled={page >= pageCount - 1}
            onClick={() => onPage(page + 1)}
            title="Next page"
          >
            <ChevronRight className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>
    </section>
  );
}

function UploadRow({ row, last }: { row: HistoryRow; last: boolean }) {
  return (
    <div
      className={
        'grid gap-3 px-4 py-3 text-[13px] md:grid-cols-[minmax(180px,2fr)_0.9fr_1.2fr_0.8fr_0.7fr_0.7fr_84px] md:items-center ' +
        (last ? '' : 'border-b border-slate-100')
      }
    >
      <div className="flex min-w-0 items-center gap-2">
        <span className="rounded border border-slate-200 px-1.5 py-1 text-[9px] font-bold uppercase tracking-wider text-slate-500">
          {extension(row.source_filename)}
        </span>
        <span className="min-w-0 flex-1 truncate font-semibold text-slate-900">{row.source_filename}</span>
      </div>
      <span className="font-semibold text-slate-700">{row.vendor}</span>
      <span className="text-slate-600">{row.file_type_display}</span>
      <span className="text-left tabular-nums text-slate-600 md:text-right">{formatDecimal(row.fx_rate, 4)}</span>
      <span className="text-left tabular-nums text-slate-600 md:text-right">{formatDecimal(row.margin, 2)}%</span>
      <span className="label-faint text-left md:text-right">{row.when}</span>
      <div className="flex justify-start gap-1 md:justify-end">
        <a
          className="flex h-8 w-8 items-center justify-center rounded-md border border-slate-200 bg-white text-slate-500 transition-colors hover:bg-slate-50 hover:text-slate-700"
          href={`/api/history/${row.id}/source`}
          title="Download original"
        >
          <FileDown className="h-3.5 w-3.5" />
        </a>
        <a
          className="flex h-8 w-8 items-center justify-center rounded-md border border-accent/30 bg-accent/10 text-accent transition-colors hover:bg-accent/20"
          href={`/api/history/${row.id}/output`}
          title="Download CRM-ready export"
        >
          <Download className="h-3.5 w-3.5" />
        </a>
      </div>
    </div>
  );
}

function extension(name: string) {
  return name.split('.').pop()?.toUpperCase() ?? 'FILE';
}

function formatDecimal(value: string, places: number) {
  const number = Number(value);
  if (Number.isNaN(number)) return value;
  return number.toFixed(places);
}

function visiblePages(page: number, pageCount: number) {
  const windowSize = Math.min(pageCount, 4);
  const start = Math.min(Math.max(0, page - 1), Math.max(0, pageCount - windowSize));
  return Array.from({ length: windowSize }, (_, index) => start + index);
}
