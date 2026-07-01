// CRM template identifiers. Must stay in sync with
// BidParser.Domain.Constants.CrmTemplates on the backend.
export const CRM_TEMPLATE_NO_CALCULATION = 'No Calculation';
export const CRM_TEMPLATE_UPLIFT = 'Uplift';
export const CRM_TEMPLATE_PERCENT_OFF_WITH_UPLIFT = '% Off RRP with Uplift';

// Client-side upload-size guard. Keep in sync with the server's MAX_UPLOAD_MB
// env var (default 10); the server's 413 remains the authoritative check.
export const MAX_UPLOAD_MB = 10;
export const MAX_UPLOAD_BYTES = MAX_UPLOAD_MB * 1024 * 1024;

// Vendor identifiers. Must stay in sync with BidParser.Domain.Constants.Vendors.
export const VENDOR_NUTANIX = 'Nutanix';
export const VENDOR_HP = 'HP';
export const VENDOR_LENOVO = 'Lenovo';
export const VENDOR_ZEBRA = 'Zebra';
