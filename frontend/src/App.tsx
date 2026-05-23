import { Navigate, Route, Routes } from 'react-router-dom';

import { useAuth } from './auth/AuthContext';
import { ChangePasswordPage } from './pages/ChangePasswordPage';
import { DashboardPage } from './pages/DashboardPage';
import { LoginPage } from './pages/LoginPage';
import { MetricsDashboard } from './pages/admin/MetricsDashboard';
import { UsersPage } from './pages/admin/UsersPage';

function AdminRoute({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  if (user.must_change_password) return <Navigate to="/change-password" replace />;
  if (user.role !== 'admin') return <Navigate to="/dashboard" replace />;
  return <>{children}</>;
}

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
      
      {/* Admin Shell Routes */}
      <Route path="/settings" element={<Navigate to="/admin/users" replace />} />
      <Route path="/admin/users" element={<AdminRoute><UsersPage /></AdminRoute>} />
      <Route path="/admin/metrics" element={<AdminRoute><MetricsDashboard /></AdminRoute>} />
      
      <Route path="*" element={<Navigate to={user ? (user.must_change_password ? '/change-password' : '/dashboard') : '/login'} replace />} />
    </Routes>
  );
}
