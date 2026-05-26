import { Check, Clipboard } from 'lucide-react';
import { useState } from 'react';

import type { FailedParseJob } from '../../types';
import { CategoryBadge } from './CategoryBadge';

export function FailureRowDetail({ failure }: { failure: FailedParseJob }) {
  const [copied, setCopied] = useState(false);

  async function handleCopy() {
    if (!failure.error_detail) return;
    try {
      await navigator.clipboard.writeText(failure.error_detail);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard API may be unavailable (insecure context); silently ignore.
    }
  }

  const isMismatch = failure.category === 'validation_mismatch';

  return (
    <div className="space-y-4 bg-slate-50 px-6 py-4">
      <dl className="grid grid-cols-1 gap-x-6 gap-y-2 text-sm sm:grid-cols-2">
        <Field label="Category">
          <CategoryBadge category={failure.category} />
        </Field>
        {isMismatch && failure.computed_total != null && (
          <Field label="Computed total">{failure.computed_total}</Field>
        )}
        {isMismatch && failure.quoted_total != null && (
          <Field label="Quoted total">{failure.quoted_total}</Field>
        )}
        {!isMismatch && failure.stage && <Field label="Stage">{failure.stage}</Field>}
        {!isMismatch && failure.hint && <Field label="Hint">{failure.hint}</Field>}
        {!isMismatch && failure.message && <Field label="Message">{failure.message}</Field>}
      </dl>

      <div className="rounded-md border border-slate-200 bg-white">
        <div className="flex items-center justify-between border-b border-slate-200 px-3 py-2">
          <span className="text-xs font-semibold uppercase tracking-wide text-slate-500">
            {isMismatch ? 'Detail' : 'Stack trace'}
          </span>
          <button
            type="button"
            onClick={handleCopy}
            disabled={!failure.error_detail}
            className="inline-flex items-center gap-1 rounded-md border border-slate-200 bg-white px-2 py-1 text-xs font-medium text-slate-700 transition-colors hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {copied ? <Check className="h-3.5 w-3.5" /> : <Clipboard className="h-3.5 w-3.5" />}
            {copied ? 'Copied' : 'Copy'}
          </button>
        </div>
        <pre className="max-h-96 overflow-auto whitespace-pre-wrap break-words p-3 font-mono text-xs leading-relaxed text-slate-800">
          {failure.error_detail || '(no detail captured)'}
        </pre>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-baseline gap-2">
      <dt className="min-w-[80px] text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</dt>
      <dd className="text-slate-800">{children}</dd>
    </div>
  );
}
