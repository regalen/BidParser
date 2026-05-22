const APP_VERSION = import.meta.env.VITE_APP_VERSION ?? 'dev';
const versionDisplay = /^\d/.test(APP_VERSION) ? `v${APP_VERSION}` : APP_VERSION;

export function Footer() {
  return (
    <footer className="border-t border-slate-200 bg-white px-8 py-3 text-[10px] font-bold uppercase tracking-widest text-slate-400">
      <div className="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-3">
        <span className="normal-case">
          <span className="tracking-tight text-slate-500">BidParser</span>{' '}
          <span className="text-slate-500">{versionDisplay}</span>
        </span>
      </div>
    </footer>
  );
}
