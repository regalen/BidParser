import { Bug, Github } from 'lucide-react';

const REPO_URL = 'https://github.com/regalen/BidParser';
const ISSUES_URL = 'https://github.com/regalen/BidParser/issues';
const APP_VERSION = '0.1.0';

export function Footer() {
  return (
    <footer className="border-t border-slate-200 bg-white px-8 py-3 text-[10px] font-bold uppercase tracking-widest text-slate-400">
      <div className="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-3">
        <span className="normal-case">
          <span className="tracking-tight text-slate-500">BidParser</span>{' '}
          <span className="text-slate-500">v{APP_VERSION}</span>
        </span>
        <nav className="flex items-center gap-5">
          <a
            href={REPO_URL}
            target="_blank"
            rel="noopener noreferrer"
            className="flex items-center gap-1.5 transition-colors hover:text-accent"
          >
            <Github className="h-3 w-3" />
            GitHub
          </a>
          <a
            href={ISSUES_URL}
            target="_blank"
            rel="noopener noreferrer"
            className="flex items-center gap-1.5 transition-colors hover:text-accent"
          >
            <Bug className="h-3 w-3" />
            Report an Issue
          </a>
        </nav>
      </div>
    </footer>
  );
}
