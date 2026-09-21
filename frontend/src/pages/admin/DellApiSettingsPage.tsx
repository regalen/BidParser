import { AlertCircle, CheckCircle2, Loader2, Plug, Save } from 'lucide-react';
import { useEffect, useState } from 'react';

import { api, ApiError } from '../../api/client';
import { AppHeader } from '../../components/AppHeader';
import { Footer } from '../../components/Footer';
import type { DellApiSettings, DellApiSettingsUpdate } from '../../types';

type Draft = Omit<DellApiSettingsUpdate, 'clientSecret'>;
type Notice = { tone: 'success' | 'error'; message: string };

export function DellApiSettingsPage() {
  const [settings, setSettings] = useState<DellApiSettings | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [clientSecret, setClientSecret] = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);

  useEffect(() => {
    let active = true;
    api
      .dellSettings()
      .then((value) => {
        if (!active) return;
        setSettings(value);
        setDraft(toDraft(value));
      })
      .catch((caught: unknown) => {
        if (active) setNotice({ tone: 'error', message: apiErrorMessage(caught, 'Could not load Dell API settings.') });
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, []);

  function update<K extends keyof Draft>(key: K, value: Draft[K]) {
    setDraft((current) => (current ? { ...current, [key]: value } : current));
    setNotice(null);
  }

  async function save(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draft) return;

    setSaving(true);
    setNotice(null);
    try {
      const updated = await api.updateDellSettings({
        ...draft,
        ...(clientSecret ? { clientSecret: clientSecret } : {}),
      });
      setSettings(updated);
      setDraft(toDraft(updated));
      setClientSecret('');
      setNotice({ tone: 'success', message: 'Dell API settings saved.' });
    } catch (caught) {
      setNotice({ tone: 'error', message: apiErrorMessage(caught, 'Could not save Dell API settings.') });
    } finally {
      setSaving(false);
    }
  }

  async function testConnection() {
    setTesting(true);
    setNotice(null);
    try {
      await api.testDellSettings();
      setNotice({ tone: 'success', message: 'Connection successful. Dell accepted the stored credentials.' });
    } catch (caught) {
      setNotice({ tone: 'error', message: apiErrorMessage(caught, 'Could not connect to the Dell API.') });
    } finally {
      setTesting(false);
    }
  }

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <AppHeader />
      <main className="mx-auto w-full max-w-4xl flex-1 px-6 py-8 lg:px-8">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900">Dell API</h1>
          <p className="mt-1 text-sm text-slate-500">Configure the credentials and endpoints used to retrieve Dell quotes.</p>
        </div>

        {notice && <NoticeBanner notice={notice} />}

        {loading ? (
          <div className="card mt-8 flex h-48 items-center justify-center text-slate-500">
            <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            <span className="label">Loading settings</span>
          </div>
        ) : draft ? (
          <form className="card mt-8 p-6" onSubmit={save}>
            <div className="flex items-center gap-3 border-b border-slate-200 pb-5">
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-accent/10 text-accent">
                <Plug className="h-5 w-5" />
              </div>
              <div>
                <h2 className="font-bold text-slate-900">Quote API connection</h2>
                <p className="mt-0.5 text-xs text-slate-500">The client secret is encrypted at rest and is never returned to this page.</p>
              </div>
            </div>

            <div className="mt-6 grid grid-cols-1 gap-5 md:grid-cols-2">
              <TextField
                id="dell-token-url"
                label="Token URL"
                type="url"
                value={draft.tokenUrl}
                onChange={(value) => update('tokenUrl', value)}
              />
              <TextField
                id="dell-client-id"
                label="Client ID"
                value={draft.clientId}
                onChange={(value) => update('clientId', value)}
              />
              <label className="flex flex-col gap-2">
                <span className="label">Client secret</span>
                <input
                  id="dell-client-secret"
                  className="field"
                  type="password"
                  autoComplete="new-password"
                  value={clientSecret}
                  placeholder={settings?.clientSecretConfigured ? '••••••••  (unchanged)' : ''}
                  onChange={(event) => {
                    setClientSecret(event.target.value);
                    setNotice(null);
                  }}
                />
                <span className="text-[11px] text-slate-500">Leave blank to keep the current secret.</span>
              </label>
              <TextField
                id="dell-client-id-header"
                label="Client ID header"
                value={draft.clientIdHeader}
                onChange={(value) => update('clientIdHeader', value)}
              />
              <div className="md:col-span-2">
                <TextField
                  id="dell-quote-url-template"
                  label="Quote URL template"
                  value={draft.quoteUrlTemplate}
                  helper="Must be HTTPS and contain {quoteNumber}, {version}, and {locale}."
                  onChange={(value) => update('quoteUrlTemplate', value)}
                />
              </div>
              <TextField
                id="dell-default-locale"
                label="Default locale"
                value={draft.defaultLocale}
                onChange={(value) => update('defaultLocale', value)}
              />
              <TextField
                id="dell-api-version"
                label="API version"
                value={draft.apiVersion}
                helper="Sent as the Accepts-version header. Dell picks its own default if left unset."
                onChange={(value) => update('apiVersion', value)}
              />
              <label className="flex items-center gap-3 self-end rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
                <input
                  type="checkbox"
                  className="h-4 w-4 rounded-sm border-slate-300 text-accent focus:ring-accent"
                  checked={draft.useBasicAuthForToken}
                  onChange={(event) => update('useBasicAuthForToken', event.target.checked)}
                />
                <span className="text-sm font-semibold text-slate-700">Use HTTP Basic auth for token requests</span>
              </label>
            </div>

            <div className="mt-6 flex flex-col gap-3 border-t border-slate-200 pt-5 sm:flex-row sm:items-center sm:justify-between">
              <span className="text-xs text-slate-500">
                {settings?.updatedAt ? `Last saved ${new Date(settings.updatedAt).toLocaleString()}` : 'Not configured yet'}
              </span>
              <div className="flex gap-3">
                <button type="button" className="button" disabled={saving || testing} onClick={() => void testConnection()}>
                  {testing ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Plug className="h-3.5 w-3.5" />}
                  Test connection
                </button>
                <button type="submit" className="button button-primary" disabled={saving || testing}>
                  {saving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}
                  Save settings
                </button>
              </div>
            </div>
          </form>
        ) : null}
      </main>
      <Footer />
    </div>
  );
}

