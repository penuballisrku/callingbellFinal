import { api } from './api';
import type { MediaKind, OwnerMediaItem } from './types';

/** Client-side limits for files as picked (images are compressed before upload; the server re-checks everything). */
export const MEDIA_LIMITS = {
  imageTypes: ['image/jpeg', 'image/png', 'image/webp'],
  videoTypes: ['video/mp4', 'video/webm', 'video/quicktime'],
  maxImageSourceMb: 20,
  maxVideoMb: 50,
  maxVideos: 5,
};
export const IMAGE_ACCEPT = MEDIA_LIMITS.imageTypes.join(',');
export const VIDEO_ACCEPT = MEDIA_LIMITS.videoTypes.join(',');

/** A file picked in the browser, waiting to be (or being) uploaded. */
export interface PendingMedia {
  id: string;
  kind: MediaKind;
  file: File;
  previewUrl: string;
  title?: string;
  poster?: Blob | null;
  durationSeconds?: number | null;
  status: 'ready' | 'uploading' | 'done' | 'failed';
  progress: number;
  error?: string;
}

/** Validates a picked file; returns an error message or null. */
export function checkFile(file: File, kind: MediaKind): string | null {
  if (kind === 'video') {
    if (!MEDIA_LIMITS.videoTypes.includes(file.type)) return `${file.name}: upload an MP4, WebM or MOV video.`;
    if (file.size > MEDIA_LIMITS.maxVideoMb * 1024 * 1024) return `${file.name} is larger than ${MEDIA_LIMITS.maxVideoMb} MB.`;
    return null;
  }
  if (!MEDIA_LIMITS.imageTypes.includes(file.type)) return `${file.name}: upload a JPG, PNG or WebP image.`;
  if (file.size > MEDIA_LIMITS.maxImageSourceMb * 1024 * 1024) return `${file.name} is larger than ${MEDIA_LIMITS.maxImageSourceMb} MB.`;
  return null;
}

export async function toPending(file: File, kind: MediaKind): Promise<PendingMedia> {
  const base: PendingMedia = {
    id: crypto.randomUUID(), kind, file, previewUrl: URL.createObjectURL(file), status: 'ready', progress: 0,
    title: kind === 'video' ? file.name.replace(/\.[^.]+$/, '').replace(/[-_]+/g, ' ').trim() : undefined,
  };
  if (kind !== 'video') return base;
  const { poster, durationSeconds } = await readVideo(file);
  return { ...base, poster, durationSeconds, previewUrl: poster ? URL.createObjectURL(poster) : base.previewUrl };
}

export function releasePending(item: PendingMedia) {
  URL.revokeObjectURL(item.previewUrl);
}

/* ---------- Image processing ---------- */

const canvasBlob = (canvas: HTMLCanvasElement, type: string, quality?: number) =>
  new Promise<Blob>((resolve, reject) => canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('Could not process image'))), type, quality));

async function render(source: CanvasImageSource, width: number, height: number, maxSide: number, type: string, quality: number) {
  const scale = Math.min(1, maxSide / Math.max(width, height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(width * scale));
  canvas.height = Math.max(1, Math.round(height * scale));
  const ctx = canvas.getContext('2d')!;
  if (type === 'image/jpeg') { ctx.fillStyle = '#ffffff'; ctx.fillRect(0, 0, canvas.width, canvas.height); }
  ctx.imageSmoothingQuality = 'high';
  ctx.drawImage(source, 0, 0, canvas.width, canvas.height);
  return canvasBlob(canvas, type, quality);
}

/**
 * Resizes an image for the web and makes a small rendition for thumbnails and mobile.
 * Logos keep transparency (PNG); everything else becomes a compressed JPEG.
 */
export async function processImage(file: File, kind: MediaKind): Promise<{ file: Blob; thumbnail: Blob; name: string }> {
  const bitmap = await createImageBitmap(file);
  try {
    const isLogo = kind === 'logo';
    const type = isLogo ? 'image/png' : 'image/jpeg';
    const maxSide = isLogo ? 600 : kind === 'cover' ? 2000 : 1600;
    const main = await render(bitmap, bitmap.width, bitmap.height, maxSide, type, 0.86);
    const thumbnail = await render(bitmap, bitmap.width, bitmap.height, isLogo ? 160 : 640, type, 0.8);
    const name = file.name.replace(/\.[^.]+$/, '') + (isLogo ? '.png' : '.jpg');
    return { file: main, thumbnail, name };
  } finally {
    bitmap.close();
  }
}

/** Reads a video's duration and grabs a frame for the poster. Resolves with nulls if the browser can't decode it (e.g. some MOV files). */
export function readVideo(file: File): Promise<{ poster: Blob | null; durationSeconds: number | null }> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(file);
    const video = document.createElement('video');
    let settled = false;
    const done = (poster: Blob | null, duration: number | null) => {
      if (settled) return;
      settled = true;
      URL.revokeObjectURL(url);
      resolve({ poster, durationSeconds: duration && Number.isFinite(duration) ? Math.max(1, Math.round(duration)) : null });
    };
    const timer = setTimeout(() => done(null, Number.isFinite(video.duration) ? video.duration : null), 8000);
    video.muted = true;
    video.playsInline = true;
    video.preload = 'metadata';
    video.onloadedmetadata = () => { video.currentTime = Math.min(1, (video.duration || 2) / 3); };
    video.onseeked = async () => {
      try {
        const poster = await render(video, video.videoWidth, video.videoHeight, 1280, 'image/jpeg', 0.82);
        clearTimeout(timer);
        done(poster, video.duration);
      } catch {
        clearTimeout(timer);
        done(null, video.duration);
      }
    };
    video.onerror = () => { clearTimeout(timer); done(null, null); };
    video.src = url;
  });
}

/* ---------- Upload ---------- */

/** Processes (images) and uploads one file to the business's media library. */
export async function uploadMedia(businessId: string, item: PendingMedia, onProgress: (fraction: number) => void, signal?: AbortSignal): Promise<OwnerMediaItem> {
  const form = new FormData();
  form.append('kind', item.kind);
  if (item.kind === 'video') {
    form.append('file', item.file, item.file.name);
    if (item.poster) form.append('thumbnail', item.poster, 'poster.jpg');
    if (item.durationSeconds) form.append('durationSeconds', String(item.durationSeconds));
  } else {
    const processed = await processImage(item.file, item.kind);
    form.append('file', processed.file, processed.name);
    form.append('thumbnail', processed.thumbnail, `thumb-${processed.name}`);
  }
  if (item.title) form.append('title', item.title.slice(0, 150));
  const res = await api.upload<OwnerMediaItem>(`/api/owner/businesses/${businessId}/media`, form, onProgress, signal);
  return res.data;
}

export const fileSize = (bytes: number) =>
  bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;

export const duration = (seconds?: number | null) =>
  seconds ? `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}` : '';
