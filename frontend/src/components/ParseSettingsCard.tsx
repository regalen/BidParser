import { Loader2 } from 'lucide-react';

import { VENDOR_DELL, isAutoParserSlug } from '../constants';
import type { ParserInfo } from '../types';
import { CrmTemplateCallout } from './CrmTemplateCallout';
import { FileTypeSelect } from './FileTypeSelect';
import { VendorSelect } from './VendorSelect';
import { VendorSettingsBlock } from './VendorSettingsBlock';

export function ParseSettingsCard({
  parsers,
  vendor,
  parserSlug,
  fxRate,
  margin,
  imPercent,
  onCostPct,
  selectedTemplate,
  splitBySolutionId,
  canSubmit,
  parsing,
  onVendor,
  onParser,
  onFxRate,
  onMargin,
  onImPercent,
  onOnCostPct,
  onTemplate,
  onSplitBySolutionId,
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
  splitBySolutionId: boolean;
  canSubmit: boolean;
  parsing: boolean;
  onVendor: (value: string) => void;
  onParser: (value: string) => void;
  onFxRate: (value: string) => void;
  onMargin: (value: string) => void;
  onImPercent: (value: string) => void;
  onOnCostPct: (value: string) => void;
  onTemplate: (value: string) => void;
  onSplitBySolutionId: (value: boolean) => void;
  onSubmit: () => void;
}) {
  const selectedParser = parsers.find((parser) => parser.slug === parserSlug);
  const vendors = Array.from(new Set(parsers.map((parser) => parser.vendor))).sort((a, b) => a.localeCompare(b));
  const isDellVendor = vendor === VENDOR_DELL;
  // Dell quotes come from the Quote API, not manual file upload: CTO vs APOS is
  // disambiguated by dell_auto's Detect() scoring, so the dropdown offers (and locks to)
  // only the Auto entry.
  const filtered = isDellVendor
    ? parsers.filter((parser) => parser.vendor === vendor && isAutoParserSlug(parser.slug))
    : parsers.filter((parser) => parser.vendor === vendor);
  const showVendorSettings = Boolean(vendor && parserSlug);
  const isMultiTemplate = (selectedParser?.availableTemplates?.length ?? 0) > 1;

  return (
    <aside className="card flex w-full flex-col gap-4 p-6 md:w-80 md:shrink-0">
      <span className="label">Parse settings</span>

      <VendorSelect vendors={vendors} value={vendor} onChange={onVendor} />
      <FileTypeSelect parsers={filtered} value={parserSlug} disabled={!vendor || isDellVendor} onChange={onParser} />

      {showVendorSettings && selectedParser && (
        <>
          {isMultiTemplate ? (
            <label className="flex flex-col gap-2">
              <span className="label">CRM Import Template</span>
              <select className="field" value={selectedTemplate} onChange={(e) => onTemplate(e.target.value)}>
                {selectedParser.availableTemplates.map((template) => (
                  <option key={template} value={template}>
                    {template}
                  </option>
                ))}
              </select>
            </label>
          ) : (
            <CrmTemplateCallout template={selectedParser.crmTemplate} />
          )}
          <VendorSettingsBlock
            vendorLabel={selectedParser.vendor}
            selectedTemplate={selectedTemplate}
            supportsOnCost={selectedParser.supportsOnCost}
            fxRate={fxRate}
            margin={margin}
            imPercent={imPercent}
            onCostPct={onCostPct}
            onFxRate={onFxRate}
            onMargin={onMargin}
            onImPercent={onImPercent}
            onOnCostPct={onOnCostPct}
          />
          {selectedParser.supportsSolutionIdSplit && (
            <label className="flex items-start gap-3 rounded-lg border border-slate-200 bg-slate-50 px-3 py-3">
              <input
                type="checkbox"
                className="mt-0.5 h-4 w-4 rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                checked={splitBySolutionId}
                onChange={(event) => onSplitBySolutionId(event.target.checked)}
              />
              <span className="flex flex-col gap-1">
                <span className="text-sm font-medium text-slate-800">
                  Split output by {selectedParser.solutionSplitLabel}
                </span>
                <span className="text-xs leading-5 text-slate-500">
                  One workbook per {selectedParser.solutionSplitLabel}, delivered as a .zip
                </span>
              </span>
            </label>
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
    </aside>
  );
}
