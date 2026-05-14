import { RotateCcw } from 'lucide-react';

export function ResetButton({ onClick }: { onClick: () => void }) {
  return (
    <button type="button" className="button button-danger" onClick={onClick}>
      <RotateCcw className="h-3.5 w-3.5" />
      Reset
    </button>
  );
}
