import { useEffect, useState } from 'react';

import type { Role, User } from '../types';

export function UserModal({
  user,
  onClose,
  onSave,
}: {
  user?: User | null;
  onClose: () => void;
  onSave: (payload: { username: string; role: Role }) => Promise<void>;
}) {
  const [username, setUsername] = useState('');
  const [role, setRole] = useState<Role>('user');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    setUsername(user?.username ?? '');
    setRole(user?.role ?? 'user');
  }, [user]);

  return (
    <div className="fixed inset-0 z-40 grid place-items-center bg-ink/30 px-4">
      <form
        className="w-full max-w-md rounded-xl border-[1.5px] border-ink bg-paper p-6 shadow-panel"
        onSubmit={async (event) => {
          event.preventDefault();
          setSaving(true);
          await onSave({ username, role });
          setSaving(false);
        }}
      >
        <div className="label">{user ? 'Edit user' : 'Add user'}</div>
        <label className="mt-5 flex flex-col gap-2">
          <span className="label">Username</span>
          <input className="field" value={username} onChange={(event) => setUsername(event.target.value)} autoFocus />
        </label>
        <label className="mt-4 flex flex-col gap-2">
          <span className="label">Role</span>
          <select className="field" value={role} onChange={(event) => setRole(event.target.value as Role)}>
            <option value="user">User</option>
            <option value="admin">Admin</option>
          </select>
        </label>
        <div className="mt-6 flex justify-end gap-2">
          <button type="button" className="button" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="button button-primary" disabled={!username.trim() || saving}>
            Save
          </button>
        </div>
      </form>
    </div>
  );
}
