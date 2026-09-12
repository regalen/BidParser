// CRM template identifiers. Must stay in sync with
// BidParser.Domain.Constants.CrmTemplates on the backend.
export const CRM_TEMPLATE_NO_CALCULATION = 'No Calculation';
export const CRM_TEMPLATE_UPLIFT = 'Uplift';
export const CRM_TEMPLATE_FOREIGN_UPLIFT = 'Foreign Uplift';
export const CRM_TEMPLATE_PERCENT_OFF_WITH_UPLIFT = '% Off RRP with Uplift';

// Client-side upload-size guard. Keep in sync with the server's MAX_UPLOAD_MB
// env var (default 10); the server's 413 remains the authoritative check.
export const MAX_UPLOAD_MB = 10;
export const MAX_UPLOAD_BYTES = MAX_UPLOAD_MB * 1024 * 1024;

// Vendor identifiers. Must stay in sync with BidParser.Domain.Constants.Vendors.
export const VENDOR_NUTANIX = 'Nutanix';
export const VENDOR_HP = 'HP';
export const VENDOR_LENOVO_ISG = 'Lenovo ISG';
export const VENDOR_LENOVO_IDG = 'Lenovo IDG';
export const VENDOR_DELL = 'Dell';
export const VENDOR_CISCO = 'Cisco';

// Parser slug identifiers. Must stay in sync with BidParser.Domain.Constants.ParserSlugs on the backend.
export const PARSER_SLUG_NUTANIX_AUTO = 'nutanix_auto';
export const PARSER_SLUG_LENOVO_AUTO = 'lenovo_auto';
export const PARSER_SLUG_ZEBRA_AUTO = 'zebra_auto';
export const PARSER_SLUG_DELL_AUTO = 'dell_auto';

const autoParserSlugs = new Set<string>([
  PARSER_SLUG_NUTANIX_AUTO,
  PARSER_SLUG_LENOVO_AUTO,
  PARSER_SLUG_ZEBRA_AUTO,
  PARSER_SLUG_DELL_AUTO,
]);

export function isAutoParserSlug(slug: string): boolean {
  return autoParserSlugs.has(slug);
}

// 13 digits, a full stop, then a one- or two-digit version.
export const DELL_QUOTE_ID_PATTERN = /^\d{13}\.\d{1,2}$/;
