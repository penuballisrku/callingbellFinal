import { createBrowserRouter, Link, useRouteError } from 'react-router';
import { Button } from '@mui/material';
import CustomerLayout from '@/layouts/CustomerLayout';
import { RequireAuth } from '@/features/auth/RequireAuth';
import { EmptyState } from '@/components/ui';

const page = (loader: () => Promise<{ default: React.ComponentType }>) => async () => ({ Component: (await loader()).default });

/** Portal routes: role guard wraps the lazily-loaded portal, which owns its nested routes. */
const guarded = (role: string, loader: () => Promise<{ default: React.ComponentType }>) => async () => {
  const Portal = (await loader()).default;
  return { Component: () => <RequireAuth role={role}><Portal /></RequireAuth> };
};

/** Shown while a lazily-loaded route chunk downloads on first visit. */
const Loading = () => <div className="min-h-screen bg-canvas" aria-busy="true" />;

function RouteError() {
  const error = useRouteError() as { status?: number } | undefined;
  return (
    <div className="container-page py-20">
      <EmptyState title={error?.status === 404 ? 'Page not found' : 'Something went wrong'}
        message={error?.status === 404 ? "The page you're looking for doesn't exist or has moved." : 'Please refresh the page or go back home.'}
        action={<Button component={Link} to="/" variant="contained">Go to home</Button>} />
    </div>
  );
}

export const router = createBrowserRouter([
  {
    element: <CustomerLayout />,
    errorElement: <RouteError />,
    hydrateFallbackElement: <Loading />,
    children: [
      { index: true, lazy: page(() => import('@/features/home/HomePage')) },
      { path: 'search', lazy: page(() => import('@/features/search/SearchPage')) },
      { path: 'categories', lazy: page(() => import('@/features/categories/CategoriesPage')) },
      { path: 'categories/:slug', lazy: page(() => import('@/features/categories/CategoriesPage')) },
      { path: 'b/:slug', lazy: page(() => import('@/features/business/BusinessPage')) },
      { path: 'pricing', lazy: page(() => import('@/features/pricing/PricingPage')) },
      { path: 'list-your-business', lazy: page(() => import('@/features/list-business/ListBusinessPage')) },
      { path: 'login', lazy: page(() => import('@/features/auth/LoginPage')) },
      { path: 'register', lazy: page(() => import('@/features/auth/RegisterPage')) },
      {
        path: 'account',
        element: <RequireAuth />,
        children: [{ index: true, lazy: page(() => import('@/features/account/AccountPage')) }],
      },
      { path: '*', element: <RouteError /> },
    ],
  },
  {
    path: 'business/*',
    errorElement: <RouteError />,
    hydrateFallbackElement: <Loading />,
    lazy: guarded('BusinessOwner', () => import('@/features/owner/OwnerPortal')),
  },
  {
    path: 'admin/*',
    errorElement: <RouteError />,
    hydrateFallbackElement: <Loading />,
    lazy: guarded('Administrator', () => import('@/features/admin/AdminPortal')),
  },
]);
