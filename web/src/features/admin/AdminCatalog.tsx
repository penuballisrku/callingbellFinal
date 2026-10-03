import { Fragment, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Collapse, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, Skeleton, Switch, Table, TableBody, TableCell, TableContainer,
  TableHead, TableRow, TextField, Tooltip,
} from '@mui/material';
import ExpandMoreRounded from '@mui/icons-material/ExpandMoreRounded';
import { useSnackbar } from 'notistack';
import { api, errorMessage } from '@/lib/api';
import { number } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import type { AdminCategory, AdminCity } from '@/lib/types';
import { ErrorState, Img, PageHeader } from '@/components/ui';

export function AdminCategories() {
  useDocumentTitle('Categories');
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const [open, setOpen] = useState<string | null>(null);
  const [editing, setEditing] = useState<AdminCategory | null>(null);
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: ['admin', 'categories'], queryFn: () => api.get<AdminCategory[]>('/api/admin/categories') });

  const onDone = (msg: string) => { enqueueSnackbar(msg, { variant: 'success' }); void queryClient.invalidateQueries({ queryKey: ['admin', 'categories'] }); void queryClient.invalidateQueries({ queryKey: ['categories'] }); };
  const saveCategory = useMutation({
    mutationFn: (c: AdminCategory) => api.put(`/api/admin/categories/${c.id}`, { name: c.name, description: c.description, isActive: c.isActive, isFeatured: c.isFeatured, sortOrder: c.sortOrder }),
    onSuccess: () => { setEditing(null); onDone('Category saved'); },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });
  const saveSub = useMutation({
    mutationFn: (s: { id: string; isActive: boolean; isFeatured: boolean }) => api.patch(`/api/admin/subcategories/${s.id}`, s),
    onSuccess: () => onDone('Sub-category saved'),
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });

  return (
    <>
      <PageHeader title="Categories" subtitle="Control which categories appear to customers and which are featured on the homepage." />
      <div className="card overflow-hidden">
        {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <div className="p-4"><Skeleton height={400} /></div> : (
          <TableContainer>
            <Table size="small">
              <TableHead><TableRow><TableCell /><TableCell>Category</TableCell><TableCell align="right">Businesses</TableCell><TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>Leads 30d</TableCell><TableCell>Active</TableCell><TableCell>Featured</TableCell><TableCell /></TableRow></TableHead>
              <TableBody>
                {data!.map((c) => (
                  <Fragment key={c.id}>
                    <TableRow hover>
                      <TableCell padding="checkbox"><IconButton size="small" onClick={() => setOpen(open === c.id ? null : c.id)} aria-label={`Show ${c.name} sub-categories`} aria-expanded={open === c.id}>
                        <ExpandMoreRounded sx={{ transform: open === c.id ? 'rotate(180deg)' : 'none', transition: 'transform .15s' }} /></IconButton></TableCell>
                      <TableCell sx={{ minWidth: 180 }}><div className="flex items-center gap-3"><Img src={c.iconUrl} alt="" className="h-9 w-9 shrink-0 p-1.5" fit="contain" fallbackText={c.name} />
                        <div className="min-w-0"><div className="font-semibold">{c.name}</div><div className="text-xs text-muted">{c.subCategories.length} sub-categories<span className="hidden sm:inline"> · /{c.slug}</span></div></div></div></TableCell>
                      <TableCell align="right">{number(c.businessCount)}</TableCell>
                      <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>{number(c.leadCount30)}</TableCell>
                      <TableCell><Switch size="small" checked={c.isActive} onChange={(e) => saveCategory.mutate({ ...c, isActive: e.target.checked })} slotProps={{ input: { 'aria-label': `${c.name} active` } }} /></TableCell>
                      <TableCell><Switch size="small" checked={c.isFeatured} onChange={(e) => saveCategory.mutate({ ...c, isFeatured: e.target.checked })} slotProps={{ input: { 'aria-label': `${c.name} featured` } }} /></TableCell>
                      <TableCell align="right"><Button size="small" onClick={() => setEditing(c)}>Edit</Button></TableCell>
                    </TableRow>
                    <TableRow>
                      <TableCell colSpan={7} sx={{ p: 0, borderBottom: open === c.id ? undefined : 'none' }}>
                        <Collapse in={open === c.id} unmountOnExit>
                          <div className="grid gap-2 bg-canvas p-4 sm:grid-cols-2 xl:grid-cols-3">
                            {c.subCategories.map((s) => (
                              <div key={s.id} className="flex items-center gap-3 rounded-lg border border-line bg-surface p-3">
                                <Img src={s.iconUrl} alt="" className="h-8 w-8 p-1" fit="contain" fallbackText={s.name} />
                                <div className="min-w-0 flex-1"><div className="truncate text-sm font-semibold">{s.name}</div><div className="text-xs text-muted">{s.businessCount} businesses</div></div>
                                <Tooltip title="Visible to customers"><Switch size="small" checked={s.isActive} onChange={(e) => saveSub.mutate({ id: s.id, isActive: e.target.checked, isFeatured: s.isFeatured })} slotProps={{ input: { 'aria-label': `${s.name} active` } }} /></Tooltip>
                                <Tooltip title="Show on homepage grid"><Switch size="small" color="secondary" checked={s.isFeatured} onChange={(e) => saveSub.mutate({ id: s.id, isActive: s.isActive, isFeatured: e.target.checked })} slotProps={{ input: { 'aria-label': `${s.name} featured` } }} /></Tooltip>
                              </div>
                            ))}
                          </div>
                        </Collapse>
                      </TableCell>
                    </TableRow>
                  </Fragment>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </div>

      <Dialog open={!!editing} onClose={() => setEditing(null)} fullWidth maxWidth="sm">
        <DialogTitle fontWeight={700}>Edit category</DialogTitle>
        {editing && (
          <DialogContent className="space-y-4" sx={{ pt: '8px !important' }}>
            <TextField label="Name" value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} error={!editing.name.trim()} helperText={!editing.name.trim() ? 'Name is required' : ''} />
            <TextField label="Description" multiline minRows={3} value={editing.description ?? ''} onChange={(e) => setEditing({ ...editing, description: e.target.value })} />
            <TextField label="Display order" type="number" value={editing.sortOrder} onChange={(e) => setEditing({ ...editing, sortOrder: Number(e.target.value) })} />
            <div className="flex gap-6">
              <FormControlLabel control={<Switch checked={editing.isActive} onChange={(e) => setEditing({ ...editing, isActive: e.target.checked })} />} label="Active" />
              <FormControlLabel control={<Switch checked={editing.isFeatured} onChange={(e) => setEditing({ ...editing, isFeatured: e.target.checked })} />} label="Featured" />
            </div>
          </DialogContent>
        )}
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={() => setEditing(null)} color="inherit">Cancel</Button>
          <Button variant="contained" disabled={!editing?.name.trim() || saveCategory.isPending} onClick={() => editing && saveCategory.mutate(editing)}>Save</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

export function AdminCities() {
  useDocumentTitle('Cities');
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: ['admin', 'cities'], queryFn: () => api.get<AdminCity[]>('/api/admin/cities') });
  const save = useMutation({
    mutationFn: (c: AdminCity) => api.patch(`/api/admin/cities/${c.id}`, { isActive: c.isActive, isPopular: c.isPopular }),
    onSuccess: () => { enqueueSnackbar('City saved', { variant: 'success' }); void queryClient.invalidateQueries({ queryKey: ['admin', 'cities'] }); void queryClient.invalidateQueries({ queryKey: ['cities'] }); },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });

  return (
    <>
      <PageHeader title="Cities" subtitle="Launch new cities and choose which appear as popular destinations." />
      {isError ? <ErrorState onRetry={() => refetch()} /> : (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {isLoading ? Array.from({ length: 6 }, (_, i) => <Skeleton key={i} variant="rounded" height={230} />) : data!.map((c) => (
            <article key={c.id} className={`card overflow-hidden ${c.isActive ? '' : 'opacity-75'}`}>
              <div className="relative">
                <Img src={c.imageUrl} alt={`${c.name} skyline`} aspect="16/6" rounded="rounded-none" className="w-full" fallbackText={c.name} />
                <div className="absolute bottom-3 left-4 text-white"><div className="text-lg font-bold">{c.name}</div><div className="text-xs text-on-navy-muted">{c.state} · {c.areaCount} areas</div></div>
                {!c.isActive && <span className="absolute right-3 top-3 rounded-md bg-surface px-2 py-0.5 text-xs font-semibold">Launching soon</span>}
              </div>
              <div className="grid grid-cols-3 divide-x divide-line border-b border-line text-center">
                {[['Businesses', c.businessCount], ['Customers', c.customerCount], ['Leads 30d', c.leads30]].map(([l, v]) => (
                  <div key={l as string} className="py-3"><div className="text-lg font-bold">{number(v as number)}</div><div className="text-xs text-muted">{l}</div></div>
                ))}
              </div>
              <div className="flex justify-between px-4 py-2">
                <FormControlLabel control={<Switch size="small" checked={c.isActive} onChange={(e) => save.mutate({ ...c, isActive: e.target.checked })} />} label={<span className="text-sm">Live</span>} />
                <FormControlLabel control={<Switch size="small" color="secondary" checked={c.isPopular} onChange={(e) => save.mutate({ ...c, isPopular: e.target.checked })} />} label={<span className="text-sm">Popular</span>} />
              </div>
            </article>
          ))}
        </div>
      )}
    </>
  );
}
