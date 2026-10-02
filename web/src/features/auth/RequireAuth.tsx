import type { ReactNode } from 'react';
import { Link, Navigate, Outlet, useLocation } from 'react-router';
import { Button } from '@mui/material';
import { useAuth } from '@/stores/auth';
import { EmptyState } from '@/components/ui';

export function RequireAuth({ role, children }: { role?: string; children?: ReactNode }) {
  const user = useAuth((s) => s.user);
  const location = useLocation();
  if (!user) return <Navigate to={`/login?returnUrl=${encodeURIComponent(location.pathname + location.search)}`} replace />;
  if (role && !user.roles.includes(role)) {
    return (
      <div className="container-page py-20">
        <EmptyState title="You don't have access to this area"
          message={`This section is for ${role === 'Administrator' ? 'Calling Bell administrators' : 'business owners'}. Sign in with an account that has access.`}
          action={<Button component={Link} to="/" variant="contained">Back to home</Button>} />
      </div>
    );
  }
  return children ?? <Outlet />;
}
