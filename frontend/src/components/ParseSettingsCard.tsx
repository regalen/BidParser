import { RotateCcw } from 'lucide-react';

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

      <VendorSelect vendors={vendors} value={vendor} onChange={onVendor} />
      <FileTypeSelect parsers={filtered} value={parserSlug} disabled={!vendor} onChange={onParser} />

      <NutanixSettingsBlock fxRate={fxRate} margin={margin} onFxRate={onFxRate} onMargin={onMargin} />

      <CrmTemplateCallout template={selectedParser?.crm_template ?? 'Foreign Uplift'} />

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
