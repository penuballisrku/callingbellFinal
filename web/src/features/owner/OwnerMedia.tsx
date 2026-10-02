import { useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Dialog, DialogContent, DialogTitle, IconButton, LinearProgress, Skeleton } from '@mui/material';
import CloseRounded from '@mui/icons-material/CloseRounded';
import ImageOutlined from '@mui/icons-material/ImageOutlined';
import PanoramaOutlined from '@mui/icons-material/PanoramaOutlined';
import PhotoLibraryOutlined from '@mui/icons-material/PhotoLibraryOutlined';
import VideoLibraryOutlined from '@mui/icons-material/VideoLibraryOutlined';
import { useSnackbar } from 'notistack';
import { api, errorMessage } from '@/lib/api';
import { date } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import { IMAGE_ACCEPT, VIDEO_ACCEPT, checkFile, duration, fileSize, releasePending, toPending, uploadMedia, type PendingMedia } from '@/lib/media';
import type { MediaKind, OwnerMedia as Media, OwnerMediaItem } from '@/lib/types';
import { DropZone, MediaTile } from '@/components/media';
import { ConfirmDialog, EmptyState, ErrorState, PageHeader, Panel } from '@/components/ui';
import { useBusiness } from './OwnerPortal';

export default function OwnerMedia() {
  useDocumentTitle('Photos & videos');
  const business = useBusiness();
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();
  const key = ['owner', 'media', business.id];
  const { data, isLoading, isError, refetch } = useQuery({ queryKey: key, queryFn: () => api.get<Media>(`/api/owner/businesses/${business.id}/media`) });
  const [queue, setQueue] = useState<PendingMedia[]>([]);
  const queueRef = useRef(queue);
  queueRef.current = queue;
  useEffect(() => () => queueRef.current.forEach(releasePending), []);
  const [confirm, setConfirm] = useState<{ kind: MediaKind; item?: OwnerMediaItem } | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [playing, setPlaying] = useState<OwnerMediaItem | null>(null);

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: key });
    void queryClient.invalidateQueries({ queryKey: ['owner', 'overview', business.id] });
    void queryClient.invalidateQueries({ queryKey: ['owner', 'businesses'] });
  };

  /** Validates, then uploads files one at a time with per-file progress. */
  const upload = async (kind: MediaKind, files: File[]) => {
    if (!data) return;
    const room = kind === 'photo' ? data.limits.maxPhotos - data.photos.length - queue.filter((q) => q.kind === 'photo' && q.status !== 'failed').length
      : kind === 'video' ? data.limits.maxVideos - data.videos.length - queue.filter((q) => q.kind === 'video' && q.status !== 'failed').length : 1;
    const errors: string[] = [];
    const accepted = files.filter((f) => { const e = checkFile(f, kind); if (e) errors.push(e); return !e; }).slice(0, Math.max(0, room));
    if (files.length > accepted.length + errors.length) errors.push(`Only ${room} more ${kind === 'video' ? 'video(s)' : 'photo(s)'} can be added on your ${data.limits.planName} plan.`);
    errors.forEach((e) => enqueueSnackbar(e, { variant: 'warning' }));
    if (!accepted.length) return;

    const items = await Promise.all(accepted.map((f) => toPending(f, kind)));
    setQueue((q) => [...q, ...items]);
    let ok = 0;
    for (const item of items) {
      setQueue((q) => q.map((x) => (x.id === item.id ? { ...x, status: 'uploading' } : x)));
      try {
        await uploadMedia(business.id, item, (p) => setQueue((q) => q.map((x) => (x.id === item.id ? { ...x, progress: p } : x))));
        ok++;
        setQueue((q) => q.filter((x) => x.id !== item.id));
        releasePending(item);
      } catch (e) {
        setQueue((q) => q.map((x) => (x.id === item.id ? { ...x, status: 'failed', error: errorMessage(e) } : x)));
        enqueueSnackbar(`${item.file.name}: ${errorMessage(e)}`, { variant: 'error' });
      }
    }
    if (ok) {
      enqueueSnackbar(ok === 1 ? 'Upload complete' : `${ok} files uploaded`, { variant: 'success' });
      refresh();
    }
  };

  const remove = async () => {
    if (!confirm) return;
    setDeleting(true);
    try {
      await api.del(`/api/owner/businesses/${business.id}/media/${confirm.kind}${confirm.item ? `/${confirm.item.id}` : ''}`);
      enqueueSnackbar('Removed', { variant: 'success' });
      refresh();
      setConfirm(null);
    } catch (e) {
      enqueueSnackbar(errorMessage(e), { variant: 'error' });
    } finally {
      setDeleting(false);
    }
  };

  const makePrimary = async (photo: OwnerMediaItem) => {
    try {
      await api.put(`/api/owner/businesses/${business.id}/media/photos/${photo.id}/primary`);
      enqueueSnackbar('Main photo updated', { variant: 'success' });
      refresh();
    } catch (e) { enqueueSnackbar(errorMessage(e), { variant: 'error' }); }
  };

  if (isError) return <ErrorState onRetry={() => refetch()} />;
  if (isLoading || !data) return <><PageHeader title="Photos & videos" /><Skeleton variant="rounded" height={560} /></>;

  const pending = (kind: MediaKind) => queue.filter((q) => q.kind === kind);
  const photoCount = data.photos.length;
  const unlimited = data.limits.maxPhotos >= 999;

  return (
    <>
      <PageHeader title="Photos & videos" crumbs={[{ label: 'Dashboard', to: '/business' }, { label: 'Photos & videos' }]}
        subtitle="Great photos and a short video help customers trust you before they call." />

      <div className="grid gap-6 xl:grid-cols-[1fr_340px]">
        <div className="min-w-0 space-y-6">
          <Panel title="Logo & cover image" subtitle="Shown on search results and the top of your profile">
            <div className="grid gap-5 sm:grid-cols-[180px_1fr]">
              <BrandSlot label="Logo" url={data.logoUrl} pending={pending('logo')[0]} aspect="1/1" fit="contain"
                onFiles={(f) => void upload('logo', f)} onRemove={() => setConfirm({ kind: 'logo' })} icon={<ImageOutlined />} hint="Square, PNG with transparency works best" />
              <BrandSlot label="Cover image" url={data.coverImageUrl} pending={pending('cover')[0]} aspect="16/6"
                onFiles={(f) => void upload('cover', f)} onRemove={() => setConfirm({ kind: 'cover' })} icon={<PanoramaOutlined />} hint="Wide photo, at least 1600 × 600 px" />
            </div>
          </Panel>

          <Panel title={`Photos · ${photoCount}${unlimited ? '' : ` of ${data.limits.maxPhotos}`}`} subtitle="Hover a photo to make it your main photo or remove it">
            {photoCount + pending('photo').length > 0 && (
              <div className="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
                {data.photos.map((p) => (
                  <MediaTile key={p.id} src={p.thumbnailUrl ?? p.url} kind="photo" title={p.title} primary={p.isPrimary}
                    onMakePrimary={() => void makePrimary(p)} onRemove={() => setConfirm({ kind: 'photo', item: p })} removeLabel="Delete photo" />
                ))}
                {pending('photo').map((q) => (
                  <MediaTile key={q.id} src={q.previewUrl} kind="photo" status={q.status} progress={q.progress} error={q.error} title={q.file.name}
                    onRemove={q.status === 'failed' ? () => { releasePending(q); setQueue((x) => x.filter((i) => i.id !== q.id)); } : undefined} />
                ))}
              </div>
            )}
            <DropZone accept={IMAGE_ACCEPT} multiple disabled={!unlimited && photoCount >= data.limits.maxPhotos} onFiles={(f) => void upload('photo', f)}
              icon={<PhotoLibraryOutlined />}
              title={!unlimited && photoCount >= data.limits.maxPhotos ? `You've reached the ${data.limits.planName} plan limit` : 'Drag photos here or click to upload'}
              hint={`JPG, PNG or WebP · optimised automatically · ${unlimited ? 'unlimited photos' : `up to ${data.limits.maxPhotos} photos`} on ${data.limits.planName}`} />
          </Panel>

          <Panel title={`Videos · ${data.videos.length} of ${data.limits.maxVideos}`} subtitle="Introduce your team, show your work or walk customers through your premises" noPad>
            {data.videos.length + pending('video').length === 0 ? (
              <EmptyState icon={<VideoLibraryOutlined />} title="No videos yet" message="Businesses with a video get more enquiries. A 30–60 second clip shot on a phone is perfect." />
            ) : (
              <ul className="divide-y divide-line">
                {data.videos.map((v) => (
                  <li key={v.id} className="flex items-center gap-4 px-5 py-3">
                    <button type="button" className="w-36 shrink-0 rounded-lg focus-visible:outline-2" onClick={() => setPlaying(v)} aria-label={`Play ${v.title}`}>
                      <MediaTile src={v.thumbnailUrl} kind="video" aspect="16/9" durationSeconds={v.durationSeconds} title={v.title} />
                    </button>
                    <div className="min-w-0 flex-1">
                      <div className="truncate font-semibold">{v.title}</div>
                      <div className="text-xs text-muted">{[v.fileSize ? fileSize(v.fileSize) : null, v.durationSeconds ? duration(v.durationSeconds) : null, `Added ${date(v.createdOn)}`].filter(Boolean).join(' · ')}</div>
                    </div>
                    <Button size="small" onClick={() => setPlaying(v)}>Play</Button>
                    <Button size="small" color="error" onClick={() => setConfirm({ kind: 'video', item: v })}>Delete</Button>
                  </li>
                ))}
                {pending('video').map((q) => (
                  <li key={q.id} className="flex items-center gap-4 px-5 py-3">
                    <div className="w-36 shrink-0"><MediaTile src={q.poster ? q.previewUrl : null} kind="video" aspect="16/9" status={q.status} progress={q.progress} error={q.error} /></div>
                    <div className="min-w-0 flex-1">
                      <div className="truncate font-semibold">{q.title || q.file.name}</div>
                      <div className="text-xs text-muted">{q.status === 'failed' ? q.error : `Uploading · ${Math.round(q.progress * 100)}% of ${fileSize(q.file.size)}`}</div>
                      {q.status === 'uploading' && <LinearProgress variant="determinate" value={q.progress * 100} sx={{ mt: 1, height: 4, borderRadius: 2 }} />}
                    </div>
                  </li>
                ))}
              </ul>
            )}
            <div className="border-t border-line p-5">
              <DropZone accept={VIDEO_ACCEPT} multiple disabled={data.videos.length >= data.limits.maxVideos} onFiles={(f) => void upload('video', f)} compact
                icon={<VideoLibraryOutlined />} title={data.videos.length >= data.limits.maxVideos ? 'Maximum videos reached' : 'Drag videos here or click to upload'}
                hint={`MP4, WebM or MOV · up to ${data.limits.maxVideoMb} MB each`} />
            </div>
          </Panel>
        </div>

        <aside className="space-y-4">
          <Panel title="Tips for great photos">
            <ul className="space-y-2.5 text-sm text-ink-2">
              <li>• Shoot in daylight or a well-lit space; avoid filters.</li>
              <li>• Show finished work, your premises and your team at work.</li>
              <li>• Use landscape photos for the cover image.</li>
              <li>• Keep videos short: 30 to 90 seconds is ideal.</li>
              <li>• Only upload photos you own or have permission to use.</li>
            </ul>
          </Panel>
          <div className="rounded-xl border border-line bg-subtle p-4 text-sm">
            <div className="font-semibold">{data.limits.planName} plan</div>
            <div className="mt-1 text-muted">{unlimited ? 'Unlimited photos' : `${data.limits.maxPhotos} photos`} · {data.limits.maxVideos} videos · images up to {data.limits.maxImageMb} MB after optimisation</div>
          </div>
        </aside>
      </div>

      <ConfirmDialog open={!!confirm} destructive loading={deleting} confirmLabel="Delete"
        title={confirm?.kind === 'video' ? 'Delete this video?' : confirm?.kind === 'photo' ? 'Delete this photo?' : `Remove your ${confirm?.kind}?`}
        message="It will be removed from your public profile straight away." onConfirm={() => void remove()} onClose={() => setConfirm(null)} />

      <Dialog open={!!playing} onClose={() => setPlaying(null)} maxWidth="md" fullWidth>
        <DialogTitle sx={{ pr: 6 }}>{playing?.title}
          <IconButton onClick={() => setPlaying(null)} aria-label="Close" sx={{ position: 'absolute', right: 12, top: 12 }}><CloseRounded /></IconButton>
        </DialogTitle>
        <DialogContent>
          {playing && <video src={playing.url} poster={playing.thumbnailUrl ?? undefined} controls autoPlay playsInline className="aspect-video w-full rounded-lg bg-black" />}
        </DialogContent>
      </Dialog>
    </>
  );
}

function BrandSlot({ label, url, pending, aspect, fit, onFiles, onRemove, icon, hint }: {
  label: string; url?: string | null; pending?: PendingMedia; aspect: string; fit?: 'cover' | 'contain'; onFiles: (f: File[]) => void; onRemove: () => void;
  icon: React.ReactNode; hint: string;
}) {
  return (
    <div>
      <div className="mb-1.5 text-sm font-medium">{label}</div>
      {pending ? (
        <MediaTile src={pending.previewUrl} kind="cover" aspect={aspect} fit={fit} status={pending.status} progress={pending.progress} error={pending.error} />
      ) : url ? (
        <>
          <MediaTile src={url} kind="cover" aspect={aspect} fit={fit} title={label} onRemove={onRemove} removeLabel={`Remove ${label.toLowerCase()}`} />
          <div className="mt-2">
            <DropZone accept={IMAGE_ACCEPT} onFiles={onFiles} title={`Replace ${label.toLowerCase()}`} compact icon={icon} />
          </div>
        </>
      ) : (
        <DropZone accept={IMAGE_ACCEPT} onFiles={onFiles} title={`Add ${label.toLowerCase()}`} hint={hint} icon={icon} compact />
      )}
    </div>
  );
}
