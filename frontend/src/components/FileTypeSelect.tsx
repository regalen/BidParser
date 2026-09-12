import type { ParserInfo } from '../types';

const sampleFiles: Record<string, string> = {
  nutanix_software_only_pdf: 'XQ-9100002.pdf',
  nutanix_software_only_xlsx: 'XQ-9100002.xlsx',
  nutanix_renewal_pdf: 'XQ-9100004.pdf',
  nutanix_renewal_xlsx: 'XQ-9100010.xlsx',
  nutanix_hardware_only_pdf: 'XQ-9100003.pdf',
  nutanix_hardware_only_xlsx: 'XQ-9100003.xlsx',
  hp_bid_xlsx: 'Deals_Sample_01_HPI.xlsx',
  hp_global_bid_xlsx: 'translate_quote_98000001_v25_all.xlsx',
  hp_oneconfig_xlsx: '99000001.xlsx',
  hp_services_xlsx: 'CH9000000002.xlsx',
  hpe_bid_xlsx: 'HPE_Deal_9500000001_v2.xlsx',
  lenovo_lbpe_isg_xls: 'Bid_Platform_Bid_Request_Sample_04.xlsx',
  lenovo_lbpi_isg_pdf: 'BRDAS019000004V1.pdf',
  lenovo_lbpi_idg_pdf: 'BRPAS019100001V1.pdf',
  zebra_pcr_pdf: 'Zebra_PC_97000001_V2.0.pdf',
  zebra_pcr_xls: 'Zebra_PC_97000001.xls',
  datalogic_quote_pdf: 'Datalogic_PE930003.pdf',
  epson_quote_pdf: 'Epson_96000002.pdf',
  strike_quote_pdf: 'Strike_Quote_9202.pdf',
  cisco_ccw_quote_xls: 'Quote_9400000001.xls',
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
  const sampleFilename = value ? sampleFiles[value] : undefined;

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
            {parser.displayName}
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
