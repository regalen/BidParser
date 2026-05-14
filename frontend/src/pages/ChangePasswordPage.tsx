import { FormEvent, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { KeyRound, Loader2 } from 'lucide-react';

import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { Footer } from '../components/Footer';

export function ChangePasswordPage() {
  const { changePassword } = useAuth();
  const navigate = useNavigate();
  const [oldPassword, setOldPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const checks = useMemo(() => {
    return {
      length: newPassword.length >= 8,
      upper: /[A-Z]/.test(newPassword),
      digit: /\d/.test(newPassword),
      symbol: /[^A-Za-z0-9]/.test(newPassword),
      match: newPassword.length > 0 && newPassword === confirm,
    };
  }, [newPassword, confirm]);

  const valid = checks.length && checks.upper && checks.digit && checks.symbol && checks.match;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError('');
    try {
      await changePassword(oldPassword, newPassword);
      navigate('/dashboard', { replace: true });
    } catch (caught) {
      if (caught instanceof ApiError && Array.isArray(caught.detail)) {
        setError(caught.detail.join(' '));
      } else {
        setError('Could not change password. Check the old password and rules.');
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <div className="flex flex-1 items-center justify-center p-4">
        <form
          onSubmit={submit}
          className="w-full max-w-md space-y-6 rounded-xl border border-slate-200 bg-white p-8 shadow-xl"
        >
          <div className="space-y-2 text-center">
            <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-accent">
              <KeyRound className="h-6 w-6 text-white" />
            </div>
            <h1 className="text-2xl font-bold text-slate-900">Set a new password</h1>
            <p className="text-sm text-slate-500">First-time sign-in requires a new password</p>
          </div>

          <div className="space-y-4">
            <label className="block space-y-2">
              <span className="text-xs font-bold uppercase tracking-wider text-slate-400">Old password</span>
              <input
                type="password"
                value={oldPassword}
                onChange={(event) => setOldPassword(event.target.value)}
                autoComplete="current-password"
                required
                className="w-full rounded-md border border-slate-200 bg-white px-3 py-2 text-sm text-slate-900 outline-none transition-colors focus:border-accent focus:ring-2 focus:ring-accent/20"
              />
            </label>
            <label className="block space-y-2">
              <span className="text-xs font-bold uppercase tracking-wider text-slate-400">New password</span>
              <input
                type="password"
                value={newPassword}
                onChange={(event) => setNewPassword(event.target.value)}
                autoComplete="new-password"
                required
                className="w-full rounded-md border border-slate-200 bg-white px-3 py-2 text-sm text-slate-900 outline-none transition-colors focus:border-accent focus:ring-2 focus:ring-accent/20"
              />
              <ul className="space-y-1 pt-1 text-[11px] font-medium text-slate-500">
                <Rule met={checks.length}>At least 8 characters</Rule>
                <Rule met={checks.upper}>One uppercase letter</Rule>
                <Rule met={checks.digit}>One digit</Rule>
                <Rule met={checks.symbol}>One symbol</Rule>
              </ul>
            </label>
            <label className="block space-y-2">
              <span className="text-xs font-bold uppercase tracking-wider text-slate-400">Confirm password</span>
              <input
                type="password"
                value={confirm}
                onChange={(event) => setConfirm(event.target.value)}
                autoComplete="new-password"
                required
                className="w-full rounded-md border border-slate-200 bg-white px-3 py-2 text-sm text-slate-900 outline-none transition-colors focus:border-accent focus:ring-2 focus:ring-accent/20"
              />
              {confirm.length > 0 && !checks.match && (
                <span className="text-[11px] font-medium text-red-500">Passwords don't match</span>
              )}
            </label>
            {error && <p className="text-xs font-bold text-red-500">{error}</p>}
            <button
              type="submit"
              disabled={!oldPassword || !valid || busy}
              className="flex h-11 w-full items-center justify-center gap-2 rounded-md bg-accent font-bold uppercase tracking-wider text-white transition-colors hover:bg-accent/90 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {busy ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Saving…
                </>
              ) : (
                'Change password'
              )}
            </button>
          </div>
        </form>
      </div>
      <Footer />
    </div>
  );
}

function Rule({ met, children }: { met: boolean; children: React.ReactNode }) {
  return (
    <li className={'flex items-center gap-2 transition-colors ' + (met ? 'text-emerald-600' : 'text-slate-500')}>
      <span
        className={
          'inline-flex h-3 w-3 items-center justify-center rounded-full text-[8px] font-bold leading-none ' +
          (met ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-100 text-slate-400')
        }
      >
        {met ? '✓' : '•'}
      </span>
      {children}
    </li>
  );
}
