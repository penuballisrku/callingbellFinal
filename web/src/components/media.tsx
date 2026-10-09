import { useRef, useState, type ReactNode } from 'react';
import { IconButton, LinearProgress, Tooltip } from '@mui/material';
import CloudUploadOutlined from '@mui/icons-material/CloudUploadOutlined';
import CloseRounded from '@mui/icons-material/CloseRounded';
import PlayArrowRounded from '@mui/icons-material/PlayArrowRounded';
import CheckCircleRounded from '@mui/icons-material/CheckCircleRounded';
import ErrorOutlineRounded from '@mui/icons-material/ErrorOutlineRounded';
import StarRounded from '@mui/icons-material/StarRounded';
import ChevronLeftRounded from '@mui/icons-material/ChevronLeftRounded';
import ChevronRightRounded from '@mui/icons-material/ChevronRightRounded';
import { duration } from '@/lib/media';

/**
 * Drag-and-drop file target. Also opens the file picker on click, Enter or Space, so it works without a mouse.
 */
export function DropZone({ accept, multiple, disabled, onFiles, title, hint, icon, compact, children }: {
  accept: string; multiple?: boolean; disabled?: boolean; onFiles: (files: File[]) => void; title: string; hint?: string; icon?: ReactNode;
  compact?: boolean; children?: ReactNode;
}) {
  const input = useRef<HTMLInputElement>(null);
  const [over, setOver] = useState(false);
  const pick = () => { if (!disabled) input.current?.click(); };
  const take = (list: FileList | null) => {
    if (!list || disabled) return;
    const files = Array.from(list);
    onFiles(multiple ? files : files.slice(0, 1));
  };

  return (
    <div
      role="button" tabIndex={disabled ? -1 : 0} aria-disabled={disabled} aria-label={`${title}. ${hint ?? ''}`}
      onClick={pick}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); pick(); } }}
      onDragOver={(e) => { e.preventDefault(); if (!disabled) setOver(true); }}
      onDragLeave={() => setOver(false)}
      onDrop={(e) => { e.preventDefault(); setOver(false); take(e.dataTransfer.files); }}
      className={`flex cursor-pointer flex-col items-center justify-center rounded-xl border-2 border-dashed text-center transition-colors
        ${compact ? 'gap-1 px-4 py-5' : 'gap-2 px-6 py-8'}
        ${disabled ? 'cursor-not-allowed border-line bg-subtle opacity-60' : over ? 'border-accent bg-accent-soft' : 'border-line-strong bg-surface hover:border-accent hover:bg-accent-soft/40'}`}>
      <span className={`flex items-center justify-center rounded-full ${over ? 'bg-accent text-on-accent' : 'bg-subtle text-muted'} ${compact ? 'h-9 w-9' : 'h-11 w-11'}`}>
        {icon ?? <CloudUploadOutlined />}
      </span>
      <span className="text-sm font-semibold text-ink">{over ? 'Drop to add' : title}</span>
      {hint && <span className="text-xs text-muted">{hint}</span>}
      {children}
      <input ref={input} type="file" hidden accept={accept} multiple={multiple} onChange={(e) => { take(e.target.files); e.target.value = ''; }} />
    </div>
  );
}

