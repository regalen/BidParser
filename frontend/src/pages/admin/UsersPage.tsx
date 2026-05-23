import { AlertCircle, Edit2, KeyRound, Plus, Shield, Trash2, User as UserIcon, X } from 'lucide-react';
import { useEffect, useState } from 'react';

import { api, ApiError } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import { AppHeader } from '../../components/AppHeader';
import { Footer } from '../../components/Footer';
import { UserModal } from '../../components/UserModal';
import type { Role, User } from '../../types';

export function UsersPage() {
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

  async function saveUser(payload: { username: string; name: string; role: Role }) {
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
      setError(
        caught instanceof ApiError && typeof (caught as ApiError).detail === 'string' ? (caught as ApiError).detail as string : 'Could not save user.',
      );
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
      setError(
        caught instanceof ApiError && typeof (caught as ApiError).detail === 'string' ? (caught as ApiError).detail as string : 'Could not delete user.',
      );
    }
  }

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <AppHeader />
      <main className="mx-auto w-full max-w-6xl flex-1 px-6 py-8 lg:px-8">
        <div className="flex items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-slate-900">User Management</h1>
            <p className="mt-1 text-sm text-slate-500">Add, edit, and manage access to BidParser.</p>
          </div>
          <button type="button" className="button button-primary" onClick={() => setModalUser(null)}>
            <Plus className="h-3.5 w-3.5" />
            Create user
          </button>
        </div>

        {error && (
          <div className="mt-6 flex items-center gap-2 rounded-xl border border-red-100 bg-red-50 px-4 py-3 text-xs font-bold text-red-600">
            <AlertCircle className="h-4 w-4" />
            {error}
            <button
              type="button"
              className="ml-auto flex h-6 w-6 items-center justify-center rounded-full text-red-400 hover:bg-red-100"
              onClick={() => setError('')}
            >
              <X className="h-3 w-3" />
            </button>
          </div>
        )}

        <div className="mt-8 grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-3">
          {users.map((row) => (
            <UserCard
              key={row.id}
              row={row}
              canDelete={row.id !== user?.id}
              onEdit={() => setModalUser(row)}
              onResetPassword={() => resetPassword(row)}
              onDelete={() => removeUser(row)}
            />
          ))}
        </div>
      </main>
      <Footer />
      {modalUser !== undefined && (
        <UserModal user={modalUser} onClose={() => setModalUser(undefined)} onSave={saveUser} />
      )}
    </div>
  );
}

function UserCard({
  row,
  canDelete,
  onEdit,
  onResetPassword,
  onDelete,
}: {
  row: User;
  canDelete: boolean;
  onEdit: () => void;
  onResetPassword: () => void;
  onDelete: () => void;
}) {
  const isAdmin = row.role === 'admin';
  const RoleIcon = isAdmin ? Shield : UserIcon;

  return (
    <div className="group relative overflow-hidden rounded-xl border border-slate-200 bg-white p-6 shadow-sm transition-all hover:border-accent/50">
      <div className="absolute right-2 top-2 flex gap-1 opacity-0 transition-opacity group-hover:opacity-100">
        <button
          type="button"
          className="flex h-8 w-8 items-center justify-center rounded-md text-slate-400 transition-colors hover:bg-slate-100 hover:text-accent"
          title="Edit user"
          onClick={onEdit}
        >
          <Edit2 className="h-3.5 w-3.5" />
        </button>
        <button
          type="button"
          className="flex h-8 w-8 items-center justify-center rounded-md text-slate-400 transition-colors hover:bg-slate-100 hover:text-amber-600"
          title="Reset password"
          onClick={onResetPassword}
        >
          <KeyRound className="h-3.5 w-3.5" />
        </button>
        <button
          type="button"
          className="flex h-8 w-8 items-center justify-center rounded-md text-slate-400 transition-colors hover:bg-red-50 hover:text-red-500 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent disabled:hover:text-slate-400"
          title="Delete user"
          disabled={!canDelete}
          onClick={onDelete}
        >
          <Trash2 className="h-3.5 w-3.5" />
        </button>
      </div>

      <div className="flex items-center gap-4">
        <div
          className={
            'flex h-12 w-12 items-center justify-center rounded-full border ' +
            (isAdmin ? 'border-red-100 bg-red-50' : 'border-slate-100 bg-slate-50')
          }
        >
          <RoleIcon className={'h-5 w-5 ' + (isAdmin ? 'text-red-500' : 'text-slate-400')} />
        </div>
        <div className="min-w-0 flex-1">
          <h3 className="truncate font-bold text-slate-900">{row.name || row.username}</h3>
          <div className="mt-0.5 flex items-center gap-2">
            <span className="truncate text-xs text-slate-500">@{row.username}</span>
            <span
              className={
                'inline-flex items-center rounded px-1.5 py-0.5 text-[9px] font-bold uppercase tracking-wider text-white ' +
                (isAdmin ? 'bg-red-500' : 'bg-slate-500')
              }
            >
              {row.role}
            </span>
          </div>
        </div>
      </div>

      <div className="mt-5 flex items-center justify-between border-t border-slate-100 pt-4 text-[11px] font-medium text-slate-500">
        <span>
          {row.must_change_password ? (
            <span className="font-bold text-amber-600">Password change required</span>
          ) : (
            <span>Account active</span>
          )}
        </span>
        <span className="text-slate-400">{row.created_at ? new Date(row.created_at).toLocaleDateString() : '—'}</span>
      </div>
    </div>
  );
}
