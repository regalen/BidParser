import { Download } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router';

import { api } from '../../api/client';
import { AppHeader } from '../../components/AppHeader';
import { Footer } from '../../components/Footer';
import { BreakdownCard, type BreakdownRow } from '../../components/metrics/BreakdownCard';
import { DateRangeControl } from '../../components/metrics/DateRangeControl';
import { FilterChips } from '../../components/metrics/FilterChips';
import { KpiStrip } from '../../components/metrics/KpiStrip';
import { UtilisationTimeChart } from '../../components/metrics/UtilisationTimeChart';
import type { MetricsSummaryResponse } from '../../types';

export function MetricsDashboard() {
  const [searchParams] = useSearchParams();
  const [data, setData] = useState<MetricsSummaryResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    api
      .metricsSummary(searchParams)
      .then((res) => {
        if (active) {
          setData(res);
          setError(null);
        }
      })
      .catch((err) => {
        if (active) setError(err.message);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [searchParams]);

  const handleExport = () => {
    window.location.href = `/api/metrics/export?${searchParams.toString()}`;
  };

  const userRows: BreakdownRow[] = (data?.byUser ?? []).map((u) => ({
    label: u.name ?? u.username,
    subLabel: `@${u.username}`,
    count: u.count,
    filterKey: 'userId',
    filterValue: u.userId !== null ? String(u.userId) : '',
    disabled: u.userId === null,
  }));

  const vendorRows: BreakdownRow[] = (data?.byVendor ?? []).map((v) => ({
    label: v.vendor,
    count: v.count,
    filterKey: 'vendor',
    filterValue: v.vendor,
  }));

  const parserRows: BreakdownRow[] = (data?.byParser ?? []).map((p) => ({
    label: p.displayName,
    subLabel: p.parserSlug,
    count: p.count,
    filterKey: 'parserSlug',
    filterValue: p.parserSlug,
  }));

  const importTypeRows: BreakdownRow[] = (data?.byImportType ?? []).map((i) => ({
    label: i.importType === 'auto' ? 'Auto' : i.importType === 'manual' ? 'Manual' : 'Unknown',
    count: i.count,
    filterKey: 'importType',
    filterValue: i.importType ?? '',
    disabled: i.importType === null,
  }));

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <AppHeader />

      <main className="mx-auto w-full max-w-6xl flex-1 px-8 py-8">
        <div className="mb-6 flex flex-col items-start justify-between gap-4 sm:flex-row sm:items-center">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight text-slate-900">Utilisation Dashboard</h1>
            <p className="mt-1 text-sm text-slate-500">Track parsing activity and success rates</p>
          </div>

          <div className="flex flex-col items-end gap-3 sm:flex-row sm:items-center sm:gap-4">
            <DateRangeControl />
            <button type="button" className="button" onClick={handleExport}>
              <Download className="h-4 w-4" />
              Export XLSX
            </button>
          </div>
        </div>

        <FilterChips data={data} />

        {error ? (
          <div className="rounded-md bg-red-50 p-4 text-red-500">{error}</div>
        ) : loading && !data ? (
          <div className="flex h-32 items-center justify-center text-slate-500">
            <span className="label">Loading...</span>
          </div>
        ) : (
          <div className="mt-4 flex flex-col gap-6">
            <KpiStrip data={data} />
            <UtilisationTimeChart data={data} />

            <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
              <BreakdownCard title="By User" rows={userRows} />
              <BreakdownCard title="By Vendor" rows={vendorRows} />
              <BreakdownCard title="By File Type" rows={parserRows} />
              <BreakdownCard title="By Import Type" rows={importTypeRows} />
            </div>
          </div>
        )}
      </main>

      <Footer />
    </div>
  );
}
