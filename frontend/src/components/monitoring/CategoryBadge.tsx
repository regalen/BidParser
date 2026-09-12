type Category = 'success' | 'magicByteMismatch' | 'parserError' | 'unhandledException' | 'validationMismatch' | string;

// The keys are the wire status values; the labels are what the operator reads. Keeping the two
// separate means a wire rename never leaks a machine identifier into the monitoring table.
const categoryStyles: Record<string, { tone: string; label: string }> = {
  success: {
    tone: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
    label: 'Success',
  },
  magicByteMismatch: {
    tone: 'bg-slate-100 text-slate-700 ring-slate-600/10',
    label: 'Magic byte mismatch',
  },
  parserError: {
    tone: 'bg-amber-50 text-amber-700 ring-amber-600/20',
    label: 'Parser error',
  },
  unhandledException: {
    tone: 'bg-red-50 text-red-700 ring-red-600/20',
    label: 'Unhandled exception',
  },
  validationMismatch: {
    tone: 'bg-orange-50 text-orange-700 ring-orange-600/20',
    label: 'Validation mismatch',
  },
};

export function CategoryBadge({ category }: { category: Category }) {
  const style = categoryStyles[category] ?? {
    tone: 'bg-slate-100 text-slate-600 ring-slate-500/10',
    label: category,
  };
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset ${style.tone}`}>
      {style.label}
    </span>
  );
}
