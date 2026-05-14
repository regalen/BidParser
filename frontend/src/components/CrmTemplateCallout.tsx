import { FileSpreadsheet } from 'lucide-react';

export function CrmTemplateCallout({ template }: { template: string }) {
  return (
    <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-3">
      <div className="flex items-center justify-between">
        <span className="text-[10px] font-bold uppercase tracking-wider text-emerald-700">CRM Import Template</span>
        <span className="text-[9px] font-bold uppercase tracking-wider text-emerald-600/70">Auto</span>
      </div>
      <div className="mt-1.5 flex items-center gap-2 text-sm font-semibold text-emerald-800">
        <FileSpreadsheet className="h-4 w-4" />
        {template}
      </div>
    </div>
  );
}
