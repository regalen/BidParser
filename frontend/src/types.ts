export type Role = 'admin' | 'user';

export interface User {
  id: number;
  username: string;
  name: string | null;
  role: Role;
  must_change_password: boolean;
  default_vendor: string | null;
  fx_rate: string | null;
  margin: string | null;
  created_at?: string | null;
}

export interface ParserInfo {
  slug: string;
  display_name: string;
  vendor: string;
  accepted_mime: string;
  crm_template: string;
}

export interface HistoryRow {
  id: number;
  source_filename: string;
  vendor: string;
  parser_slug: string;
  file_type_display: string;
  fx_rate: string;
  margin: string;
  when: string;
  totals_match: boolean;
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

export interface FailedParseJob {
  id: number;
  created_at: string;
  user_id: number | null;
  username: string;
  name: string | null;
  vendor: string;
  parser_slug: string;
  parser_display_name: string;
  source_filename: string;
  category: string;
  stage: string;
  hint: string | null;
  message: string | null;
  error_detail: string | null;
  source_available: boolean;
}

export interface FailedParseJobResponse {
  items: FailedParseJob[];
  total: number;
}
