import { Navigate, Route, Routes } from 'react-router-dom';

import { useAuth } from './auth/AuthContext';
import { ChangePasswordPage } from './pages/ChangePasswordPage';
import { DashboardPage } from './pages/DashboardPage';
import { LoginPage } from './pages/LoginPage';
import { SettingsPage } from './pages/SettingsPage';

export function App() {
  const { user, loading } = useAuth();

  if (loading) {
    return (
      <div className="grid min-h-screen place-items-center bg-slate-50 text-slate-500">
        <span className="label">Loading BidParser</span>
      </div>
    );
  }

  return (
    <Routes>
      <Route path="/login" element={user ? <Navigate to={user.must_change_password ? '/change-password' : '/dashboard'} replace /> : <LoginPage />} />
      <Route path="/change-password" element={user ? <ChangePasswordPage /> : <Navigate to="/login" replace />} />
      <Route
        path="/dashboard"
        element={user ? (user.must_change_password ? <Navigate to="/change-password" replace /> : <DashboardPage />) : <Navigate to="/login" replace />}
      />
      <Route
        path="/settings"
        element={
          user ? (
            user.must_change_password ? (
              <Navigate to="/change-password" replace />
            ) : user.role === 'admin' ? (
              <SettingsPage />
            ) : (
              <Navigate to="/dashboard" replace />
            )
          ) : (
            <Navigate to="/login" replace />
          )
        }
      />
      <Route path="*" element={<Navigate to={user ? (user.must_change_password ? '/change-password' : '/dashboard') : '/login'} replace />} />
    </Routes>
  );
}
