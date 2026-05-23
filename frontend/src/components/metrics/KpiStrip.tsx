import type { MetricsSummaryResponse } from '../../types';

export function KpiStrip({ data }: { data: MetricsSummaryResponse | null }) {
  if (!data?.kpis) return null;

  const { total_parses, active_users, active_vendors, mismatch_rate } = data.kpis;
  const mismatchPct = (parseFloat(mismatch_rate) * 100).toFixed(1);

  return (
    <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
      <div className="card flex flex-col p-4">
        <span className="label text-slate-500">Total Parses</span>
        <span className="mt-2 text-2xl font-semibold text-slate-900">{total_parses}</span>
      </div>
      <div className="card flex flex-col p-4">
        <span className="label text-slate-500">Active Users</span>
        <span className="mt-2 text-2xl font-semibold text-slate-900">{active_users}</span>
      </div>
      <div className="card flex flex-col p-4">
        <span className="label text-slate-500">Active Vendors</span>
        <span className="mt-2 text-2xl font-semibold text-slate-900">{active_vendors}</span>
      </div>
      <div className="card flex flex-col p-4">
        <span className="label text-slate-500">Mismatch Rate</span>
        <span className="mt-2 text-2xl font-semibold text-slate-900">{mismatchPct}%</span>
      </div>
    </div>
  );
}
