import { UploadCloud, X } from 'lucide-react';
import { useRef, useState } from 'react';

import { MAX_UPLOAD_MB } from '../constants';
import { ProgressPanel } from './ProgressPanel';

export type UploadState = 'idle' | 'parsing' | 'parsed';

export function Dropzone({
  file,
  state,
  error,
  onFile,
  onClear,
}: {
  file: File | null;
  state: UploadState;
  error?: string | null;
  onFile: (file: File) => void;
  onClear: () => void;
}) {
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [dragging, setDragging] = useState(false);

  if (state !== 'idle' && file) {
    return <ProgressPanel file={file} state={state} />;
  }

  const borderClass = error
    ? 'border-red-300 bg-red-50'
    : dragging
      ? 'border-accent bg-accent/5'
      : 'border-slate-300 bg-white hover:border-slate-400 hover:bg-slate-50';

  return (
    <div
      className={
        'flex h-[180px] cursor-pointer flex-col items-center justify-center rounded-2xl border-2 border-dashed p-6 text-center transition-colors ' +
        borderClass
      }
      onClick={() => inputRef.current?.click()}
      onDragOver={(event) => {
        event.preventDefault();
        setDragging(true);
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={(event) => {
        event.preventDefault();
        setDragging(false);
        const next = event.dataTransfer.files[0];
        if (next) onFile(next);
      }}
    >
      <input
        ref={inputRef}
        type="file"
        accept=".pdf,.xlsx,.xls,application/pdf,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.ms-excel"
        className="hidden"
        onChange={(event) => {
          const next = event.currentTarget.files?.[0];
          if (next) onFile(next);
          event.currentTarget.value = '';
        }}
      />
      {file ? (
        <div className="flex w-full max-w-lg items-center gap-3 rounded-lg border border-slate-200 bg-white px-3 py-2">
          <span className="rounded border border-slate-200 px-1.5 py-1 text-[9px] font-bold uppercase tracking-wider text-slate-500">
            {extension(file.name)}
          </span>
          <span className="min-w-0 flex-1 truncate text-left text-sm font-semibold text-slate-900">{file.name}</span>
          <button
            type="button"
            className="icon-button"
            title="Remove file"
            onClick={(event) => {
              event.stopPropagation();
              onClear();
            }}
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      ) : (
        <>
          <UploadCloud className="h-12 w-12 text-slate-400" strokeWidth={1.7} />
          <div className="mt-3 text-base font-semibold tracking-tight text-slate-900">Drop quote file here</div>
          <div className="mt-1 text-[11px] tracking-wide text-slate-500">PDF, XLSX or XLS · max {MAX_UPLOAD_MB} MB</div>
        </>
      )}
      {error && <div className="mt-3 max-w-xl text-xs font-semibold text-red-600">{error}</div>}
    </div>
  );
}

function extension(name: string) {
  return name.split('.').pop()?.toUpperCase() ?? 'FILE';
}
