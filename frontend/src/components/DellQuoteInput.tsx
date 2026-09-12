import { useEffect, useState } from 'react';

import { DELL_QUOTE_ID_PATTERN } from '../constants';
import type { UploadState } from './Dropzone';
import { ProgressPanel } from './ProgressPanel';

export function DellQuoteInput({
  quoteId,
  state,
  validationRequested,
  onQuoteId,
}: {
  quoteId: string;
  state: UploadState;
  validationRequested: boolean;
  onQuoteId: (value: string) => void;
}) {
  const [touched, setTouched] = useState(false);

  useEffect(() => {
    if (!quoteId) setTouched(false);
  }, [quoteId]);

  if (state !== 'idle') {
    return <ProgressPanel label={`Quote ${quoteId}`} badge="DELL" state={state} />;
  }

  const showError = (touched || validationRequested) && !DELL_QUOTE_ID_PATTERN.test(quoteId);

  return (
    <div
      className={
        'flex h-[180px] flex-col justify-center rounded-2xl border-2 border-dashed bg-white p-6 transition-colors ' +
        (showError ? 'border-red-300 bg-red-50' : 'border-slate-300')
      }
    >
      <label className="mx-auto flex w-full max-w-lg flex-col gap-2">
        <span className="label">Dell quote ID</span>
        <input
          className={'field ' + (showError ? 'border-red-300 focus:border-red-400 focus:shadow-[0_0_0_3px_rgba(248,113,113,0.18)]' : '')}
          type="text"
          inputMode="decimal"
          autoComplete="off"
          placeholder="9000000000003.1"
          value={quoteId}
          aria-invalid={showError}
          aria-describedby={showError ? 'dell-quote-id-error' : 'dell-quote-id-help'}
          onBlur={() => setTouched(true)}
          onChange={(event) => onQuoteId(event.target.value)}
        />
        {showError ? (
          <span id="dell-quote-id-error" className="text-xs font-semibold text-red-600">
            Enter 13 digits, a full stop, and a one- or two-digit version.
          </span>
        ) : (
          <span id="dell-quote-id-help" className="text-[11px] text-slate-500">
            e.g. 9000000000003.1
          </span>
        )}
      </label>
    </div>
  );
}
