import { useSearchParams } from 'react-router-dom';

export interface BreakdownRow {
  label: string;
  subLabel?: string;
  count: number;
  filterKey: string;
  filterValue: string;
  disabled?: boolean;
}

export function BreakdownCard({ title, rows }: { title: string; rows: BreakdownRow[] }) {
  const [searchParams, setSearchParams] = useSearchParams();

  const handleRowClick = (row: BreakdownRow) => {
    if (row.disabled || !row.filterValue) return;
    const newParams = new URLSearchParams(searchParams);
    newParams.set(row.filterKey, row.filterValue);
    setSearchParams(newParams);
  };

  const maxCount = rows.length > 0 ? Math.max(...rows.map((r) => r.count)) : 0;

  return (
    <div className="card p-0 overflow-hidden flex flex-col">
      <div className="border-b border-slate-200 bg-slate-50 px-4 py-3">
        <h3 className="label text-slate-900">{title}</h3>
      </div>
      <div className="flex-1 divide-y divide-slate-100 overflow-y-auto max-h-[300px]">
        {rows.length === 0 ? (
          <div className="p-4 text-center text-sm text-slate-500">No data</div>
        ) : (
          rows.map((row, idx) => {
            const widthPct = maxCount > 0 ? (row.count / maxCount) * 100 : 0;
            return (
              <button
                key={idx}
                type="button"
                disabled={row.disabled}
                className="group relative flex w-full items-center justify-between px-4 py-3 text-left transition-colors hover:bg-slate-50 disabled:cursor-default disabled:hover:bg-transparent"
                onClick={() => handleRowClick(row)}
              >
                <div
                  className="absolute bottom-0 left-0 top-0 bg-accent-soft opacity-30 transition-all group-hover:opacity-50"
                  style={{ width: `${widthPct}%` }}
                />
                <div className="relative z-10 flex flex-col">
                  <span className="text-sm font-medium text-slate-900">{row.label}</span>
                  {row.subLabel && <span className="text-xs text-slate-500">{row.subLabel}</span>}
                </div>
                <div className="relative z-10 text-sm font-semibold text-slate-900">{row.count}</div>
              </button>
            );
          })
        )}
      </div>
    </div>
  );
}
