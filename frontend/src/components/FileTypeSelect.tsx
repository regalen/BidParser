import type { ParserInfo } from '../types';

const SAMPLE_FILES: Record<string, string> = {
  nutanix_software_only_pdf: 'XQ-4076249.pdf',
  nutanix_software_only_xlsx: 'XQ-4076249.xlsx',
  nutanix_renewal_pdf: 'XQ-4128926.pdf',
  nutanix_hardware_only_pdf: 'XQ-4108785.pdf',
  nutanix_hardware_only_xlsx: 'XQ-4108785.xlsx',
  hp_bid_xlsx: 'Deals20260518T034809_HPI.xlsx',
  hp_global_bid_xlsx: 'translate_quote_47500427_v25_all.xlsx',
  hp_oneconfig_xlsx: '55648855.xlsx',
  lenovo_brda_dcg_pdf: 'BRDAS010260417V1.pdf',
  lenovo_brda_dcg_xlsx: 'BRDAD010458440.xls',
};

export function FileTypeSelect({
  parsers,
  value,
  disabled,
  onChange,
}: {
  parsers: ParserInfo[];
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  const sampleFilename = value ? SAMPLE_FILES[value] : undefined;

  return (
    <label className="flex flex-col gap-2">
      <span className="label">File type</span>
      <select
        className="field appearance-none"
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">Select file type</option>
        {parsers.map((parser) => (
          <option key={parser.slug} value={parser.slug}>
            {parser.display_name}
          </option>
        ))}
      </select>
      <span className="text-[11px] text-slate-500">
        Types depend on the vendor.
        {sampleFilename && (
          <>
            {' '}
            <a
              className="text-[#0077d4] underline hover:no-underline"
              href={`/samples/${sampleFilename}`}
              target="_blank"
              rel="noopener noreferrer"
            >
              Sample File
            </a>
          </>
        )}
      </span>
    </label>
  );
}
