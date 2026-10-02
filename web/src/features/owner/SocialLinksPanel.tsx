import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, IconButton, MenuItem, Skeleton, TextField } from '@mui/material';
import AddRounded from '@mui/icons-material/AddRounded';
import DeleteOutlineRounded from '@mui/icons-material/DeleteOutlineRounded';
import { useSnackbar } from 'notistack';
import { ApiError, api, errorMessage } from '@/lib/api';
import { useLookup } from '@/lib/hooks';
import type { SocialLink } from '@/lib/types';
import { Panel } from '@/components/ui';

/** Social media profiles shown on the public listing. Platforms come from the SocialPlatform lookup. */
export function SocialLinksPanel({ businessId, slug }: { businessId: string; slug: string }) {
  const platforms = useLookup('SocialPlatform');
  const { enqueueSnackbar } = useSnackbar();
  const queryClient = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ['owner', 'social', businessId], queryFn: () => api.get<SocialLink[]>(`/api/owner/businesses/${businessId}/social-links`) });
  const [links, setLinks] = useState<SocialLink[]>([]);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  useEffect(() => { if (data) setLinks(data); }, [data]);
  const dirty = JSON.stringify(links) !== JSON.stringify(data ?? []);
  const used = new Set(links.map((l) => l.platform));
  const nextPlatform = platforms.find((p) => !used.has(p.code));

  const save = async () => {
    setErrors({});
    setSaving(true);
    try {
      const res = await api.put<SocialLink[]>(`/api/owner/businesses/${businessId}/social-links`, { links: links.filter((l) => l.url.trim()) });
      queryClient.setQueryData(['owner', 'social', businessId], res.data);
      void queryClient.invalidateQueries({ queryKey: ['owner', 'overview', businessId] });
      void queryClient.invalidateQueries({ queryKey: ['business', slug] });
      enqueueSnackbar('Social links saved', { variant: 'success' });
    } catch (e) {
      if (e instanceof ApiError && e.errors) setErrors(Object.fromEntries(Object.entries(e.errors).map(([k, v]) => [k, v[0]!])));
      else enqueueSnackbar(errorMessage(e), { variant: 'error' });
    } finally { setSaving(false); }
  };

  return (
    <Panel title="Social media" subtitle="Link the profiles where customers can see more of your work" className="mt-6"
      action={<Button variant="contained" size="small" disabled={!dirty || saving} onClick={() => void save()}>{saving ? 'Saving…' : 'Save links'}</Button>}>
      {isLoading ? <Skeleton height={80} /> : (
        <div className="space-y-3">
          {!links.length && <p className="text-sm text-muted">No social profiles linked yet.</p>}
          {links.map((l, i) => (
            <div key={i} className="flex items-start gap-2">
              <TextField select label="Platform" value={l.platform} sx={{ width: 170, flexShrink: 0 }}
                onChange={(e) => setLinks((x) => x.map((y, j) => (j === i ? { ...y, platform: e.target.value } : y)))}>
                {platforms.map((p) => <MenuItem key={p.code} value={p.code} disabled={p.code !== l.platform && used.has(p.code)}>{p.name}</MenuItem>)}
              </TextField>
              <TextField label="Profile URL" value={l.url} placeholder={platforms.find((p) => p.code === l.platform)?.description ?? 'https://'}
                error={!!errors[`links[${i}].url`] || !!errors[`links[${i}].platform`]} helperText={errors[`links[${i}].url`] ?? errors[`links[${i}].platform`]}
                onChange={(e) => setLinks((x) => x.map((y, j) => (j === i ? { ...y, url: e.target.value } : y)))} />
              <IconButton aria-label={`Remove ${l.platform} link`} onClick={() => setLinks((x) => x.filter((_, j) => j !== i))} sx={{ mt: 0.25 }}><DeleteOutlineRounded /></IconButton>
            </div>
          ))}
          {nextPlatform && <Button startIcon={<AddRounded />} variant="outlined" size="small" onClick={() => setLinks((x) => [...x, { platform: nextPlatform.code, url: '' }])}>Add social profile</Button>}
        </div>
      )}
    </Panel>
  );
}
