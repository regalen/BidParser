import { RotateCcw } from 'lucide-react';

import type { ParserInfo } from '../types';

export function ParseSettingsCard({
  parsers,
  vendor,
  parserSlug,
  fxRate,
  margin,
  canSubmit,
  parsing,
  onVendor,
  onParser,
  onFxRate,
  onMargin,
  onSubmit,
}: {
  parsers: ParserInfo[];
  vendor: string;
  parserSlug: string;
  fxRate: string;
  margin: string;
  canSubmit: boolean;
  parsing: boolean;
  onVendor: (value: string) => void;
  onParser: (value: string) => void;
  onFxRate: (value: string) => void;
  onMargin: (value: string) => void;
  onSubmit: () => void;
}) {
  const selectedParser = parsers.find((parser) => parser.slug === parserSlug);
  const vendors = Array.from(new Set(parsers.map((parser) => parser.vendor)));
  const filtered = parsers.filter((parser) => parser.vendor === vendor);

  return (
    <aside className="flex w-full flex-col gap-4 rounded-xl border-[1.5px] border-ink bg-paper p-6 md:w-80 md:shrink-0">
      <span className="label">Parse settings</span>

      <label className="flex flex-col gap-2">
        <span className="label">Vendor</span>
        <select className="field appearance-none" value={vendor} onChange={(event) => onVendor(event.target.value)}>
          <option value="">Select vendor</option>
          {vendors.map((vendorName) => (
            <option key={vendorName} value={vendorName}>
              {vendorName}
            </option>
          ))}
        </select>
      </label>

      <label className="flex flex-col gap-2">
        <span className="label">File type</span>
        <select className="field appearance-none" value={parserSlug} disabled={!vendor} onChange={(event) => onParser(event.target.value)}>
          <option value="">Select file type</option>
          {filtered.map((parser) => (
            <option key={parser.slug} value={parser.slug}>
              {parser.display_name}
            </option>
          ))}
        </select>
        <span className="text-[11px] text-ink-mute">Types depend on the vendor.</span>
      </label>

      <div className="mt-1 border-t-[1.5px] border-dashed border-ink-faint" />
      <div className="flex items-baseline justify-between">
        <span className="label">Nutanix settings</span>
        <span className="label label-faint text-[9px]">Vendor-specific</span>
      </div>

      <label className="flex flex-col gap-2">
        <span className="label">
          Exchange rate <span className="font-normal text-ink-mute">· USD to AUD</span>
        </span>
        <input className="field" inputMode="decimal" value={fxRate} placeholder="0.7354" onChange={(event) => onFxRate(event.target.value)} />
      </label>

      <label className="flex flex-col gap-2">
        <span className="label">
          Margin <span className="font-normal text-ink-mute">· %, 2 d.p.</span>
        </span>
        <div className="relative">
          <input className="field pr-9" inputMode="decimal" value={margin} placeholder="5.25" onChange={(event) => onMargin(event.target.value)} />
          <span className="absolute right-3 top-1/2 -translate-y-1/2 text-sm font-medium text-ink-mute">%</span>
        </div>
      </label>

      <div className="mt-1 rounded-lg border-[1.5px] border-emerald-500 bg-emerald-50 px-3 py-2.5">
        <div className="flex items-baseline justify-between">
          <span className="label text-emerald-600">CRM import template</span>
          <span className="label text-[9px] text-emerald-600 opacity-70">Auto</span>
        </div>
        <div className="mt-1 text-sm font-semibold text-emerald-700">{selectedParser?.crm_template ?? 'Foreign Uplift'}</div>
      </div>

      <div className="mt-2 border-t-[1.5px] border-ink-faint" />
      <button type="button" className="button button-primary" disabled={!canSubmit || parsing} onClick={onSubmit}>
        {parsing ? (
          <>
            <RotateCcw size={13} className="animate-spin" />
            Parsing
          </>
        ) : (
          'Upload & parse'
        )}
      </button>
      <span className="text-center text-[11px] leading-5 text-ink-mute">Output will automatically download once completed.</span>
    </aside>
  );
}
