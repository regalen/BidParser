import { CRM_TEMPLATE_UPLIFT } from '../constants';

export function HpSettingsBlock({
  vendorLabel,
  margin,
  onMargin,
  selectedTemplate,
}: {
  vendorLabel: string;
  margin: string;
  onMargin: (value: string) => void;
  selectedTemplate: string;
}) {
  return (
    <>
      <div className="mt-1 border-t border-dashed border-slate-200" />
      <div className="flex items-baseline justify-between">
        <span className="label">{vendorLabel} settings</span>
        <span className="text-[9px] font-bold uppercase tracking-widest text-slate-400">Vendor-specific</span>
      </div>

      {selectedTemplate === CRM_TEMPLATE_UPLIFT && (
        <label className="flex flex-col gap-2">
          <span className="label">
            Margin <span className="font-medium normal-case tracking-normal text-slate-400">· %, 2 d.p.</span>
          </span>
          <div className="relative">
            <input
              className="field pr-9"
              inputMode="decimal"
              value={margin}
              placeholder="5.25"
              onChange={(event) => onMargin(event.target.value)}
            />
            <span className="absolute right-3 top-1/2 -translate-y-1/2 text-sm font-medium text-slate-400">%</span>
          </div>
        </label>
      )}
    </>
  );
}
