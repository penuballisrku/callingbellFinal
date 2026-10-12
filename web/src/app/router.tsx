import { useEffect, useState } from 'react';
import { createBrowserRouter, Link, redirect, useRouteError, type LoaderFunctionArgs } from 'react-router';
import { Button } from '@mui/material';
import CustomerLayout from '@/layouts/CustomerLayout';
import { RequireAuth } from '@/features/auth/RequireAuth';
import { EmptyState } from '@/components/ui';

const page = (loader: () => Promise<{ default: React.ComponentType }>) => async () => ({ Component: (await loader()).default });

/** Owner dashboard sections that used to live under /business/ (links in older notifications and emails still use them). */
const OWNER_SECTIONS = new Set(['setup', 'leads', 'bookings', 'reviews', 'services', 'profile', 'media', 'plan', 'advertising', 'settings']);
const search = (request: Request) => new URL(request.url).search;

/** /business/{slug} is a public business page; /business/{section} is an old owner-dashboard address, now /owner/{section}. */
const businessLoader = ({ params, request }: LoaderFunctionArgs) => {
  const slug = params.slug ?? '';
  if (OWNER_SECTIONS.has(slug)) throw redirect(`/owner/${slug}${params['*'] ? `/${params['*']}` : ''}${search(request)}`);
  return null;
};

/** Portal routes: role guard wraps the lazily-loaded portal, which owns its nested routes. */
const guarded = (role: string, loader: () => Promise<{ default: React.ComponentType }>) => async () => {
  const Portal = (await loader()).default;
  return { Component: () => <RequireAuth role={role}><Portal /></RequireAuth> };
};

/** Shown while a lazily-loaded route chunk downloads on first visit. */
const Loading = () => <div className="min-h-screen bg-canvas" aria-busy="true" />;

/**
 * A page's code that no longer exists on the server: the tab was opened before a deploy (or before the dev server re-bundled its
 * packages) and asks for the old files. Reloading fetches the current ones.
 */
const isStaleChunk = (error: unknown) => /dynamically imported module|Importing a module script failed|error loading dynamically imported module|Outdated Optimize Dep/i
  .test(String((error as Error | undefined)?.message ?? error ?? ''));

/** Reloads once to pick up the current files; a second failure within a minute shows the error instead of reloading in a loop. */
export function reloadForStaleChunk(): boolean {
  const key = 'cb-chunk-reload';
  try {
    const last = Number(sessionStorage.getItem(key) ?? 0);
    if (Date.now() - last < 60_000) return false;
    sessionStorage.setItem(key, String(Date.now()));
  } catch { /* storage blocked: reload anyway */ }
  window.location.reload();
  return true;
}

function RouteError() {
  const error = useRouteError() as { status?: number } | undefined;
  // Rendered as a route's own element (unknown addresses) there is no error: that is a missing page.
  const notFound = !error || error.status === 404;
  const stale = !notFound && isStaleChunk(error);
  const [reloading] = useState(() => stale && reloadForStaleChunk());
  // Keep the cause visible to developers and error monitoring; visitors only see the friendly message below.
  useEffect(() => { if (!notFound && !reloading) console.error('Route error:', error); }, [error, notFound, reloading]);
  if (reloading) return <Loading />;
  return (
    <div className="container-page py-20">
      <EmptyState title={notFound ? 'Page not found' : stale ? 'A new version is available' : 'Something went wrong'}
        message={notFound ? "The page you're looking for doesn't exist or has moved."
          : stale ? 'CallInBell was updated while this page was open. Refresh to load the latest version.' : 'Please refresh the page or go back home.'}
        action={stale || !notFound
          ? <Button variant="contained" onClick={() => window.location.reload()}>Refresh the page</Button>
          : <Button component={Link} to="/" variant="contained">Go to home</Button>} />
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
      { path: 'nearby', lazy: page(() => import('@/features/places/PlacesPage')) },
      { path: 'nearby/place/:id', lazy: page(() => import('@/features/places/PlaceDetailPage')) },
      { path: 'categories', lazy: page(() => import('@/features/categories/CategoriesPage')) },
      { path: 'categories/:slug', lazy: page(() => import('@/features/categories/CategoriesPage')) },
      { path: 'business/:slug', loader: businessLoader, lazy: page(() => import('@/features/business/BusinessPage')) },
      { path: 'business/:slug/*', loader: businessLoader, element: <RouteError /> },
      { path: 'business', loader: ({ request }) => redirect(`/owner${search(request)}`) },
      // Former business page address.
      { path: 'b/:slug', loader: ({ params }) => redirect(`/business/${params.slug}`) },
      { path: 'category/:slug', lazy: page(() => import('@/features/seo/LandingPage')) },
      { path: 'location/*', lazy: page(() => import('@/features/seo/LandingPage')) },
      { path: 'pricing', lazy: page(() => import('@/features/pricing/PricingPage')) },
      { path: 'list-your-business', lazy: page(() => import('@/features/list-business/ListBusinessPage')) },
      { path: 'about', lazy: page(() => import('@/features/about/AboutPage')) },
      { path: 'trust-and-safety', lazy: page(() => import('@/features/about/TrustSafetyPage')) },
      { path: 'support', lazy: page(() => import('@/features/about/ContactSupportPage')) },
      { path: 'login', lazy: page(() => import('@/features/auth/LoginPage')) },
      { path: 'register', lazy: page(() => import('@/features/auth/RegisterPage')) },
      // Video consultations: full screen, for the two people in the call.
      { path: 'video/:roomId', lazy: page(() => import('@/features/video/VideoRoomPage')) },
      {
        path: 'account',
        element: <RequireAuth />,
        children: [
          { index: true, lazy: page(() => import('@/features/account/AccountPage')) },
          { path: 'bookings', loader: () => redirect('/account?tab=upcoming') },
        ],
      },
      { path: '*', element: <RouteError /> },
    ],
  },
  {
    path: 'owner/*',
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