/** Thumbnail tile for a photo or video, with optional upload progress, status and remove action. */
export function MediaTile({ src, kind, title, durationSeconds, progress, status, error, onRemove, removeLabel, primary, onMakePrimary, onMoveEarlier, onMoveLater, aspect = '4/3', fit = 'cover' }: {
  src?: string | null; kind: 'photo' | 'video' | 'logo' | 'cover'; title?: string | null; durationSeconds?: number | null;
  progress?: number; status?: 'ready' | 'uploading' | 'done' | 'failed'; error?: string; onRemove?: () => void; removeLabel?: string;
  primary?: boolean; onMakePrimary?: () => void; aspect?: string; fit?: 'cover' | 'contain';
  /** Reordering (photos): shown as arrows at the bottom of the tile. */
  onMoveEarlier?: () => void; onMoveLater?: () => void;
}) {
  const [broken, setBroken] = useState(false);
  return (
    <figure className="group relative overflow-hidden rounded-lg border border-line bg-subtle" style={{ aspectRatio: aspect }}>
      {src && !broken ? (
        <img src={src} alt={title ?? ''} onError={() => setBroken(true)} className={`h-full w-full ${fit === 'contain' ? 'object-contain p-2' : 'object-cover'}`} />
      ) : (
        <div className="flex h-full w-full items-center justify-center text-xs text-muted">{kind === 'video' ? 'Video' : 'Image'}</div>
      )}
      {kind === 'video' && (
        <span className="pointer-events-none absolute inset-0 flex items-center justify-center">
          <span className="flex h-9 w-9 items-center justify-center rounded-full bg-black/55 text-white"><PlayArrowRounded /></span>
        </span>
      )}
      {kind === 'video' && durationSeconds ? (
        <span className="absolute bottom-1.5 right-1.5 rounded bg-black/65 px-1.5 py-0.5 text-[11px] font-semibold text-white tabular">{duration(durationSeconds)}</span>
      ) : null}
      {primary && <span className="absolute left-1.5 top-1.5 inline-flex items-center gap-0.5 rounded bg-black/65 px-1.5 py-0.5 text-[11px] font-semibold text-white"><StarRounded sx={{ fontSize: 12 }} />Main</span>}

      {status === 'uploading' && (
        <div className="absolute inset-x-0 bottom-0 bg-black/55 p-1.5">
          <LinearProgress variant="determinate" value={Math.round((progress ?? 0) * 100)} sx={{ height: 4, bgcolor: 'rgba(255,255,255,.25)', '& .MuiLinearProgress-bar': { bgcolor: '#F4A62C' } }} />
        </div>
      )}
      {status === 'done' && <span className="absolute bottom-1.5 left-1.5 text-success"><CheckCircleRounded sx={{ fontSize: 20, bgcolor: '#fff', borderRadius: '50%' }} /></span>}
      {status === 'failed' && (
        <Tooltip title={error ?? 'Upload failed'}>
          <span className="absolute inset-x-0 bottom-0 flex items-center gap-1 bg-danger px-2 py-1 text-[11px] font-semibold text-white"><ErrorOutlineRounded sx={{ fontSize: 14 }} />Failed</span>
        </Tooltip>
      )}

      {(onMoveEarlier || onMoveLater) && !status && (
        <div className="absolute bottom-1 right-1 flex gap-1 opacity-100 transition-opacity sm:opacity-0 sm:group-focus-within:opacity-100 sm:group-hover:opacity-100">
          {onMoveEarlier && (
            <Tooltip title="Move earlier">
              <IconButton size="small" onClick={onMoveEarlier} aria-label={`Move ${title ?? 'photo'} earlier`} sx={{ bgcolor: 'rgba(0,0,0,.6)', color: '#fff', '&:hover': { bgcolor: 'rgba(0,0,0,.8)' } }}>
                <ChevronLeftRounded sx={{ fontSize: 16 }} />
              </IconButton>
            </Tooltip>
          )}
          {onMoveLater && (
            <Tooltip title="Move later">
              <IconButton size="small" onClick={onMoveLater} aria-label={`Move ${title ?? 'photo'} later`} sx={{ bgcolor: 'rgba(0,0,0,.6)', color: '#fff', '&:hover': { bgcolor: 'rgba(0,0,0,.8)' } }}>
                <ChevronRightRounded sx={{ fontSize: 16 }} />
              </IconButton>
            </Tooltip>
          )}
        </div>
      )}
      {(onRemove || onMakePrimary) && status !== 'uploading' && (
        <div className="absolute right-1 top-1 flex gap-1 opacity-100 transition-opacity sm:opacity-0 sm:group-focus-within:opacity-100 sm:group-hover:opacity-100">
          {onMakePrimary && !primary && (
            <Tooltip title="Make main photo">
              <IconButton size="small" onClick={onMakePrimary} aria-label={`Make ${title ?? 'photo'} the main photo`} sx={{ bgcolor: 'rgba(0,0,0,.6)', color: '#fff', '&:hover': { bgcolor: 'rgba(0,0,0,.8)' } }}>
                <StarRounded sx={{ fontSize: 16 }} />
              </IconButton>
            </Tooltip>
          )}
          {onRemove && (
            <Tooltip title={removeLabel ?? 'Remove'}>
              <IconButton size="small" onClick={onRemove} aria-label={`${removeLabel ?? 'Remove'} ${title ?? ''}`} sx={{ bgcolor: 'rgba(0,0,0,.6)', color: '#fff', '&:hover': { bgcolor: 'rgba(0,0,0,.8)' } }}>
                <CloseRounded sx={{ fontSize: 16 }} />
              </IconButton>
            </Tooltip>
          )}
        </div>
      )}
      {title && kind !== 'logo' && kind !== 'cover' && (
        <figcaption className="sr-only">{title}</figcaption>
      )}
    </figure>
  );
}
