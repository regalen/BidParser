import { ChevronLeft, ChevronRight, Download, FileDown } from 'lucide-react';
import { useEffect, useRef } from 'react';

import type { HistoryRow } from '../types';

export function RecentUploadsTable({
  rows,
  total,
  page,
  pageSize,
  onPage,
  onPageSize,
}: {
  rows: HistoryRow[];
  total: number;
  page: number;
  pageSize: number;
  onPage: (page: number) => void;
  onPageSize: (pageSize: number) => void;
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
      const nextPageSize = Math.max(1, Math.floor(availableBodyHeight / 46));
      if (nextPageSize !== pageSize) {
        onPageSize(nextPageSize);
      }
    });
    observer.observe(table);
    return () => observer.disconnect();
  }, [onPageSize, pageSize]);

  return (
    <section ref={tableRef} className="mt-5 flex min-h-[360px] flex-1 flex-col overflow-hidden rounded-xl border-[1.5px] border-ink bg-paper">
      <div className="flex items-center justify-between border-b-[1.5px] border-ink-faint bg-slate-50 px-[18px] py-3.5">
        <span className="label">Recent uploads</span>
        <span className="label label-faint">Last {rows.length}</span>
      </div>
      <div className="hidden grid-cols-[minmax(180px,2fr)_0.9fr_1.2fr_0.8fr_0.7fr_0.7fr_76px] gap-3 border-b border-ink-faint bg-slate-50 px-[18px] py-2.5 md:grid">
        <span className="label label-faint">File name</span>
        <span className="label label-faint">Vendor</span>
        <span className="label label-faint">File type</span>
        <span className="label label-faint text-right">FX rate</span>
        <span className="label label-faint text-right">Margin</span>
        <span className="label label-faint text-right">When</span>
        <span className="label label-faint text-right">Files</span>
      </div>
      <div className="min-h-0 flex-1 overflow-hidden">
        {rows.length === 0 ? (
          <div className="grid h-full min-h-48 place-items-center px-6 text-center">
            <div>
              <div className="label label-faint">No uploads yet</div>
              <div className="mt-2 text-sm text-ink-mute">Parsed files will appear here after the first download.</div>
            </div>
          </div>
        ) : (
          rows.map((row, index) => <UploadRow key={row.id} row={row} last={index === rows.length - 1} />)
        )}
      </div>
      <div className="flex items-center justify-between border-t border-ink-faint bg-slate-50 px-[18px] py-2.5">
        <span className="label label-faint">
          Showing {start} - {end} of {total}
        </span>
        <div className="flex items-center gap-1">
          <button type="button" className="icon-button disabled:cursor-not-allowed disabled:opacity-45" disabled={page === 0} onClick={() => onPage(page - 1)} title="Previous page">
            <ChevronLeft size={14} />
          </button>
          {pageButtons.map((pageIndex) => (
            <button
              key={pageIndex}
              type="button"
              className={[
                'h-7 min-w-7 rounded-md border-[1.5px] px-2 text-[11px] font-bold',
                pageIndex === page ? 'border-ink bg-ink text-paper' : 'border-ink-faint bg-paper text-ink-soft',
              ].join(' ')}
              onClick={() => onPage(pageIndex)}
            >
              {pageIndex + 1}
            </button>
          ))}
          <button type="button" className="icon-button disabled:cursor-not-allowed disabled:opacity-45" disabled={page >= pageCount - 1} onClick={() => onPage(page + 1)} title="Next page">
            <ChevronRight size={14} />
          </button>
        </div>
      </div>
    </section>
  );
}

function UploadRow({ row, last }: { row: HistoryRow; last: boolean }) {
  return (
    <div
      className={[
        'grid gap-3 px-[18px] py-3 text-[13px] md:grid-cols-[minmax(180px,2fr)_0.9fr_1.2fr_0.8fr_0.7fr_0.7fr_76px] md:items-center',
        last ? '' : 'border-b border-ink-faint',
      ].join(' ')}
    >
      <div className="flex min-w-0 items-center gap-2">
        <span className="label rounded border-[1.5px] border-ink-mute px-1.5 py-1 text-[9px]">{extension(row.source_filename)}</span>
        <span className="min-w-0 flex-1 truncate font-semibold text-ink">{row.source_filename}</span>
      </div>
      <span className="font-semibold text-ink">{row.vendor}</span>
      <span className="text-ink-soft">{row.file_type_display}</span>
      <span className="text-left tabular-nums text-ink-soft md:text-right">{formatDecimal(row.fx_rate, 4)}</span>
      <span className="text-left tabular-nums text-ink-soft md:text-right">{formatDecimal(row.margin, 2)}%</span>
      <span className="label label-faint text-left md:text-right">{row.when}</span>
      <div className="flex justify-start gap-1 md:justify-end">
        <a className="icon-button" href={`/api/history/${row.id}/source`} title="Download original">
          <FileDown size={14} />
        </a>
        <a className="icon-button border-accent bg-accent-soft text-accent" href={`/api/history/${row.id}/output`} title="Download CRM-ready export">
          <Download size={14} />
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
