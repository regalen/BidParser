import { useSearchParams } from 'react-router-dom';

function getLocalDateString(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

export function DateRangeControl() {
  const [searchParams, setSearchParams] = useSearchParams();

  const handlePreset = (days: number) => {
    const today = new Date();
    const from = new Date(today);
    from.setDate(today.getDate() - days);

    const newParams = new URLSearchParams(searchParams);
    newParams.set('from', getLocalDateString(from));
    newParams.set('to', getLocalDateString(today));
    setSearchParams(newParams);
  };

  const handleYear = () => {
    const today = new Date();
    const from = new Date(today.getFullYear(), 0, 1); // Jan 1st

    const newParams = new URLSearchParams(searchParams);
    newParams.set('from', getLocalDateString(from));
    newParams.set('to', getLocalDateString(today));
    setSearchParams(newParams);
  };

  const fromParam = searchParams.get('from');
  const toParam = searchParams.get('to');

  const isLast30 = !fromParam && !toParam; // Default is last 30
  const isLast90 =
    fromParam &&
    toParam &&
    fromParam === getLocalDateString(new Date(new Date().setDate(new Date().getDate() - 90))) &&
    toParam === getLocalDateString(new Date());
  const isThisYear =
    fromParam &&
    toParam &&
    fromParam === getLocalDateString(new Date(new Date().getFullYear(), 0, 1)) &&
    toParam === getLocalDateString(new Date());

  return (
    <div className="flex items-center gap-2">
      <span className="label">Date Range:</span>
      <select
        className="field w-40 !min-h-[32px] !py-1 !text-sm"
        value={isThisYear ? 'year' : isLast90 ? '90' : '30'}
        onChange={(e) => {
          if (e.target.value === '30') {
            const newParams = new URLSearchParams(searchParams);
            newParams.delete('from');
            newParams.delete('to');
            setSearchParams(newParams);
          } else if (e.target.value === '90') {
            handlePreset(90);
          } else if (e.target.value === 'year') {
            handleYear();
          }
        }}
      >
        <option value="30">Last 30 days</option>
        <option value="90">Last 90 days</option>
        <option value="year">This Year</option>
      </select>
    </div>
  );
}
