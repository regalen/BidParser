import { Edit2, KeyRound, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';

import { api, ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { AppHeader } from '../components/AppHeader';
import { UserModal } from '../components/UserModal';
import type { Role, User } from '../types';

export function SettingsPage() {
  const { user } = useAuth();
  const [users, setUsers] = useState<User[]>([]);
  const [modalUser, setModalUser] = useState<User | null | undefined>(undefined);
  const [error, setError] = useState('');

  async function loadUsers() {
    setUsers(await api.users());
  }

  useEffect(() => {
    void loadUsers();
  }, []);

  async function saveUser(payload: { username: string; role: Role }) {
    setError('');
    try {
      if (modalUser) {
        await api.updateUser(modalUser.id, payload);
      } else {
        await api.createUser(payload);
      }
      setModalUser(undefined);
      await loadUsers();
    } catch (caught) {
      setError(caught instanceof ApiError && typeof caught.detail === 'string' ? caught.detail : 'Could not save user.');
    }
  }

  async function resetPassword(target: User) {
    await api.updateUser(target.id, { reset_password: true });
    await loadUsers();
  }

  async function removeUser(target: User) {
    setError('');
    try {
      await api.deleteUser(target.id);
      await loadUsers();
    } catch (caught) {
      setError(caught instanceof ApiError && typeof caught.detail === 'string' ? caught.detail : 'Could not delete user.');
    }
  }

  return (
    <div className="min-h-screen bg-paper">
      <AppHeader />
      <main className="mx-auto max-w-[1080px] px-5 py-8 md:px-12">
        <div className="flex items-end justify-between">
          <div>
            <h1 className="text-[26px] font-semibold leading-none text-ink">Settings</h1>
            <div className="label label-faint mt-2">Admin user management</div>
          </div>
          <button type="button" className="button button-primary" onClick={() => setModalUser(null)}>
            <Plus size={14} />
            Add user
          </button>
        </div>
        {error && <div className="mt-5 rounded-lg border-[1.5px] border-red-300 bg-red-50 px-3 py-2 text-xs font-semibold text-red-700">{error}</div>}

        <section className="mt-7 overflow-hidden rounded-xl border-[1.5px] border-ink bg-paper">
          <div className="grid grid-cols-[1.4fr_0.7fr_0.9fr_0.9fr_150px] gap-3 border-b border-ink-faint bg-slate-50 px-[18px] py-3">
            <span className="label label-faint">Username</span>
            <span className="label label-faint">Role</span>
            <span className="label label-faint">Password</span>
            <span className="label label-faint">Created</span>
            <span className="label label-faint text-right">Actions</span>
          </div>
          {users.map((row, index) => (
            <div key={row.id} className={['grid grid-cols-[1.4fr_0.7fr_0.9fr_0.9fr_150px] items-center gap-3 px-[18px] py-3 text-sm', index < users.length - 1 ? 'border-b border-ink-faint' : ''].join(' ')}>
              <span className="font-semibold text-ink">{row.username}</span>
              <span className="text-ink-soft">{row.role}</span>
              <span className={row.must_change_password ? 'font-semibold text-amber-700' : 'text-ink-soft'}>{row.must_change_password ? 'Change required' : 'Active'}</span>
              <span className="text-ink-soft">{row.created_at ? new Date(row.created_at).toLocaleDateString() : '-'}</span>
              <div className="flex justify-end gap-1">
                <button type="button" className="icon-button" title="Edit user" onClick={() => setModalUser(row)}>
                  <Edit2 size={14} />
                </button>
                <button type="button" className="icon-button" title="Reset password" onClick={() => resetPassword(row)}>
                  <KeyRound size={14} />
                </button>
                <button type="button" className="icon-button text-red-600 disabled:opacity-35" title="Delete user" disabled={row.id === user?.id} onClick={() => removeUser(row)}>
                  <Trash2 size={14} />
                </button>
              </div>
            </div>
          ))}
        </section>
      </main>
      {modalUser !== undefined && <UserModal user={modalUser} onClose={() => setModalUser(undefined)} onSave={saveUser} />}
    </div>
  );
}
