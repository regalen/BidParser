import type { ParserInfo } from '../types';

export function FileTypeSelect({ parsers, value, disabled, onChange }: { parsers: ParserInfo[]; value: string; disabled: boolean; onChange: (value: string) => void }) {
  return (
    <label className="flex flex-col gap-2">
      <span className="label">File type</span>
      <select className="field appearance-none" value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>
        <option value="">Select file type</option>
        {parsers.map((parser) => (
          <option key={parser.slug} value={parser.slug}>
            {parser.display_name}
          </option>
        ))}
      </select>
      <span className="text-[11px] text-ink-mute">Types depend on the vendor.</span>
    </label>
  );
}
