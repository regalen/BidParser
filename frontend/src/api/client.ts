import type {
  HistoryResponse,
  MetricsSummaryResponse,
  MonitoringRunsResponse,
  ParserInfo,
  User,
} from '../types';

export class ApiError extends Error {
  status: number;
  detail: unknown;
  retryAfter: string | null;

  constructor(status: number, detail: unknown, retryAfter: string | null = null) {
    super(typeof detail === 'string' ? detail : 'API request failed');
    this.status = status;
    this.detail = detail;
    this.retryAfter = retryAfter;
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (!(init.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  if (init.method && init.method !== 'GET') {
    headers.set('X-Requested-With', 'BidParser');
  }

  const response = await fetch(`/api${path}`, {
    credentials: 'include',
    ...init,
    headers,
  });

  if (!response.ok) {
    let detail: unknown = response.statusText;
    try {
      const payload = await response.json();
      detail = payload.detail ?? payload;
    } catch {
      detail = response.statusText;
    }
    handleAuthSideEffect(path, response.status, detail);
    throw new ApiError(response.status, detail, response.headers.get('Retry-After'));
  }

  if (response.status === 204) {
    return undefined as T;
  }
  return response.json() as Promise<T>;
}

export interface CancelledLineInfo {
  line: string;
  vpn: string;
}

export interface ParseResponse {
  blob: Blob;
  filename: string;
  validation: 'match' | 'mismatch';
  currency: string;
  computedTotal: string;
  quotedTotal: string;
  /** Lines flagged Cancelled=Y in the source document; empty array when none. */
  cancelledLines: CancelledLineInfo[];
}

export const api = {
  async login(username: string, password: string): Promise<User> {
    const payload = await request<{ user: User }>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    });
    return payload.user;
  },

  async logout(): Promise<void> {
    await request('/auth/logout', { method: 'POST' });
  },

  async changePassword(oldPassword: string, newPassword: string): Promise<void> {
    await request('/auth/change-password', {
      method: 'POST',
      body: JSON.stringify({ old_password: oldPassword, new_password: newPassword }),
    });
  },

  me(): Promise<User> {
    return request<User>('/me');
  },

  updateSettings(payload: { default_vendor?: string; fx_rate?: string; margin?: string; im_percent?: string }): Promise<User> {
    return request<User>('/me/settings', {
      method: 'PATCH',
      body: JSON.stringify(payload),
    });
  },

  parsers(): Promise<ParserInfo[]> {
    return request<ParserInfo[]>('/parsers');
  },

  history(limit: number, offset: number, q?: string): Promise<HistoryResponse> {
    const params = new URLSearchParams({ limit: String(limit), offset: String(offset) });
    if (q && q.trim()) params.set('q', q.trim());
    return request<HistoryResponse>(`/history?${params.toString()}`);
  },

  users(): Promise<User[]> {
    return request<User[]>('/users');
  },

  metricsSummary(params: URLSearchParams): Promise<MetricsSummaryResponse> {
    return request<MetricsSummaryResponse>(`/metrics/summary?${params.toString()}`);
  },

  monitoringRuns(params: URLSearchParams): Promise<MonitoringRunsResponse> {
    return request<MonitoringRunsResponse>(`/monitoring/runs?${params.toString()}`);
  },

  createUser(payload: { username: string; name: string; role: 'admin' | 'user' }): Promise<User> {
    return request<User>('/users', {
      method: 'POST',
      body: JSON.stringify(payload),
    });
  },

  updateUser(id: number, payload: { username?: string; name?: string; role?: 'admin' | 'user'; reset_password?: boolean }): Promise<User> {
    return request<User>(`/users/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(payload),
    });
  },

  deleteUser(id: number): Promise<void> {
    return request<void>(`/users/${id}`, { method: 'DELETE' });
  },

  // Upsert the admin-configured report type for a parser slug. An empty string
  // clears the mapping.
  updateReportType(slug: string, reportType: string): Promise<{ ok: boolean }> {
    return request<{ ok: boolean }>(`/report-types/${encodeURIComponent(slug)}`, {
      method: 'PUT',
      body: JSON.stringify({ report_type: reportType }),
    });
  },

  async parse(formData: FormData): Promise<ParseResponse> {
    const headers = new Headers({ 'X-Requested-With': 'BidParser' });
    const response = await fetch('/api/parse', {
      method: 'POST',
      credentials: 'include',
      headers,
      body: formData,
    });
    if (!response.ok) {
      let detail: unknown = response.statusText;
      try {
        const payload = await response.json();
        detail = payload.detail ?? payload;
      } catch {
        detail = response.statusText;
      }
      handleAuthSideEffect('/parse', response.status, detail);
      throw new ApiError(response.status, detail, response.headers.get('Retry-After'));
    }
    const disposition = response.headers.get('Content-Disposition') ?? '';
    const filename = disposition.match(/filename="?([^"]+)"?/)?.[1] ?? 'parsed.xlsx';

    // Parse the X-Cancelled-Lines header: "line:VPN;line:VPN;..."
    const cancelledRaw = response.headers.get('X-Cancelled-Lines') ?? '';
    const cancelledLines: CancelledLineInfo[] = cancelledRaw
      ? cancelledRaw.split(';').map((entry) => {
          const colonIdx = entry.indexOf(':');
          return colonIdx >= 0
            ? { line: entry.slice(0, colonIdx), vpn: entry.slice(colonIdx + 1) }
            : { line: entry, vpn: '' };
        })
      : [];

    return {
      blob: await response.blob(),
      filename,
      validation: (response.headers.get('X-Validation') as 'match' | 'mismatch') ?? 'mismatch',
      currency: response.headers.get('X-Currency') ?? '',
      computedTotal: response.headers.get('X-Computed-Total') ?? '',
      quotedTotal: response.headers.get('X-Quoted-Total') ?? '',
      cancelledLines,
    };
  },
};

function handleAuthSideEffect(path: string, status: number, detail: unknown) {
  if (typeof window === 'undefined') return;
  if (status === 401 && path !== '/auth/login') {
    window.dispatchEvent(new CustomEvent('bidparser:unauthorized'));
  }
  if (status === 403 && detail === 'password_change_required') {
    window.dispatchEvent(new CustomEvent('bidparser:password-change-required'));
  }
}
