import { useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';

type PresetKey = 'last_7' | 'last_30' | 'this_month' | 'last_month' | 'month' | 'custom';

function toIso(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function startOfMonth(year: number, month: number) {
  return new Date(year, month, 1);
}

function endOfMonth(year: number, month: number) {
  return new Date(year, month + 1, 0);
}

function presetRange(preset: PresetKey, monthValue: string | null): { from: string; to: string } | null {
  const today = new Date();
  switch (preset) {
    case 'last_7': {
      const from = new Date(today);
      from.setDate(today.getDate() - 6);
      return { from: toIso(from), to: toIso(today) };
    }
    case 'last_30': {
      const from = new Date(today);
      from.setDate(today.getDate() - 29);
      return { from: toIso(from), to: toIso(today) };
    }
    case 'this_month': {
      return {
        from: toIso(startOfMonth(today.getFullYear(), today.getMonth())),
        to: toIso(today),
      };
    }
    case 'last_month': {
      const from = startOfMonth(today.getFullYear(), today.getMonth() - 1);
      const to = endOfMonth(today.getFullYear(), today.getMonth() - 1);
      return { from: toIso(from), to: toIso(to) };
    }
    case 'month': {
      if (!monthValue) return null;
      const [year, m] = monthValue.split('-').map(Number);
      if (!year || !m) return null;
      return {
        from: toIso(startOfMonth(year, m - 1)),
        to: toIso(endOfMonth(year, m - 1)),
      };
    }
    case 'custom':
    default:
      return null;
  }
}

function detectPreset(from: string | null, to: string | null): PresetKey {
  if (!from || !to) return 'last_30';
  for (const candidate of ['last_7', 'last_30', 'this_month', 'last_month'] as const) {
    const range = presetRange(candidate, null);
    if (range && range.from === from && range.to === to) return candidate;
  }
  if (from.slice(0, 7) === to.slice(0, 7)) {
    const monthRange = presetRange('month', from.slice(0, 7));
    if (monthRange && monthRange.from === from && monthRange.to === to) return 'month';
  }
  return 'custom';
}

export function DateRangeControl() {
  const [searchParams, setSearchParams] = useSearchParams();

  const from = searchParams.get('from');
  const to = searchParams.get('to');
  const selected = useMemo(() => detectPreset(from, to), [from, to]);
  const monthValue = selected === 'month' && from ? from.slice(0, 7) : '';

  function applyRange(range: { from: string; to: string } | null) {
    const next = new URLSearchParams(searchParams);
    if (!range) {
      next.delete('from');
      next.delete('to');
    } else {
      next.set('from', range.from);
      next.set('to', range.to);
    }
    setSearchParams(next);
  }

  function handlePresetChange(preset: PresetKey) {
    if (preset === 'last_30') {
      applyRange(null);
      return;
    }
    if (preset === 'custom') {
      const today = new Date();
      const def = new Date(today);
      def.setDate(today.getDate() - 6);
      applyRange({ from: toIso(def), to: toIso(today) });
      return;
    }
    if (preset === 'month') {
      const today = new Date();
      const value = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`;
      const range = presetRange('month', value);
      if (range) applyRange(range);
      return;
    }
    const range = presetRange(preset, null);
    if (range) applyRange(range);
  }

  function handleMonthChange(value: string) {
    const range = presetRange('month', value);
    if (range) applyRange(range);
  }

  function handleCustomChange(field: 'from' | 'to', value: string) {
    if (!value) return;
    const next = new URLSearchParams(searchParams);
    next.set(field, value);
    if (field === 'from' && !next.get('to')) next.set('to', value);
    if (field === 'to' && !next.get('from')) next.set('from', value);
    setSearchParams(next);
  }

  return (
    <div className="flex flex-wrap items-center gap-2">
      <label className="label" htmlFor="date-range-preset">Date range</label>
      <select
        id="date-range-preset"
        className="field w-40 !min-h-[32px] !py-1 !text-sm"
        value={selected}
        onChange={(event) => handlePresetChange(event.target.value as PresetKey)}
      >
        <option value="last_7">Last 7 days</option>
        <option value="last_30">Last 30 days</option>
        <option value="this_month">This month</option>
        <option value="last_month">Last month</option>
        <option value="month">Pick month…</option>
        <option value="custom">Custom range</option>
      </select>

      {selected === 'month' && (
        <input
          type="month"
          className="field !min-h-[32px] !py-1 !text-sm"
          value={monthValue}
          onChange={(event) => handleMonthChange(event.target.value)}
        />
      )}

      {selected === 'custom' && (
        <>
          <input
            type="date"
            className="field !min-h-[32px] !py-1 !text-sm"
            value={from ?? ''}
            max={to ?? undefined}
            onChange={(event) => handleCustomChange('from', event.target.value)}
            aria-label="From"
          />
          <span className="text-xs text-slate-500">to</span>
          <input
            type="date"
            className="field !min-h-[32px] !py-1 !text-sm"
            value={to ?? ''}
            min={from ?? undefined}
            onChange={(event) => handleCustomChange('to', event.target.value)}
            aria-label="To"
          />
        </>
      )}
    </div>
  );
}
