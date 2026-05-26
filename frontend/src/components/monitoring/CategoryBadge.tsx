type Category = 'magic_byte_mismatch' | 'parser_error' | 'unhandled_exception' | 'validation_mismatch' | string;

const STYLES: Record<string, { tone: string; label: string }> = {
  magic_byte_mismatch: {
    tone: 'bg-slate-100 text-slate-700 ring-slate-600/10',
    label: 'magic_byte_mismatch',
  },
  parser_error: {
    tone: 'bg-amber-50 text-amber-700 ring-amber-600/20',
    label: 'parser_error',
  },
  unhandled_exception: {
    tone: 'bg-red-50 text-red-700 ring-red-600/20',
    label: 'unhandled_exception',
  },
  validation_mismatch: {
    tone: 'bg-orange-50 text-orange-700 ring-orange-600/20',
    label: 'validation_mismatch',
  },
};

export function CategoryBadge({ category }: { category: Category }) {
  const style = STYLES[category] ?? {
    tone: 'bg-slate-100 text-slate-600 ring-slate-500/10',
    label: category,
  };
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset ${style.tone}`}>
      {style.label}
    </span>
  );
}
