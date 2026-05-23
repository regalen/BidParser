import { Bar, BarChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import type { MetricsSummaryResponse } from '../../types';

export function UtilisationTimeChart({ data }: { data: MetricsSummaryResponse | null }) {
  if (!data?.time_series || data.time_series.length === 0) {
    return (
      <div className="card flex h-64 flex-col items-center justify-center p-4">
        <span className="label text-slate-500">No data for selected range</span>
      </div>
    );
  }

  const chartData = data.time_series.map((point) => {
    const d = new Date(point.date);
    return {
      ...point,
      displayDate: d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
    };
  });

  return (
    <div className="card p-6">
      <h3 className="label mb-6 text-slate-900">Parses over time</h3>
      <div className="h-64 w-full">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={chartData} margin={{ top: 0, right: 0, left: -20, bottom: 0 }}>
            <XAxis
              dataKey="displayDate"
              axisLine={false}
              tickLine={false}
              tick={{ fontSize: 12, fill: '#64748b' }}
              dy={10}
            />
            <YAxis
              axisLine={false}
              tickLine={false}
              tick={{ fontSize: 12, fill: '#64748b' }}
              allowDecimals={false}
            />
            <Tooltip
              cursor={{ fill: '#f1f5f9' }}
              contentStyle={{
                backgroundColor: '#ffffff',
                border: '1px solid #e2e8f0',
                borderRadius: '8px',
                boxShadow: '0 1px 2px 0 rgba(15, 23, 42, 0.04)',
                fontSize: '12px',
                fontWeight: 500,
              }}
              itemStyle={{ color: '#0f172a' }}
              labelStyle={{ color: '#64748b', marginBottom: '4px' }}
            />
            <Bar dataKey="count" fill="#0077d4" radius={[4, 4, 0, 0]} maxBarSize={40} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}
