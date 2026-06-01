import { Loader2 } from 'lucide-react';

import { VENDOR_HP, VENDOR_ZEBRA } from '../constants';
import type { ParserInfo } from '../types';
import { CrmTemplateCallout } from './CrmTemplateCallout';
import { FileTypeSelect } from './FileTypeSelect';
import { HpSettingsBlock } from './HpSettingsBlock';
import { NutanixSettingsBlock } from './NutanixSettingsBlock';
import { VendorSelect } from './VendorSelect';
import { ZebraSettingsBlock } from './ZebraSettingsBlock';

export function ParseSettingsCard({
  parsers,
  vendor,
  parserSlug,
  fxRate,
  margin,
  imPercent,
  onCostPct,
  selectedTemplate,
  canSubmit,
  parsing,
  onVendor,
  onParser,
  onFxRate,
  onMargin,
  onImPercent,
  onOnCostPct,
  onTemplate,
  onSubmit,
}: {
  parsers: ParserInfo[];
  vendor: string;
  parserSlug: string;
  fxRate: string;
  margin: string;
  imPercent: string;
  onCostPct: string;
  selectedTemplate: string;
  canSubmit: boolean;
  parsing: boolean;
  onVendor: (value: string) => void;
  onParser: (value: string) => void;
  onFxRate: (value: string) => void;
  onMargin: (value: string) => void;
  onImPercent: (value: string) => void;
  onOnCostPct: (value: string) => void;
  onTemplate: (value: string) => void;
  onSubmit: () => void;
}) {
  const selectedParser = parsers.find((parser) => parser.slug === parserSlug);
  const vendors = Array.from(new Set(parsers.map((parser) => parser.vendor)));
  const filtered = parsers.filter((parser) => parser.vendor === vendor);
  const showVendorSettings = Boolean(vendor && parserSlug);
  const isZebraVendor = selectedParser?.vendor === VENDOR_ZEBRA;
  const isMultiTemplate = (selectedParser?.available_templates?.length ?? 0) > 1;
  const isHpVendor = selectedParser?.vendor === VENDOR_HP;

  return (
    <aside className="card flex w-full flex-col gap-4 p-6 md:w-80 md:shrink-0">
      <span className="label">Parse settings</span>

      <VendorSelect vendors={vendors} value={vendor} onChange={onVendor} />
      <FileTypeSelect parsers={filtered} value={parserSlug} disabled={!vendor} onChange={onParser} />

      {showVendorSettings && selectedParser && (
        <>
          {isZebraVendor ? (
            // Zebra (multi-template): template dropdown + Zebra On Cost % block
            <>
              <label className="flex flex-col gap-2">
                <span className="label">CRM Import Template</span>
                <select
                  className="field"
                  value={selectedTemplate}
                  onChange={(e) => onTemplate(e.target.value)}
                >
                  {selectedParser.available_templates.map((t) => (
                    <option key={t} value={t}>
                      {t}
                    </option>
                  ))}
                </select>
              </label>
              <ZebraSettingsBlock
                vendorLabel={selectedParser.vendor}
                selectedTemplate={selectedTemplate}
                margin={margin}
                onMargin={onMargin}
                onCostPct={onCostPct}
                onOnCostPct={onOnCostPct}
              />
            </>
          ) : isMultiTemplate ? (
            // Multi-template HP (HP Bid XLSX): template dropdown + HP settings block
            <>
              <label className="flex flex-col gap-2">
                <span className="label">CRM Import Template</span>
                <select
                  className="field"
                  value={selectedTemplate}
                  onChange={(e) => onTemplate(e.target.value)}
                >
                  {selectedParser.available_templates.map((t) => (
                    <option key={t} value={t}>
                      {t}
                    </option>
                  ))}
                </select>
              </label>
              <HpSettingsBlock
                vendorLabel={selectedParser.vendor}
                margin={margin}
                onMargin={onMargin}
                imPercent={imPercent}
                onImPercent={onImPercent}
                selectedTemplate={selectedTemplate}
              />
            </>
          ) : isHpVendor ? (
            // Single-template HP (HP OneConfig XLSX): HP settings block + static callout
            <>
              <HpSettingsBlock
                vendorLabel={selectedParser.vendor}
                margin={margin}
                onMargin={onMargin}
                imPercent={imPercent}
                onImPercent={onImPercent}
                selectedTemplate={selectedParser.crm_template}
              />
              <CrmTemplateCallout template={selectedParser.crm_template} />
            </>
          ) : (
            // Single-template non-HP: vendor settings block + static callout
            <>
              <NutanixSettingsBlock
                vendorLabel={selectedParser.vendor}
                fxRate={fxRate}
                margin={margin}
                onFxRate={onFxRate}
                onMargin={onMargin}
              />
              <CrmTemplateCallout template={selectedParser.crm_template} />
            </>
          )}
        </>
      )}

      <div className="mt-2 border-t border-slate-200" />
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
