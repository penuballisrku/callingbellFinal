import { Navigate, Route, Routes } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import SpaceDashboardOutlined from '@mui/icons-material/SpaceDashboardOutlined';
import StorefrontOutlined from '@mui/icons-material/StorefrontOutlined';
import CategoryOutlined from '@mui/icons-material/CategoryOutlined';
import LocationCityOutlined from '@mui/icons-material/LocationCityOutlined';
import PeopleAltOutlined from '@mui/icons-material/PeopleAltOutlined';
import RateReviewOutlined from '@mui/icons-material/RateReviewOutlined';
import CampaignOutlined from '@mui/icons-material/CampaignOutlined';
import WorkspacePremiumOutlined from '@mui/icons-material/WorkspacePremiumOutlined';
import { api } from '@/lib/api';
import type { AdminDashboard as Dashboard } from '@/lib/types';
import { PortalLayout } from '@/layouts/PortalLayout';
import AdminDashboard from './AdminDashboard';
import { AdminBusinessDetail, AdminBusinesses } from './AdminBusinesses';
import { AdminCategories, AdminCities } from './AdminCatalog';
import { AdminAds, AdminReviews, AdminSubscriptions, AdminUsers } from './AdminPeople';

export default function AdminPortal() {
  const { data } = useQuery({ queryKey: ['admin', 'dashboard'], queryFn: () => api.get<Dashboard>('/api/admin/dashboard'), staleTime: 60_000 });
  const a = data?.attention;
  const nav = [
    { to: '/admin', label: 'Dashboard', icon: <SpaceDashboardOutlined fontSize="small" />, end: true },
    { to: '/admin/businesses', label: 'Businesses', icon: <StorefrontOutlined fontSize="small" />, badge: a?.pendingApprovals },
    { to: '/admin/categories', label: 'Categories', icon: <CategoryOutlined fontSize="small" /> },
    { to: '/admin/cities', label: 'Cities', icon: <LocationCityOutlined fontSize="small" /> },
    { to: '/admin/users', label: 'Users', icon: <PeopleAltOutlined fontSize="small" /> },
    { to: '/admin/reviews', label: 'Review moderation', icon: <RateReviewOutlined fontSize="small" />, badge: a?.flaggedReviews },
    { to: '/admin/advertisements', label: 'Advertisements', icon: <CampaignOutlined fontSize="small" />, badge: a?.pendingAds },
    { to: '/admin/subscriptions', label: 'Subscriptions', icon: <WorkspacePremiumOutlined fontSize="small" /> },
  ];

  return (
    <Routes>
      <Route element={<PortalLayout title="Admin console" nav={nav} headerExtra={<span className="font-semibold">Calling Bell Admin</span>} />}>
        <Route index element={<AdminDashboard />} />
        <Route path="businesses" element={<AdminBusinesses />} />
        <Route path="businesses/:id" element={<AdminBusinessDetail />} />
        <Route path="categories" element={<AdminCategories />} />
        <Route path="cities" element={<AdminCities />} />
        <Route path="users" element={<AdminUsers />} />
        <Route path="reviews" element={<AdminReviews />} />
        <Route path="advertisements" element={<AdminAds />} />
        <Route path="subscriptions" element={<AdminSubscriptions />} />
        <Route path="*" element={<Navigate to="/admin" replace />} />
      </Route>
    </Routes>
  );
}
