import { Loader2 } from 'lucide-react';

import type { ParserInfo } from '../types';
import { CrmTemplateCallout } from './CrmTemplateCallout';
import { FileTypeSelect } from './FileTypeSelect';
import { NutanixSettingsBlock } from './NutanixSettingsBlock';
import { VendorSelect } from './VendorSelect';

export function ParseSettingsCard({
  parsers,
  vendor,
  parserSlug,
  fxRate,
  fxRatePegged,
  margin,
  defaultsDirty,
  canSubmit,
  savingDefaults,
  parsing,
  onVendor,
  onParser,
  onFxRate,
  onFxRatePegged,
  onMargin,
  onSaveDefaults,
  onSubmit,
}: {
  parsers: ParserInfo[];
  vendor: string;
  parserSlug: string;
  fxRate: string;
  fxRatePegged: boolean;
  margin: string;
  defaultsDirty: boolean;
  canSubmit: boolean;
  savingDefaults: boolean;
  parsing: boolean;
  onVendor: (value: string) => void;
  onParser: (value: string) => void;
  onFxRate: (value: string) => void;
  onFxRatePegged: (value: boolean) => void;
  onMargin: (value: string) => void;
  onSaveDefaults: () => void;
  onSubmit: () => void;
}) {
  const selectedParser = parsers.find((parser) => parser.slug === parserSlug);
  const vendors = Array.from(new Set(parsers.map((parser) => parser.vendor)));
  const filtered = parsers.filter((parser) => parser.vendor === vendor);
  const showVendorSettings = Boolean(vendor && parserSlug);

  return (
    <aside className="card flex w-full flex-col gap-4 p-6 md:w-80 md:shrink-0">
      <span className="label">Parse settings</span>

      <VendorSelect vendors={vendors} value={vendor} onChange={onVendor} />
      <FileTypeSelect parsers={filtered} value={parserSlug} disabled={!vendor} onChange={onParser} />

      <FxPegControl pegged={fxRatePegged} onChange={onFxRatePegged} />

      {showVendorSettings && (
        <>
          <NutanixSettingsBlock
            fxRate={fxRate}
            fxRateDisabled={fxRatePegged}
            margin={margin}
            onFxRate={onFxRate}
            onMargin={onMargin}
          />
          <CrmTemplateCallout template={selectedParser?.crm_template ?? 'Foreign Uplift'} />
        </>
      )}

      <div className="mt-2 border-t border-slate-200" />
      <button type="button" className="button" disabled={!defaultsDirty || savingDefaults} onClick={onSaveDefaults}>
        {savingDefaults ? (
          <>
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
            Saving
          </>
        ) : (
          'Save defaults'
        )}
      </button>
      <button type="button" className="button button-primary" disabled={!canSubmit || parsing} onClick={onSubmit}>
        {parsing ? (
          <>
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
            Parsing
          </>
        ) : (
          'Upload & parse'
        )}
      </button>
      <span className="text-center text-[11px] leading-5 text-slate-500">
        Output will automatically download once completed.
      </span>
    </aside>
  );
}

function FxPegControl({ pegged, onChange }: { pegged: boolean; onChange: (value: boolean) => void }) {
  return (
    <label className="flex items-start gap-3 rounded-md border border-slate-200 bg-slate-50 px-3 py-3">
      <input
        type="checkbox"
        className="mt-0.5 h-4 w-4 rounded border-slate-300 text-accent focus:ring-accent"
        checked={pegged}
        onChange={(event) => onChange(event.target.checked)}
      />
      <span className="min-w-0">
        <span className="block text-[11px] font-bold uppercase tracking-wider text-slate-700">Peg FX rate to Bloomberg</span>
        <span className="mt-1 block text-[11px] leading-4 text-slate-500">Use the latest daily Bloomberg refresh for your default rate.</span>
      </span>
    </label>
  );
}
