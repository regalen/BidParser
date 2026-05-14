import { RotateCcw } from 'lucide-react';

export function ResetButton({ onClick }: { onClick: () => void }) {
  return (
    <button type="button" className="button border-red-200 bg-red-50 text-red-600" onClick={onClick}>
      <RotateCcw size={13} />
      Reset
    </button>
  );
}
