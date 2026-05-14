import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';

import { api, ApiError } from '../api/client';
import type { User } from '../types';

interface AuthContextValue {
  user: User | null;
  loading: boolean;
  login: (username: string, password: string) => Promise<User>;
  logout: () => Promise<void>;
  changePassword: (oldPassword: string, newPassword: string) => Promise<void>;
  refresh: () => Promise<User | null>;
  setUser: (user: User | null) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    try {
      const next = await api.me();
      setUser(next);
      return next;
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        setUser(null);
        return null;
      }
      setUser(null);
      return null;
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    function handleUnauthorized() {
      setUser(null);
    }

    function handlePasswordChangeRequired() {
      setUser((current) => (current ? { ...current, must_change_password: true } : current));
    }

    window.addEventListener('bidparser:unauthorized', handleUnauthorized);
    window.addEventListener('bidparser:password-change-required', handlePasswordChangeRequired);
    return () => {
      window.removeEventListener('bidparser:unauthorized', handleUnauthorized);
      window.removeEventListener('bidparser:password-change-required', handlePasswordChangeRequired);
    };
  }, []);

  const login = useCallback(async (username: string, password: string) => {
    const next = await api.login(username, password);
    setUser(next);
    return next;
  }, []);

  const logout = useCallback(async () => {
    await api.logout();
    setUser(null);
  }, []);

  const changePassword = useCallback(async (oldPassword: string, newPassword: string) => {
    await api.changePassword(oldPassword, newPassword);
    await refresh();
  }, [refresh]);

  const value = useMemo(
    () => ({ user, loading, login, logout, changePassword, refresh, setUser }),
    [user, loading, login, logout, changePassword, refresh],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const value = useContext(AuthContext);
  if (!value) {
    throw new Error('useAuth must be used within AuthProvider');
  }
  return value;
}
