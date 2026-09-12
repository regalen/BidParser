import {
  CRM_TEMPLATE_FOREIGN_UPLIFT,
  CRM_TEMPLATE_PERCENT_OFF_WITH_UPLIFT,
  CRM_TEMPLATE_UPLIFT,
} from '../constants';

interface VendorSettingsBlockProps {
  vendorLabel: string;
  selectedTemplate: string;
  supportsOnCost: boolean;
  fxRate: string;
  margin: string;
  imPercent: string;
  onCostPct: string;
  onFxRate: (value: string) => void;
  onMargin: (value: string) => void;
  onImPercent: (value: string) => void;
  onOnCostPct: (value: string) => void;
}

function PercentInput({
  label,
  value,
  placeholder,
  onChange,
}: {
  label: string;
  value: string;
  placeholder: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="flex flex-col gap-2">
      <span className="label">
        {label} <span className="font-medium normal-case tracking-normal text-slate-400">· %, 2 d.p.</span>
      </span>
      <div className="relative">
        <input
          className="field pr-9"
          inputMode="decimal"
          value={value}
          placeholder={placeholder}
          onChange={(event) => onChange(event.target.value)}
        />
        <span className="absolute right-3 top-1/2 -translate-y-1/2 text-sm font-medium text-slate-400">%</span>
      </div>
    </label>
  );
}

export function VendorSettingsBlock({
  vendorLabel,
  selectedTemplate,
  supportsOnCost,
  fxRate,
  margin,
  imPercent,
  onCostPct,
  onFxRate,
  onMargin,
  onImPercent,
  onOnCostPct,
}: VendorSettingsBlockProps) {
  const foreign = selectedTemplate === CRM_TEMPLATE_FOREIGN_UPLIFT;
  const uplift = foreign || selectedTemplate === CRM_TEMPLATE_UPLIFT || selectedTemplate === CRM_TEMPLATE_PERCENT_OFF_WITH_UPLIFT;
  const im = selectedTemplate === CRM_TEMPLATE_PERCENT_OFF_WITH_UPLIFT;

  if (!foreign && !uplift && !im && !supportsOnCost) {
    return null;
  }

  return (
    <>
      <div className="mt-1 border-t border-dashed border-slate-200" />
      <div className="flex items-baseline justify-between">
        <span className="label">{vendorLabel} settings</span>
        <span className="text-[9px] font-bold uppercase tracking-widest text-slate-400">Vendor-specific</span>
      </div>
      {foreign && (
        <label className="flex flex-col gap-2">
          <span className="label">
            Exchange rate{' '}
            <span className="font-medium normal-case tracking-normal text-slate-400">· USD to AUD</span>
          </span>
          <input
            className="field"
            inputMode="decimal"
            value={fxRate}
            placeholder="0.7354"
            onChange={(event) => onFxRate(event.target.value)}
          />
        </label>
      )}
      {uplift && <PercentInput label="Uplift" value={margin} placeholder="5.25" onChange={onMargin} />}
      {im && (
        <PercentInput label="Discount Off MSRP" value={imPercent} placeholder="30.00" onChange={onImPercent} />
      )}
      {supportsOnCost && <PercentInput label="On Cost %" value={onCostPct} placeholder="" onChange={onOnCostPct} />}
    </>
  );
}
