import { CheckCircle2, FileUp, UploadCloud, X } from 'lucide-react';
import { useRef, useState } from 'react';

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

  return (
    <div
      className={[
        'flex h-[180px] cursor-pointer flex-col items-center justify-center rounded-2xl border-2 border-dashed bg-paper p-6 text-center transition',
        dragging ? 'border-accent bg-accent-soft' : error ? 'border-red-500 bg-red-50' : 'border-ink',
      ].join(' ')}
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
        accept=".pdf,.xlsx,application/pdf,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        className="hidden"
        onChange={(event) => {
          const next = event.currentTarget.files?.[0];
          if (next) onFile(next);
          event.currentTarget.value = '';
        }}
      />
      {file ? (
        <div className="flex w-full max-w-lg items-center gap-3 rounded-lg border-[1.5px] border-ink bg-paper px-3 py-2">
          <span className="label rounded border-[1.5px] border-ink px-1.5 py-1 text-[9px]">{extension(file.name)}</span>
          <span className="min-w-0 flex-1 truncate text-left text-sm font-semibold text-ink">{file.name}</span>
          <button
            type="button"
            className="icon-button"
            title="Remove file"
            onClick={(event) => {
              event.stopPropagation();
              onClear();
            }}
          >
            <X size={14} />
          </button>
        </div>
      ) : (
        <>
          <UploadCloud size={48} strokeWidth={1.7} className="text-ink" />
          <div className="mt-3 text-lg font-semibold tracking-normal text-ink">Drop quote file here</div>
          <div className="mt-2 text-[11px] tracking-wide text-ink-mute">PDF or XLSX · max 10 MB</div>
        </>
      )}
      {error && <div className="mt-3 max-w-xl text-xs font-semibold text-red-700">{error}</div>}
    </div>
  );
}

function ProgressPanel({ file, state }: { file: File; state: UploadState }) {
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
