import { Check, CheckCircle2, Clipboard, Code2, Save } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useBeforeUnload, useBlocker } from 'react-router';

import { api, ApiError } from '../../api/client';
import { AppHeader } from '../../components/AppHeader';
import { Footer } from '../../components/Footer';
import type { RuntimeConfigDocument, RuntimeConfigurationAdmin } from '../../types';

type EditorKind = 'guidance' | 'defaults';
type EditorFlags = Record<EditorKind, boolean>;
type EditorMessages = Partial<Record<EditorKind, string>>;

const cleanFlags: EditorFlags = { guidance: false, defaults: false };
const unsavedMessage = 'You have unsaved runtime configuration changes. Leave without saving?';

export function RuntimeConfigurationPage() {
  const [config, setConfig] = useState<RuntimeConfigurationAdmin | null>(null);
  const [guidance, setGuidance] = useState('');
  const [defaults, setDefaults] = useState('');
  const [savedGuidance, setSavedGuidance] = useState('');
  const [savedDefaults, setSavedDefaults] = useState('');
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [errors, setErrors] = useState<EditorMessages>({});
  const [notices, setNotices] = useState<EditorMessages>({});
  const [saving, setSaving] = useState<EditorFlags>(cleanFlags);
  const [copied, setCopied] = useState<EditorKind | null>(null);

  const load = useCallback(() => {
    let active = true;
    setLoading(true);
    setLoadError(null);
    api.runtimeConfiguration()
      .then((value) => {
        if (!active) return;
        setConfig(value);
        setGuidance(value.guidanceMessages.json);
        setDefaults(value.vendorDefaults.json);
        setSavedGuidance(value.guidanceMessages.json);
        setSavedDefaults(value.vendorDefaults.json);
        setErrors({});
      })
      .catch((caught) => {
        if (active) setLoadError(message(caught));
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => { active = false; };
  }, []);

  useEffect(() => load(), [load]);

  const dirtyByEditor = useMemo<EditorFlags>(() => ({
    guidance: guidance !== savedGuidance,
    defaults: defaults !== savedDefaults,
  }), [defaults, guidance, savedDefaults, savedGuidance]);
  const dirty = dirtyByEditor.guidance || dirtyByEditor.defaults;

  useBeforeUnload(useCallback((event) => {
    if (!dirty) return;
    event.preventDefault();
    event.returnValue = '';
  }, [dirty]));
  const navigationBlocker = useBlocker(dirty);
  useEffect(() => {
    if (navigationBlocker.state !== 'blocked') return;
    if (window.confirm(unsavedMessage)) navigationBlocker.proceed();
    else navigationBlocker.reset();
  }, [navigationBlocker]);

  function format(kind: EditorKind) {
    const value = kind === 'guidance' ? guidance : defaults;
    try {
      const formatted = JSON.stringify(JSON.parse(value), null, 2);
      if (kind === 'guidance') setGuidance(formatted);
      else setDefaults(formatted);
      setErrors((current) => ({ ...current, [kind]: undefined }));
    } catch (caught) {
      setErrors((current) => ({ ...current, [kind]: syntaxMessage(caught, value) }));
    }
  }

  async function save(kind: EditorKind) {
    const value = kind === 'guidance' ? guidance : defaults;
    try {
      JSON.parse(value);
    } catch (caught) {
      setErrors((current) => ({ ...current, [kind]: syntaxMessage(caught, value) }));
      return;
    }

    setSaving((current) => ({ ...current, [kind]: true }));
    setNotices((current) => ({ ...current, [kind]: undefined }));
    setErrors((current) => ({ ...current, [kind]: undefined }));
    try {
      const saved = kind === 'guidance'
        ? await api.updateGuidanceMessages(value)
        : await api.updateVendorDefaults(value);
      applySaved(kind, saved);
      setNotices((current) => ({
        ...current,
        [kind]: `${kind === 'guidance' ? 'Guidance messages' : 'Vendor defaults'} saved.`,
      }));
    } catch (caught) {
      setErrors((current) => ({ ...current, [kind]: message(caught) }));
    } finally {
      setSaving((current) => ({ ...current, [kind]: false }));
    }
  }

  function applySaved(kind: EditorKind, saved: RuntimeConfigDocument) {
    if (kind === 'guidance') {
      setGuidance(saved.json);
      setSavedGuidance(saved.json);
    } else {
      setDefaults(saved.json);
      setSavedDefaults(saved.json);
    }
    setConfig((current) => current ? {
      ...current,
      guidanceMessages: kind === 'guidance' ? saved : current.guidanceMessages,
      vendorDefaults: kind === 'defaults' ? saved : current.vendorDefaults,
    } : current);
  }

  async function copyExample(kind: EditorKind, value: string) {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(kind);
      window.setTimeout(() => setCopied((current) => current === kind ? null : current), 1500);
    } catch {
      setErrors((current) => ({ ...current, [kind]: 'Clipboard access is unavailable in this browser.' }));
    }
  }

  const guidanceExample = config?.parsers[0]
    ? JSON.stringify([{ fileTypes: [config.parsers[0].slug], html: '<p>Use the <strong>Standard</strong> report.</p>' }], null, 2)
    : '[]';
  const defaultsExample = config?.vendors[0]
    ? JSON.stringify([{ vendors: [config.vendors[0]], margin: 5.25 }], null, 2)
    : '[]';

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <AppHeader />
      <main className="mx-auto w-full max-w-6xl flex-1 px-6 py-8 lg:px-8">
        <h1 className="text-2xl font-bold tracking-tight text-slate-900">Runtime Configuration</h1>
        <p className="mt-1 text-sm text-slate-500">Manage parse defaults and result guidance without restarting the application.</p>

        {loading && <div className="card mt-8 p-6 text-slate-500">Loading configuration…</div>}
        {!loading && loadError && (
          <div className="card mt-8 p-6">
            <p className="text-sm text-red-600">{loadError}</p>
            <button type="button" className="button mt-4" onClick={load}>Retry</button>
          </div>
        )}

        {!loading && config && (
          <>
            <section className="card mt-8 p-6">
              <h2 className="font-bold text-slate-900">Configuration reference</h2>
              <p className="mt-1 text-sm text-slate-500">Use these live parser and vendor identifiers in the JSON documents.</p>
              <div className="mt-5 grid gap-6 lg:grid-cols-2">
                <div>
                  <h3 id="concrete-parser-slugs-heading" className="label">Concrete parser slugs</h3>
                  <ul
                    className="mt-2 max-h-64 overflow-auto text-sm text-slate-700"
                    aria-labelledby="concrete-parser-slugs-heading"
                    tabIndex={0}
                  >
                    {config.parsers.map((parser) => (
                      <li key={parser.slug} className="border-b border-slate-100 py-1">
                        <code>{parser.slug}</code>
                      </li>
                    ))}
                  </ul>
                </div>
                <div>
                  <h3 className="label">Vendors</h3>
                  <p className="mt-2 text-sm text-slate-700">{config.vendors.join(', ')}</p>
                  <h3 className="label mt-5 block">Supported numeric fields</h3>
                  <table className="mt-2 text-left text-sm">
                    <tbody>
                      {config.supportedFields.map((field) => (
                        <tr key={field.key}>
                          <td className="pr-4 font-mono">{field.key}</td>
                          <td className="pr-4">{field.label}</td>
                          <td>{field.scale} dp</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  <h3 className="label mt-5 block">Allowed HTML</h3>
                  <p className="mt-2 text-sm text-slate-700">{config.htmlAllowlist.join(', ')}</p>
                </div>
              </div>

              <div className="mt-6 grid gap-4 lg:grid-cols-2">
                <Example title="Guidance example" value={guidanceExample} copied={copied === 'guidance'} onCopy={() => copyExample('guidance', guidanceExample)} />
                <Example title="Vendor-default example" value={defaultsExample} copied={copied === 'defaults'} onCopy={() => copyExample('defaults', defaultsExample)} />
              </div>
            </section>

            <div className="mt-6">
              <JsonEditor
                idBase="guidance-messages"
                title="Guidance Messages"
                description="HTML is sanitised by the server before it is stored."
                value={guidance}
                error={errors.guidance}
                notice={notices.guidance}
                dirty={dirtyByEditor.guidance}
                saving={saving.guidance}
                updatedAt={config.guidanceMessages.updatedAt}
                validationWarning={config.guidanceMessages.validationError}
                onChange={setGuidance}
                onFormat={() => format('guidance')}
                onSave={() => save('guidance')}
              />
            </div>

            <div className="mt-6">
              <JsonEditor
                idBase="vendor-defaults"
                title="Vendor Defaults"
                description="Numeric values are applied only when the selected vendor changes."
                value={defaults}
                error={errors.defaults}
                notice={notices.defaults}
                dirty={dirtyByEditor.defaults}
                saving={saving.defaults}
                updatedAt={config.vendorDefaults.updatedAt}
                validationWarning={config.vendorDefaults.validationError}
                onChange={setDefaults}
                onFormat={() => format('defaults')}
                onSave={() => save('defaults')}
              />
            </div>
          </>
        )}
      </main>
      <Footer />
    </div>
  );
}

function JsonEditor(props: {
  idBase: string;
  title: string;
  description: string;
  value: string;
  error?: string;
  notice?: string;
  dirty: boolean;
  saving: boolean;
  updatedAt: string | null;
  validationWarning: string | null;
  onChange(value: string): void;
  onFormat(): void;
  onSave(): void;
}) {
  const headingId = `${props.idBase}-heading`;
  const descriptionId = `${props.idBase}-description`;
  const errorId = `${props.idBase}-error`;
  const warningId = `${props.idBase}-warning`;
  const describedBy = [descriptionId, props.validationWarning ? warningId : null, props.error ? errorId : null]
    .filter(Boolean)
    .join(' ');

  return (
    <section className="card p-6">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <h2 id={headingId} className="font-bold text-slate-900">{props.title}</h2>
            {props.dirty && <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold uppercase text-amber-700">Unsaved</span>}
          </div>
          <p id={descriptionId} className="mt-1 text-xs text-slate-500">{props.description}</p>
        </div>
        <Code2 className="h-5 w-5 text-slate-400" />
      </div>
      <textarea
        className="field runtime-config-editor mt-5 font-mono text-xs leading-5"
        aria-labelledby={headingId}
        aria-describedby={describedBy}
        aria-invalid={Boolean(props.error)}
        spellCheck={false}
        value={props.value}
        onChange={(event) => props.onChange(event.target.value)}
      />
      {props.validationWarning && (
        <p id={warningId} role="alert" className="mt-2 text-sm text-amber-700">
          Stored document needs repair: {props.validationWarning}
        </p>
      )}
      {props.error && <p id={errorId} role="alert" className="mt-2 text-sm text-red-600">{props.error}</p>}
      {props.notice && <p role="status" className="mt-2 flex items-center gap-2 text-sm text-emerald-700"><CheckCircle2 className="h-4 w-4" />{props.notice}</p>}
      <div className="mt-4 flex items-center justify-between gap-3">
        <span className="text-xs text-slate-500">{props.updatedAt ? `Last saved ${new Date(props.updatedAt).toLocaleString()}` : 'Not saved yet'}</span>
        <div className="flex gap-2">
          <button type="button" className="button" disabled={props.saving} onClick={props.onFormat}>Format JSON</button>
          <button type="button" className="button button-primary" disabled={props.saving || !props.dirty} onClick={props.onSave}>
            <Save className="h-4 w-4" />
            {props.saving ? 'Saving' : 'Save'}
          </button>
        </div>
      </div>
    </section>
  );
}

function Example({ title, value, copied, onCopy }: { title: string; value: string; copied: boolean; onCopy(): void }) {
  return (
    <div className="overflow-hidden rounded-lg border border-slate-200 bg-slate-50">
      <div className="flex items-center justify-between border-b border-slate-200 px-3 py-2">
        <span className="text-xs font-semibold text-slate-600">{title}</span>
        <button type="button" className="inline-flex items-center gap-1 text-xs font-medium text-accent" onClick={onCopy}>
          {copied ? <Check className="h-3.5 w-3.5" /> : <Clipboard className="h-3.5 w-3.5" />}
          {copied ? 'Copied' : 'Copy'}
        </button>
      </div>
      <pre className="overflow-auto p-3 text-xs text-slate-700">{value}</pre>
    </div>
  );
}

function message(error: unknown): string {
  return error instanceof ApiError && typeof error.detail === 'string'
    ? error.detail
    : 'Could not load or save configuration.';
}

function syntaxMessage(error: unknown, source: string): string {
  if (!(error instanceof Error)) return 'Invalid JSON.';
  const position = /position\s+(\d+)/i.exec(error.message);
  if (!position) return `Invalid JSON: ${error.message}`;

  const offset = Math.min(Number(position[1]), source.length);
  const preceding = source.slice(0, offset);
  const line = preceding.split('\n').length;
  const lastNewline = preceding.lastIndexOf('\n');
  const column = offset - lastNewline;
  return `Invalid JSON at line ${line}, column ${column}: ${error.message}`;
}
