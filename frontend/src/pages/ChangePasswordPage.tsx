import { FormEvent, useMemo, useState } from 'react';
import { useBlocker, useNavigate } from 'react-router-dom';

import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { AppHeader } from '../components/AppHeader';

export function ChangePasswordPage() {
  const { changePassword } = useAuth();
  const navigate = useNavigate();
  const [oldPassword, setOldPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);

  useBlocker(() => !done);

  const valid = useMemo(() => {
    return newPassword.length >= 8 && /[A-Z]/.test(newPassword) && /\d/.test(newPassword) && /[^A-Za-z0-9]/.test(newPassword) && newPassword === confirm;
  }, [newPassword, confirm]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError('');
    try {
      await changePassword(oldPassword, newPassword);
      setDone(true);
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
    <div className="min-h-screen bg-paper-tint">
      <AppHeader bare />
      <main className="grid min-h-[calc(100vh-56px)] place-items-center px-4 py-12">
        <form className="w-full max-w-[460px] rounded-xl border-[1.5px] border-ink bg-paper p-7 shadow-panel" onSubmit={submit}>
          <div className="label">Password required</div>
          <h1 className="mt-3 text-2xl font-semibold text-ink">Choose a new password</h1>
          <p className="mt-2 text-sm leading-6 text-ink-soft">New and reset accounts need a compliant password before using the parser.</p>
          <label className="mt-7 flex flex-col gap-2">
            <span className="label">Old password</span>
            <input className="field" type="password" value={oldPassword} onChange={(event) => setOldPassword(event.target.value)} />
          </label>
          <label className="mt-4 flex flex-col gap-2">
            <span className="label">New password</span>
            <input className="field" type="password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} />
            <span className="text-xs text-ink-mute">At least 8 characters, with an uppercase letter, a digit, and a symbol.</span>
          </label>
          <label className="mt-4 flex flex-col gap-2">
            <span className="label">Confirm password</span>
            <input className="field" type="password" value={confirm} onChange={(event) => setConfirm(event.target.value)} />
          </label>
          {error && <div className="mt-4 rounded-lg border-[1.5px] border-red-300 bg-red-50 px-3 py-2 text-xs font-semibold text-red-700">{error}</div>}
          <button type="submit" className="button button-primary mt-6 w-full" disabled={!oldPassword || !valid || busy}>
            Change password
          </button>
        </form>
      </main>
    </div>
  );
}
