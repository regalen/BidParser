export function CrmTemplateCallout({ template }: { template: string }) {
  return (
    <div className="mt-1 rounded-lg border-[1.5px] border-emerald-500 bg-emerald-50 px-3 py-2.5">
      <div className="flex items-baseline justify-between">
        <span className="label text-emerald-600">CRM import template</span>
        <span className="label text-[9px] text-emerald-600 opacity-70">Auto</span>
      </div>
      <div className="mt-1 text-sm font-semibold text-emerald-700">{template}</div>
    </div>
  );
}
