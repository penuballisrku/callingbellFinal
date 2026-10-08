import { createContext, useContext, useEffect } from 'react';
import { Link, Navigate, Route, Routes } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, MenuItem, Select, Skeleton } from '@mui/material';
import SpaceDashboardOutlined from '@mui/icons-material/SpaceDashboardOutlined';
import InboxOutlined from '@mui/icons-material/InboxOutlined';
import EventNoteOutlined from '@mui/icons-material/EventNoteOutlined';
import RateReviewOutlined from '@mui/icons-material/RateReviewOutlined';
import HandymanOutlined from '@mui/icons-material/HandymanOutlined';
import StorefrontOutlined from '@mui/icons-material/StorefrontOutlined';
import WorkspacePremiumOutlined from '@mui/icons-material/WorkspacePremiumOutlined';
import CampaignOutlined from '@mui/icons-material/CampaignOutlined';
import OpenInNewRounded from '@mui/icons-material/OpenInNewRounded';
import PermMediaOutlined from '@mui/icons-material/PermMediaOutlined';
import SettingsOutlined from '@mui/icons-material/SettingsOutlined';
import RocketLaunchOutlined from '@mui/icons-material/RocketLaunchOutlined';
import { useSelectedBusiness } from '@/stores/ownerBusiness';
import { useSnackbar } from 'notistack';
import { api, errorMessage } from '@/lib/api';
import { useLookup } from '@/lib/hooks';
import type { OwnerBusiness } from '@/lib/types';
import { PortalLayout } from '@/layouts/PortalLayout';
import { EmptyState } from '@/components/ui';
import OwnerDashboard from './OwnerDashboard';
import { OwnerBookings, OwnerLeads } from './OwnerPipeline';
import { OwnerAds, OwnerProfile, OwnerReviews, OwnerServices, OwnerSubscription } from './OwnerManage';
import OwnerMedia from './OwnerMedia';
import OwnerSettings from './OwnerSettings';
import BusinessWizard from '@/features/onboarding/BusinessWizard';

const useSelected = useSelectedBusiness;

const BusinessContext = createContext<OwnerBusiness | null>(null);
export const useBusiness = () => useContext(BusinessContext)!;

