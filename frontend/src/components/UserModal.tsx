import { Loader2, X } from 'lucide-react';
import { useEffect, useState } from 'react';

import type { Role, User } from '../types';

export function UserModal({
  user,
  onClose,
  onSave,
}: {
  user?: User | null;
  onClose: () => void;
  onSave: (payload: { username: string; name: string; role: Role }) => Promise<void>;
}) {
  const [username, setUsername] = useState('');
  const [name, setName] = useState('');
  const [role, setRole] = useState<Role>('user');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    setUsername(user?.username ?? '');
    setName(user?.name ?? '');
    setRole(user?.role ?? 'user');
  }, [user]);

  return (
    <div className="fixed inset-0 z-40 grid place-items-center bg-slate-900/40 px-4">
      <form
        className="w-full max-w-md rounded-xl border border-slate-200 bg-white p-6 shadow-2xl"
        onSubmit={async (event) => {
          event.preventDefault();
          setSaving(true);
          await onSave({ username, name, role });
          setSaving(false);
        }}
      >
        <div className="flex items-start justify-between">
          <div>
            <h2 className="text-lg font-bold text-slate-900">{user ? 'Edit user' : 'Create user'}</h2>
            <p className="mt-1 text-xs text-slate-500">
              {user ? 'Update the user details.' : 'New users start with password "changeme" and must change on first sign-in.'}
            </p>
          </div>
          <button
            type="button"
            className="flex h-8 w-8 items-center justify-center rounded-full text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
            onClick={onClose}
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <label className="mt-6 flex flex-col gap-2">
          <span className="label">Full name</span>
          <input
            className="field"
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="John Doe"
            autoFocus
            required
          />
          <span className="text-[11px] text-slate-500">Displayed in the UI and on reports.</span>
        </label>
        <label className="mt-4 flex flex-col gap-2">
          <span className="label">Username</span>
          <input
            className="field"
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            placeholder="jdoe"
            required
          />
          <span className="text-[11px] text-slate-500">Used to sign in. Lowercase, no spaces.</span>
        </label>
        <label className="mt-4 flex flex-col gap-2">
          <span className="label">Role</span>
          <select className="field appearance-none" value={role} onChange={(event) => setRole(event.target.value as Role)}>
            <option value="user">User</option>
            <option value="admin">Admin</option>
          </select>
        </label>

        <div className="mt-6 flex justify-end gap-2">
          <button type="button" className="button" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="button button-primary" disabled={!username.trim() || !name.trim() || saving}>
            {saving ? (
              <>
                <Loader2 className="h-3.5 w-3.5 animate-spin" />
                Saving
              </>
            ) : (
              'Save'
            )}
          </button>
        </div>
      </form>
    </div>
  );
}
