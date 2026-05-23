import { X } from 'lucide-react';
import { useSearchParams } from 'react-router-dom';

export function FilterChips({ data }: { data: any }) {
  const [searchParams, setSearchParams] = useSearchParams();

  const vendor = searchParams.get('vendor');
  const userId = searchParams.get('userId');
  const parserSlug = searchParams.get('parserSlug');

  if (!vendor && !userId && !parserSlug) return null;

  const removeFilter = (key: string) => {
    const newParams = new URLSearchParams(searchParams);
    newParams.delete(key);
    setSearchParams(newParams);
  };

  // Attempt to find display names from the data
  const userDisplay = data?.by_user?.[0]?.name || data?.by_user?.[0]?.username || userId;
  const parserDisplay = data?.by_parser?.[0]?.display_name || parserSlug;

  return (
    <div className="flex flex-wrap items-center gap-2 py-2">
      {vendor && (
        <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-3 py-1 text-sm font-medium text-slate-800">
          Vendor: {vendor}
          <button onClick={() => removeFilter('vendor')} className="ml-1 rounded-full p-0.5 hover:bg-slate-200">
            <X className="h-3 w-3" />
          </button>
        </span>
      )}
      {userId && (
        <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-3 py-1 text-sm font-medium text-slate-800">
          User: {userDisplay}
          <button onClick={() => removeFilter('userId')} className="ml-1 rounded-full p-0.5 hover:bg-slate-200">
            <X className="h-3 w-3" />
          </button>
        </span>
      )}
      {parserSlug && (
        <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-3 py-1 text-sm font-medium text-slate-800">
          File Type: {parserDisplay}
          <button onClick={() => removeFilter('parserSlug')} className="ml-1 rounded-full p-0.5 hover:bg-slate-200">
            <X className="h-3 w-3" />
          </button>
        </span>
      )}
    </div>
  );
}
