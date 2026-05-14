import { FormEvent, useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { AppHeader } from '../components/AppHeader';

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [username, setUsername] = useState('admin');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError('');
    try {
      const user = await login(username, password);
      navigate(user.must_change_password ? '/change-password' : '/dashboard', { replace: true });
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 429) {
        setError(`Too many attempts. Try again in ${caught.retryAfter ?? '60'} seconds.`);
      } else {
        setError('Invalid username or password.');
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="min-h-screen bg-paper-tint">
      <AppHeader bare />
      <main className="grid min-h-[calc(100vh-56px)] place-items-center px-4 py-12">
        <form className="w-full max-w-[420px] rounded-xl border-[1.5px] border-ink bg-paper p-7 shadow-panel" onSubmit={submit}>
          <div className="label">Sign in</div>
          <h1 className="mt-3 text-[28px] font-semibold leading-tight tracking-normal text-ink">BidParser</h1>
          <p className="mt-2 text-sm leading-6 text-ink-soft">Use your internal account to parse supplier quotes into CRM-ready workbooks.</p>

          <label className="mt-7 flex flex-col gap-2">
            <span className="label">Username</span>
            <input className="field" value={username} onChange={(event) => setUsername(event.target.value)} autoComplete="username" />
          </label>
          <label className="mt-4 flex flex-col gap-2">
            <span className="label">Password</span>
            <input className="field" type="password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="current-password" />
          </label>
          {error && <div className="mt-4 rounded-lg border-[1.5px] border-red-300 bg-red-50 px-3 py-2 text-xs font-semibold text-red-700">{error}</div>}
          <button type="submit" className="button button-primary mt-6 w-full" disabled={!username || !password || busy}>
            Sign in
          </button>
        </form>
      </main>
    </div>
  );
}
