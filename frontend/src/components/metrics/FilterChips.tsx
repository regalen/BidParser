import { X } from 'lucide-react';
import { useSearchParams } from 'react-router';

import type { MetricsSummaryResponse } from '../../types';

export function FilterChips({ data }: { data: MetricsSummaryResponse | null }) {
  const [searchParams, setSearchParams] = useSearchParams();

  const vendor = searchParams.get('vendor');
  const userId = searchParams.get('userId');
  const parserSlug = searchParams.get('parserSlug');
  const importType = searchParams.get('importType');

  if (!vendor && !userId && !parserSlug && !importType) return null;

  const removeFilter = (key: string) => {
    const next = new URLSearchParams(searchParams);
    next.delete(key);
    setSearchParams(next);
  };

  const userMatch = userId
    ? data?.byUser.find((u) => u.userId !== null && String(u.userId) === userId)
    : undefined;
  const userDisplay = userMatch ? userMatch.name ?? userMatch.username : userId;

  const parserMatch = parserSlug ? data?.byParser.find((p) => p.parserSlug === parserSlug) : undefined;
  const parserDisplay = parserMatch?.displayName ?? parserSlug;

  const importTypeDisplay = importType === 'auto' ? 'Auto' : importType === 'manual' ? 'Manual' : importType;

  return (
    <div className="flex flex-wrap items-center gap-2 py-2">
      {vendor && (
        <Chip label={`Vendor: ${vendor}`} onRemove={() => removeFilter('vendor')} />
      )}
      {userId && (
        <Chip label={`User: ${userDisplay}`} onRemove={() => removeFilter('userId')} />
      )}
      {parserSlug && (
        <Chip label={`File Type: ${parserDisplay}`} onRemove={() => removeFilter('parserSlug')} />
      )}
      {importType && (
        <Chip label={`Import Type: ${importTypeDisplay}`} onRemove={() => removeFilter('importType')} />
      )}
    </div>
  );
}

function Chip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-3 py-1 text-sm font-medium text-slate-800">
      {label}
      <button type="button" onClick={onRemove} className="ml-1 rounded-full p-0.5 hover:bg-slate-200" aria-label={`Remove ${label}`}>
        <X className="h-3 w-3" />
      </button>
    </span>
  );
}