export default function OwnerPortal() {
  const { data, isLoading } = useQuery({ queryKey: ['owner', 'businesses'], queryFn: () => api.get<OwnerBusiness[]>('/api/owner/businesses') });
  const { id, set } = useSelected();
  const business = data?.find((b) => b.id === id) ?? data?.[0];
  useEffect(() => { if (business && business.id !== id) set(business.id); }, [business, id, set]);

  const counts = useQuery({
    queryKey: ['owner', 'nav-counts', business?.id],
    queryFn: async () => {
      const [leads, bookings] = await Promise.all([
        api.get<{ statusCounts: Record<string, number> }>(`/api/owner/businesses/${business!.id}/leads`, { pageSize: 1 }),
        api.get<{ statusCounts: Record<string, number> }>(`/api/owner/businesses/${business!.id}/bookings`, { pageSize: 1 }),
      ]);
      return { leads: leads.statusCounts.New ?? 0, bookings: bookings.statusCounts.Pending ?? 0 };
    },
    enabled: !!business,
    refetchInterval: 60_000,
  });

  const nav = [
    { to: '/owner', label: 'Dashboard', icon: <SpaceDashboardOutlined fontSize="small" />, end: true },
    { to: '/owner/leads', label: 'Leads', icon: <InboxOutlined fontSize="small" />, badge: counts.data?.leads },
    { to: '/owner/bookings', label: 'Bookings', icon: <EventNoteOutlined fontSize="small" />, badge: counts.data?.bookings },
    { to: '/owner/reviews', label: 'Reviews', icon: <RateReviewOutlined fontSize="small" /> },
    { to: '/owner/services', label: 'Services', icon: <HandymanOutlined fontSize="small" /> },
    { to: '/owner/profile', label: 'Business profile', icon: <StorefrontOutlined fontSize="small" /> },
    { to: '/owner/media', label: 'Photos & videos', icon: <PermMediaOutlined fontSize="small" /> },
    { to: '/owner/plan', label: 'Plan & billing', icon: <WorkspacePremiumOutlined fontSize="small" /> },
    { to: '/owner/advertising', label: 'Advertising', icon: <CampaignOutlined fontSize="small" /> },
    { to: '/owner/settings', label: 'Settings', icon: <SettingsOutlined fontSize="small" /> },
  ];

  const header = business ? (
    <div className="flex min-w-0 items-center gap-3">
      {data && data.length > 1 ? (
        <Select size="small" value={business.id} onChange={(e) => set(e.target.value)} sx={{ maxWidth: 260 }} inputProps={{ 'aria-label': 'Select business' }}>
          {data.map((b) => <MenuItem key={b.id} value={b.id}>{b.name}</MenuItem>)}
        </Select>
      ) : <span className="truncate font-semibold">{business.name}</span>}
      <AvailabilitySwitch business={business} />
    </div>
  ) : null;

  return (
    <BusinessContext.Provider value={business ?? null}>
      <Routes>
        <Route element={<PortalLayout title="Business portal" nav={nav} headerExtra={header}
          footer={business && <Button fullWidth size="small" variant="outlined" component={Link} to={`/business/${business.slug}`} target="_blank" endIcon={<OpenInNewRounded fontSize="small" />}
            sx={{ color: 'var(--cb-on-navy)', borderColor: 'rgba(255,255,255,.2)', bgcolor: 'transparent', boxShadow: 'none', '&:hover': { bgcolor: 'rgba(255,255,255,.06)', borderColor: 'rgba(255,255,255,.35)' } }}>View public profile</Button>} />}>
          {isLoading ? <Route path="*" element={<Skeleton variant="rounded" height={400} />} /> : !business ? (
            <>
              <Route path="setup" element={<BusinessWizard mode="setup" />} />
              <Route path="settings" element={<OwnerSettings />} />
              <Route path="*" element={
                <EmptyState icon={<RocketLaunchOutlined />} title="Let's set up your business"
                  message="Add your business details, services, photos and videos. It takes about 5 minutes, and customers can find you once our team approves your listing."
                  action={<Button variant="contained" color="secondary" size="large" component={Link} to="/owner/setup">Set up your business</Button>} />
              } />
            </>
          ) : (
            <>
              <Route index element={<OwnerDashboard />} />
              <Route path="leads" element={<OwnerLeads />} />
              <Route path="bookings" element={<OwnerBookings />} />
              <Route path="reviews" element={<OwnerReviews />} />
              <Route path="services" element={<OwnerServices />} />
              <Route path="profile" element={<OwnerProfile />} />
              <Route path="plan" element={<OwnerSubscription />} />
              <Route path="advertising" element={<OwnerAds />} />
              <Route path="media" element={<OwnerMedia />} />
              <Route path="settings" element={<OwnerSettings />} />
              <Route path="setup" element={<BusinessWizard mode="setup" />} />
              <Route path="*" element={<Navigate to="/owner" replace />} />
            </>
          )}
        </Route>
      </Routes>
    </BusinessContext.Provider>
  );
}

/** Real-time availability control - broadcasts to customers through PresenceHub. */
function AvailabilitySwitch({ business }: { business: OwnerBusiness }) {
  const statuses = useLookup('AvailabilityStatus');
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const mutation = useMutation({
    mutationFn: (status: string) => api.put<string>(`/api/owner/businesses/${business.id}/availability`, { status }),
    onSuccess: (res) => {
      queryClient.setQueryData<OwnerBusiness[]>(['owner', 'businesses'], (old) => old?.map((b) => (b.id === business.id ? { ...b, availabilityStatus: res.data } : b)));
      enqueueSnackbar('Customers now see your updated status', { variant: 'success' });
    },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });
  const current = statuses.find((s) => s.code === business.availabilityStatus);

  return (
    <Select size="small" value={statuses.length ? business.availabilityStatus : ''} onChange={(e) => mutation.mutate(e.target.value)} disabled={mutation.isPending}
      inputProps={{ 'aria-label': 'Your availability' }} sx={{ minWidth: 200 }}
      renderValue={() => (
        <span className="inline-flex items-center gap-2 text-sm font-semibold">
          <span className="h-2 w-2 rounded-full" style={{ backgroundColor: current?.colorHex ?? 'var(--cb-faint)' }} />{current?.name ?? business.availabilityStatus}
        </span>
      )}>
      {statuses.map((s) => (
        <MenuItem key={s.code} value={s.code}>
          <span className="inline-flex items-center gap-2"><span className="h-2 w-2 rounded-full" style={{ backgroundColor: s.colorHex ?? 'var(--cb-faint)' }} />{s.name}</span>
        </MenuItem>
      ))}
    </Select>
  );
}
