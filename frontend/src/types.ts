export type Role = 'admin' | 'user';

export interface User {
  id: number;
  username: string;
  name: string | null;
  role: Role;
  mustChangePassword: boolean;
  defaultVendor: string | null;
  fxRate: string | null;
  margin: string | null;
  imPercent: string | null;
  createdAt?: string | null;
}

export interface UserWithTempPassword {
  user: User;
  tempPassword: string | null;
}

export interface DellApiSettings {
  tokenUrl: string;
  clientId: string;
  clientSecretConfigured: boolean;
  quoteUrlTemplate: string;
  defaultLocale: string;
  clientIdHeader: string;
  apiVersion: string;
  useBasicAuthForToken: boolean;
  updatedAt: string | null;
}

export interface DellApiSettingsUpdate {
  tokenUrl: string;
  clientId: string;
  clientSecret?: string;
  quoteUrlTemplate: string;
  defaultLocale: string;
  clientIdHeader: string;
  apiVersion: string;
  useBasicAuthForToken: boolean;
}

export interface ParserInfo {
  slug: string;
  displayName: string;
  vendor: string;
  acceptedMime: string;
  acceptedMimes: string[];
  crmTemplate: string;
  availableTemplates: string[];
  /**
   * True when the parser can emit the vendor's full sub-component tree. No longer rendered —
   * suppression is unconditional — but retained so the capability stays visible in /api/parsers.
   */
  supportsSubComponentDetail: boolean;
  /** True when output can be split into one workbook per Solution ID. */
  supportsSolutionIdSplit: boolean;
  /** True when the parser accepts an optional On Cost percentage. */
  supportsOnCost: boolean;
  solutionSplitLabel: string;
}

export interface RuntimeVendorDefaults {
  fxRate: string | null;
  margin: string | null;
  imPercent: string | null;
  onCostPct: string | null;
}

export interface ParseUiConfig {
  vendorDefaults: Record<string, RuntimeVendorDefaults>;
  guidanceByParserSlug: Record<string, string>;
}

export interface RuntimeConfigDocument {
  json: string;
  updatedAt: string | null;
  validationError: string | null;
}

export interface RuntimeConfigParserReference {
  slug: string;
  displayName: string;
  vendor: string;
}

export interface RuntimeConfigField {
  key: string;
  label: string;
  scale: number;
}

export interface RuntimeConfigurationAdmin {
  guidanceMessages: RuntimeConfigDocument;
  vendorDefaults: RuntimeConfigDocument;
  parsers: RuntimeConfigParserReference[];
  vendors: string[];
  supportedFields: RuntimeConfigField[];
  htmlAllowlist: string[];
}

export interface HistoryRow {
  id: number;
  sourceFilename: string;
  bidNumber: string | null;
  bidRevision: string | null;
  vendor: string;
  parserSlug: string;
  fileTypeDisplay: string;
  crmTemplate: string;
  when: string;
  totalsMatch: boolean;
}

export interface HistoryResponse {
  rows: HistoryRow[];
  total: number;
}

export interface ApiErrorDetail {
  stage?: string;
  hint?: string;
  message?: string;
}

export interface MetricsKpis {
  totalParses: number;
  activeUsers: number;
  activeVendors: number;
  mismatchRate: string;
}

export interface MetricsByUser {
  userId: number | null;
  username: string;
  name: string | null;
  count: number;
}

export interface MetricsByVendor {
  vendor: string;
  count: number;
}

export interface MetricsByParser {
  parserSlug: string;
  displayName: string;
  count: number;
}

export interface MetricsByImportType {
  importType: string | null;
  count: number;
}

export interface MetricsTimeSeriesPoint {
  date: string;
  count: number;
}

export interface MetricsSummaryResponse {
  range: { mode: 'bounded' | 'all'; from: string | null; to: string | null };
  timeSeriesGranularity: 'day' | 'month';
  kpis: MetricsKpis;
  byUser: MetricsByUser[];
  byVendor: MetricsByVendor[];
  byParser: MetricsByParser[];
  byImportType: MetricsByImportType[];
  timeSeries: MetricsTimeSeriesPoint[];
}

export type FailureCategory = 'magicByteMismatch' | 'parserError' | 'unhandledException' | 'validationMismatch';

export interface FailedParseJob {
  id: number;
  createdAt: string;
  userId: number | null;
  username: string;
  name: string | null;
  vendor: string;
  parserSlug: string;
  parserDisplayName: string;
  sourceFilename: string;
  category: FailureCategory | string;
  stage: string | null;
  hint: string | null;
  message: string | null;
  errorDetail: string;
  sourceAvailable: boolean;
  /** Populated only for validationMismatch entries. */
  computedTotal: string | null;
  quotedTotal: string | null;
}

export type RunStatus = 'success' | FailureCategory;

export interface MonitoringRun {
  /** "job" → successful/mismatch ParseJob; "failure" → recorded failure. */
  kind: 'job' | 'failure';
  id: number;
  status: RunStatus | string;
  createdAt: string;
  userId: number | null;
  username: string;
  name: string | null;
  vendor: string;
  parserSlug: string;
  parserDisplayName: string;
  importType: string | null;
  sourceFilename: string;
  sourceAvailable: boolean;
  outputAvailable: boolean;
  computedTotal: string | null;
  quotedTotal: string | null;
  /** Populated for failure rows only. */
  stage: string | null;
  hint: string | null;
  message: string | null;
  errorDetail: string | null;
}

export interface MonitoringRunsResponse {
  items: MonitoringRun[];
  total: number;
}