function TextField({
  id,
  label,
  value,
  type = 'text',
  helper,
  onChange,
}: {
  id: string;
  label: string;
  value: string;
  type?: 'text' | 'url';
  helper?: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="flex flex-col gap-2" htmlFor={id}>
      <span className="label">{label}</span>
      <input id={id} className="field" type={type} value={value} onChange={(event) => onChange(event.target.value)} />
      {helper && <span className="text-[11px] text-slate-500">{helper}</span>}
    </label>
  );
}

function NoticeBanner({ notice }: { notice: Notice }) {
  const success = notice.tone === 'success';
  const Icon = success ? CheckCircle2 : AlertCircle;
  return (
    <div
      role={success ? 'status' : 'alert'}
      className={
        'mt-6 flex items-center gap-2 rounded-xl border px-4 py-3 text-xs font-bold ' +
        (success ? 'border-emerald-100 bg-emerald-50 text-emerald-700' : 'border-red-100 bg-red-50 text-red-600')
      }
    >
      <Icon className="h-4 w-4 shrink-0" />
      {notice.message}
    </div>
  );
}

function toDraft(settings: DellApiSettings): Draft {
  return {
    tokenUrl: settings.tokenUrl,
    clientId: settings.clientId,
    quoteUrlTemplate: settings.quoteUrlTemplate,
    defaultLocale: settings.defaultLocale,
    clientIdHeader: settings.clientIdHeader,
    apiVersion: settings.apiVersion,
    useBasicAuthForToken: settings.useBasicAuthForToken,
  };
}

function apiErrorMessage(caught: unknown, fallback: string): string {
  return caught instanceof ApiError && typeof caught.detail === 'string' ? caught.detail : fallback;
}
