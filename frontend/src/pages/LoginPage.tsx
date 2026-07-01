import { FormEvent, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Loader2 } from 'lucide-react';

import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { Footer } from '../components/Footer';

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [username, setUsername] = useState('');
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
      } else if (caught instanceof ApiError && caught.status === 401) {
        setError('Invalid username or password.');
      } else {
        setError('Could not sign in. Please try again.');
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
            <img src="/logo.png" alt="" className="mx-auto mb-4 h-12 w-12 rounded-xl" />
            <h1 className="text-2xl font-bold text-slate-900">Welcome Back</h1>
            <p className="text-sm text-slate-500">Sign in to BidParser</p>
          </div>

          <div className="space-y-4">
            <label className="block space-y-2">
              <span className="text-xs font-bold uppercase tracking-wider text-slate-400">Username</span>
              <input
                value={username}
                onChange={(event) => setUsername(event.target.value)}
                autoComplete="username"
                required
                className="w-full rounded-md border border-slate-200 bg-white px-3 py-2 text-sm text-slate-900 outline-none transition-colors focus:border-accent focus:ring-2 focus:ring-accent/20"
              />
            </label>
            <label className="block space-y-2">
              <span className="text-xs font-bold uppercase tracking-wider text-slate-400">Password</span>
              <input
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                autoComplete="current-password"
                required
                className="w-full rounded-md border border-slate-200 bg-white px-3 py-2 text-sm text-slate-900 outline-none transition-colors focus:border-accent focus:ring-2 focus:ring-accent/20"
              />
            </label>
            {error && <p className="text-xs font-bold text-red-500">{error}</p>}
            <button
              type="submit"
              disabled={!username || !password || busy}
              className="flex h-11 w-full items-center justify-center gap-2 rounded-md bg-accent font-bold uppercase tracking-wider text-white transition-colors hover:bg-accent/90 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {busy ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Signing in…
                </>
              ) : (
                'Sign in'
              )}
            </button>
          </div>
        </form>
      </div>
      <Footer />
    </div>
  );
}
