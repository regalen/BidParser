import { CheckCircle2, FileUp } from 'lucide-react';

import type { UploadState } from './Dropzone';

export function ProgressPanel({ file, state }: { file: File; state: UploadState }) {
  return (
    <div className="flex h-[180px] flex-col justify-center rounded-2xl border-2 border-dashed border-ink bg-paper p-6">
      <div className="flex items-center gap-3 rounded-lg border-[1.5px] border-ink-soft bg-paper px-3 py-2">
        <span className="label rounded border-[1.5px] border-ink px-1.5 py-1 text-[9px]">{extension(file.name)}</span>
        <FileUp size={16} className="text-ink-soft" />
        <span className="min-w-0 flex-1 truncate text-sm font-semibold text-ink">{file.name}</span>
        {state === 'parsed' ? <CheckCircle2 size={18} className="text-emerald-600" /> : <span className="label label-faint">Parsing</span>}
      </div>
      <div className="mt-4 h-2 overflow-hidden rounded-full border-[1.5px] border-ink bg-paper-tint">
        <div className={state === 'parsed' ? 'h-full w-full bg-emerald-500' : 'h-full w-2/3 animate-pulse bg-accent'} />
      </div>
    </div>
  );
}

function extension(name: string) {
  return name.split('.').pop()?.toUpperCase() ?? 'FILE';
}
