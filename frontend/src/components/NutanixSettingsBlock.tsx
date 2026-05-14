export function NutanixSettingsBlock({
  fxRate,
  margin,
  onFxRate,
  onMargin,
}: {
  fxRate: string;
  margin: string;
  onFxRate: (value: string) => void;
  onMargin: (value: string) => void;
}) {
  return (
    <>
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
    </>
  );
}
