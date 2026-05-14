import { CheckCircle2, FileUp, Loader2 } from 'lucide-react';

import type { UploadState } from './Dropzone';

export function ProgressPanel({ file, state }: { file: File; state: UploadState }) {
  const parsed = state === 'parsed';

  return (
    <div className="flex h-[180px] flex-col justify-center rounded-2xl border-2 border-dashed border-slate-300 bg-white p-6">
      <div className="flex items-center gap-3 rounded-lg border border-slate-200 bg-white px-3 py-2">
        <span className="rounded border border-slate-200 px-1.5 py-1 text-[9px] font-bold uppercase tracking-wider text-slate-500">
          {extension(file.name)}
        </span>
        <FileUp className="h-4 w-4 text-slate-400" />
        <span className="min-w-0 flex-1 truncate text-sm font-semibold text-slate-900">{file.name}</span>
        {parsed ? (
          <span className="flex items-center gap-1.5 text-[10px] font-bold uppercase tracking-wider text-emerald-600">
            <CheckCircle2 className="h-4 w-4" />
            Parsed
          </span>
        ) : (
          <span className="flex items-center gap-1.5 text-[10px] font-bold uppercase tracking-wider text-slate-500">
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
            Parsing
          </span>
        )}
      </div>
      <div className="mt-4 h-1.5 overflow-hidden rounded-full bg-slate-100">
        <div className={parsed ? 'h-full w-full bg-emerald-500 transition-all' : 'h-full w-2/3 animate-pulse bg-accent'} />
      </div>
    </div>
  );
}

function extension(name: string) {
  return name.split('.').pop()?.toUpperCase() ?? 'FILE';
}
