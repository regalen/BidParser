export function relativeTime(iso: string, now: Date = new Date()): string {
  const value = new Date(iso);
  const seconds = Math.floor((now.getTime() - value.getTime()) / 1000);
  if (seconds < 60) return 'just now';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  if (days === 1) return 'Yesterday';
  if (days < 7) return `${days} days ago`;
  return value.toLocaleDateString('en-GB');
}
