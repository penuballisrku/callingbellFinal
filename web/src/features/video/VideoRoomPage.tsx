import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import type { HubConnection } from '@microsoft/signalr';
import { Alert, Button, CircularProgress, IconButton, Tooltip } from '@mui/material';
import MicRounded from '@mui/icons-material/MicRounded';
import MicOffRounded from '@mui/icons-material/MicOffRounded';
import VideocamRounded from '@mui/icons-material/VideocamRounded';
import VideocamOffRounded from '@mui/icons-material/VideocamOffRounded';
import ScreenShareRounded from '@mui/icons-material/ScreenShareRounded';
import StopScreenShareRounded from '@mui/icons-material/StopScreenShareRounded';
import CallEndRounded from '@mui/icons-material/CallEndRounded';
import ScheduleRounded from '@mui/icons-material/ScheduleRounded';
import { api, ApiError, errorMessage } from '@/lib/api';
import { dateTime } from '@/lib/format';
import { useDocumentTitle } from '@/lib/hooks';
import { useAuth } from '@/stores/auth';
import type { VideoRoom } from '@/lib/types';
import { Img } from '@/components/ui';

type Phase = 'lobby' | 'connecting' | 'waiting' | 'in-call' | 'left' | 'ended';
type Signal = { type: 'offer' | 'answer'; sdp: string } | { type: 'candidate'; candidate: RTCIceCandidateInit };

/**
 * A private video call (video consultations). Audio and video go directly between the two browsers (WebRTC); the server only relays
 * the connection set-up over the VideoHub, between the two people allowed in the room. The person who joins second starts the
 * connection; if either leaves or reloads, the other waits and the call resumes when they're back.
 */
export default function VideoRoomPage() {
  const { roomId = '' } = useParams();
  const user = useAuth((s) => s.user);
  const room = useQuery({
    queryKey: ['video', 'room', roomId],
    queryFn: () => api.get<VideoRoom>(`/api/video/rooms/${roomId}`),
    enabled: !!user && !!roomId,
    retry: false,
    // Before the room opens, check again every 30 seconds.
    refetchInterval: (q) => (q.state.data && !q.state.data.canJoinNow && q.state.data.status !== 'Ended' ? 30_000 : false),
  });
  useDocumentTitle('Video consultation');

  if (!user) {
    return <Shell><Centered title="Sign in to join the call" action={<Button variant="contained" component={Link} to={`/login?returnUrl=${encodeURIComponent(`/video/${roomId}`)}`}>Sign in</Button>} /></Shell>;
  }
  if (room.isLoading) return <Shell><div className="grid h-full place-items-center"><CircularProgress /></div></Shell>;
  if (room.isError) {
    const status = room.error instanceof ApiError ? room.error.status : 0;
    return <Shell><Centered title={status === 403 ? 'This call isn’t yours' : status === 404 ? 'Call not found' : 'Couldn’t load the call'}
      message={status === 403 ? 'Only the customer and the business can join this video call.' : status === 404 ? 'The link may be wrong or the call was removed.' : errorMessage(room.error)}
      action={<Button component={Link} to="/" variant="outlined">Go home</Button>} /></Shell>;
  }
  const r = room.data!;
  if (r.status === 'Ended') return <Shell><Centered title="This call has ended" message="Thanks for using Calling Bell video consultations." action={<BackButton room={r} />} /></Shell>;
  if (!r.canJoinNow) {
    return (
      <Shell>
        <Centered icon={<ScheduleRounded sx={{ fontSize: 40 }} />} title="Your call hasn’t opened yet"
          message={r.opensAt ? `You can join from ${dateTime(r.opensAt)}. This page updates by itself.` : 'Please check back shortly.'}
          action={<BackButton room={r} />} />
      </Shell>
    );
  }
  return <Call room={r} />;
}

function Call({ room }: { room: VideoRoom }) {
  const navigate = useNavigate();
  const [phase, setPhase] = useState<Phase>('lobby');
  const [error, setError] = useState<string | null>(null);
  const [mediaError, setMediaError] = useState<string | null>(null);
  const [micOn, setMicOn] = useState(true);
  const [camOn, setCamOn] = useState(true);
  const [sharing, setSharing] = useState(false);
  const [remoteVideo, setRemoteVideo] = useState(false);
  const [startedAt, setStartedAt] = useState<number | null>(null);
  const [now, setNow] = useState(Date.now());

  const localRef = useRef<HTMLVideoElement>(null);
  const remoteRef = useRef<HTMLVideoElement>(null);
  const stream = useRef<MediaStream | null>(null);
  const screen = useRef<MediaStream | null>(null);
  const pc = useRef<RTCPeerConnection | null>(null);
  const peerId = useRef<string | null>(null);
  const hub = useRef<HubConnection | null>(null);
  const pendingCandidates = useRef<RTCIceCandidateInit[]>([]);
  const other = room.myRole === 'Customer' ? room.businessName : room.customerName;

  useEffect(() => { const t = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(t); }, []);

  // Camera and microphone for the preview (and the call).
  const startMedia = useCallback(async () => {
    setMediaError(null);
    // Browsers only give pages on https:// or localhost the camera (on http://192.168.x.x there is no camera API at all).
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
      setMediaError(`Your browser only allows the camera and microphone on a secure address (https:// or localhost), and this page is on ${window.location.host}. Open the link over https:// or localhost to be seen and heard.`);
      return;
    }
    try {
      const s = await navigator.mediaDevices.getUserMedia({ video: { width: { ideal: 1280 }, height: { ideal: 720 } }, audio: { echoCancellation: true, noiseSuppression: true } });
      stream.current = s;
      if (localRef.current) localRef.current.srcObject = s;
    } catch (e) {
      const name = (e as DOMException).name;
      // Without a camera, a voice-only consultation still works.
      if (name === 'NotFoundError' || name === 'OverconstrainedError') {
        try {
          const s = await navigator.mediaDevices.getUserMedia({ audio: true });
          stream.current = s;
          setCamOn(false);
          setMediaError('No camera found: you’ll join with audio only.');
          return;
        } catch { /* fall through */ }
      }
      setMediaError(name === 'NotAllowedError'
        ? 'Camera and microphone are blocked. Allow them for this site in your browser (the camera icon in the address bar), then try again.'
        : name === 'NotReadableError' ? 'Your camera or microphone is being used by another app. Close it and try again.'
          : 'We couldn’t start your camera or microphone.');
    }
  }, []);
  useEffect(() => { void startMedia(); }, [startMedia]);

  const send = (to: string, s: Signal) => void hub.current?.invoke('Signal', room.id, to, JSON.stringify(s)).catch(() => undefined);

  const closePeer = () => {
    pc.current?.close();
    pc.current = null;
    peerId.current = null;
    pendingCandidates.current = [];
    setRemoteVideo(false);
    if (remoteRef.current) remoteRef.current.srcObject = null;
  };

  const createPeer = (to: string) => {
    closePeer();
    peerId.current = to;
    const conn = new RTCPeerConnection({ iceServers: room.iceServers.map((s) => ({ urls: s.urls, username: s.username ?? undefined, credential: s.credential ?? undefined })) });
    stream.current?.getTracks().forEach((t) => conn.addTrack(t, stream.current!));
    // Joined without a camera or microphone: still receive the other side's audio and video.
    if (!stream.current?.getAudioTracks().length) conn.addTransceiver('audio', { direction: 'recvonly' });
    if (!stream.current?.getVideoTracks().length) conn.addTransceiver('video', { direction: 'recvonly' });
    conn.onicecandidate = (e) => { if (e.candidate) send(to, { type: 'candidate', candidate: e.candidate.toJSON() }); };
    conn.ontrack = (e) => {
      if (remoteRef.current && remoteRef.current.srcObject !== e.streams[0]) remoteRef.current.srcObject = e.streams[0] ?? null;
      if (e.track.kind === 'video') setRemoteVideo(true);
    };
    conn.onconnectionstatechange = () => {
      if (conn.connectionState === 'connected') { setPhase('in-call'); setStartedAt((s) => s ?? Date.now()); setError(null); }
      if (conn.connectionState === 'failed') setError('The connection dropped. If it keeps happening, one of you may be on a network that blocks video calls.');
    };
    pc.current = conn;
    return conn;
  };

  const join = async () => {
    setPhase('connecting');
    setError(null);
    try {
      const { HubConnectionBuilder, LogLevel } = await import('@microsoft/signalr');
      const h = new HubConnectionBuilder()
        .withUrl('/hubs/video', { accessTokenFactory: () => useAuth.getState().accessToken ?? '' })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Error)
        .build();
      hub.current = h;

      h.on('peer-joined', () => { setPhase((p) => (p === 'in-call' ? p : 'waiting')); });
      h.on('peer-left', () => { closePeer(); setPhase('left'); });
      h.on('signal', async ({ from, data }: { from: string; data: string }) => {
        const s = JSON.parse(data) as Signal;
        if (s.type === 'offer') {
          const conn = createPeer(from);
          await conn.setRemoteDescription({ type: 'offer', sdp: s.sdp });
          for (const c of pendingCandidates.current.splice(0)) await conn.addIceCandidate(c).catch(() => undefined);
          const answer = await conn.createAnswer();
          await conn.setLocalDescription(answer);
          send(from, { type: 'answer', sdp: answer.sdp! });
        } else if (s.type === 'answer' && pc.current) {
          await pc.current.setRemoteDescription({ type: 'answer', sdp: s.sdp });
          for (const c of pendingCandidates.current.splice(0)) await pc.current.addIceCandidate(c).catch(() => undefined);
        } else if (s.type === 'candidate') {
          if (pc.current?.remoteDescription) await pc.current.addIceCandidate(s.candidate).catch(() => undefined);
          else pendingCandidates.current.push(s.candidate);
        }
      });
      // After a network blip, rejoin; whoever is already there is called again.
      h.onreconnected(() => void enterRoom(h));

      await h.start();
      await enterRoom(h);
    } catch (e) {
      setPhase('lobby');
      setError(e instanceof Error ? e.message.replace(/^.*HubException: /, '') : 'Couldn’t join the call.');
    }
  };

  const enterRoom = async (h: HubConnection) => {
    const res = await h.invoke<{ role: string; peers: { connectionId: string; role: string }[] }>('Join', room.id);
    const peer = res.peers[0];
    if (!peer) { setPhase('waiting'); return; }
    // I arrived second: I start the connection.
    const conn = createPeer(peer.connectionId);
    const offer = await conn.createOffer();
    await conn.setLocalDescription(offer);
    send(peer.connectionId, { type: 'offer', sdp: offer.sdp! });
  };

  const stopAll = () => {
    closePeer();
    void hub.current?.invoke('Leave', room.id).catch(() => undefined);
    void hub.current?.stop();
    hub.current = null;
    stream.current?.getTracks().forEach((t) => t.stop());
    screen.current?.getTracks().forEach((t) => t.stop());
  };
  useEffect(() => () => stopAll(), []); // eslint-disable-line react-hooks/exhaustive-deps

  const end = async () => {
    stopAll();
    setPhase('ended');
    await api.post(`/api/video/rooms/${room.id}/end`).catch(() => undefined);
  };

  const toggleMic = () => { const on = !micOn; stream.current?.getAudioTracks().forEach((t) => (t.enabled = on)); setMicOn(on); };
  const toggleCam = () => { const on = !camOn; stream.current?.getVideoTracks().forEach((t) => (t.enabled = on)); setCamOn(on); };
  const toggleShare = async () => {
    const sender = pc.current?.getSenders().find((s) => s.track?.kind === 'video');
    const camera = stream.current?.getVideoTracks()[0] ?? null;
    if (sharing) {
      screen.current?.getTracks().forEach((t) => t.stop());
      screen.current = null;
      if (sender) await sender.replaceTrack(camera);
      if (localRef.current) localRef.current.srcObject = stream.current;
      setSharing(false);
      return;
    }
    try {
      const s = await navigator.mediaDevices.getDisplayMedia({ video: true });
      screen.current = s;
      const track = s.getVideoTracks()[0]!;
      if (sender) await sender.replaceTrack(track);
      if (localRef.current) localRef.current.srcObject = s;
      track.onended = () => void toggleShareOff(sender ?? null, camera);
      setSharing(true);
    } catch { /* cancelled */ }
  };
  const toggleShareOff = async (sender: RTCRtpSender | null, camera: MediaStreamTrack | null) => {
    screen.current = null;
    if (sender) await sender.replaceTrack(camera);
    if (localRef.current) localRef.current.srcObject = stream.current;
    setSharing(false);
  };

  if (phase === 'ended') {
    return <Shell><Centered title="You left the call" message={startedAt ? `Call length ${clock(now - startedAt)}.` : undefined}
      action={<div className="flex gap-2"><Button variant="contained" onClick={() => { setPhase('lobby'); void startMedia(); }}>Rejoin</Button><BackButton room={room} /></div>} /></Shell>;
  }

  const inLobby = phase === 'lobby' || phase === 'connecting';
  return (
    <Shell>
      <div className="relative flex h-full flex-col">
        <header className="flex items-center gap-3 px-4 py-3 text-white">
          <Img src={room.businessLogoUrl} alt="" fallbackText={room.businessName} className="h-9 w-9 shrink-0" rounded="rounded-full" />
          <div className="min-w-0 flex-1">
            <p className="truncate font-semibold">{room.serviceName ?? 'Video consultation'} · {other}</p>
            <p className="truncate text-xs text-white/70">
              {phase === 'in-call' && startedAt ? clock(now - startedAt) : phase === 'waiting' ? `Waiting for ${other} to join…` : phase === 'left' ? `${other} left the call. They can rejoin.` : 'Check your camera and microphone'}
              {room.staffName ? ` · with ${room.staffName}` : ''}
            </p>
          </div>
        </header>

        <div className="relative min-h-0 flex-1 px-2 pb-2 sm:px-4">
          {/* The other person, full size; me, small in the corner (full size in the lobby). */}
          <div className="relative h-full overflow-hidden rounded-2xl bg-black">
            <video ref={remoteRef} autoPlay playsInline className={`h-full w-full object-contain ${remoteVideo && !inLobby ? '' : 'invisible'}`} />
            {!inLobby && !remoteVideo && (
              <div className="absolute inset-0 grid place-items-center text-center text-white/80">
                <div><CircularProgress color="inherit" size={28} /><p className="mt-3 text-sm">{phase === 'left' ? `${other} left the call` : `Waiting for ${other}…`}</p></div>
              </div>
            )}
            <video ref={localRef} autoPlay playsInline muted
              className={`${inLobby ? 'absolute inset-0 h-full w-full object-cover' : 'absolute bottom-3 right-3 h-28 w-40 rounded-xl border border-white/20 object-cover shadow-lg sm:h-36 sm:w-52'} ${sharing ? '' : '-scale-x-100'} ${camOn || sharing ? '' : 'invisible'}`} />
            {inLobby && (
              <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/80 to-transparent p-4 sm:p-6">
                {mediaError && <Alert severity="warning" sx={{ mb: 2 }} action={<Button color="inherit" size="small" onClick={() => void startMedia()}>Try again</Button>}>{mediaError}</Alert>}
                {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <p className="text-white">Ready to join <strong>{room.serviceName ?? 'your consultation'}</strong> with {other}?</p>
                  {/* Without a camera or microphone the visitor can still join to see and hear the other side. */}
                  <Button variant="contained" size="large" onClick={() => void join()} disabled={phase === 'connecting'}
                    startIcon={phase === 'connecting' ? <CircularProgress size={18} color="inherit" /> : stream.current ? <VideocamRounded /> : <VideocamOffRounded />}
                    sx={{ bgcolor: 'var(--cb-accent)', color: 'var(--cb-on-accent)', '&:hover': { bgcolor: 'var(--cb-accent)' } }}>
                    {phase === 'connecting' ? 'Joining…' : stream.current ? 'Join call' : 'Join without camera'}
                  </Button>
                </div>
              </div>
            )}
          </div>
          {!inLobby && error && <Alert severity="warning" sx={{ position: 'absolute', top: 12, left: 24, right: 24 }}>{error}</Alert>}
        </div>

        <nav className="flex items-center justify-center gap-3 px-4 pb-4 pt-1" aria-label="Call controls">
          <Control label={micOn ? 'Mute' : 'Unmute'} active={micOn} onClick={toggleMic} disabled={!stream.current?.getAudioTracks().length}>{micOn ? <MicRounded /> : <MicOffRounded />}</Control>
          <Control label={camOn ? 'Turn camera off' : 'Turn camera on'} active={camOn} onClick={toggleCam} disabled={!stream.current?.getVideoTracks().length}>
            {camOn ? <VideocamRounded /> : <VideocamOffRounded />}
          </Control>
          {!inLobby && 'getDisplayMedia' in (navigator.mediaDevices ?? {}) && (
            <Control label={sharing ? 'Stop sharing' : 'Share screen'} active={!sharing} onClick={() => void toggleShare()}>
              {sharing ? <StopScreenShareRounded /> : <ScreenShareRounded />}
            </Control>
          )}
          <Tooltip title={inLobby ? 'Leave' : 'End call'}>
            <IconButton onClick={() => (inLobby ? navigate(-1) : void end())} aria-label={inLobby ? 'Leave' : 'End call'}
              sx={{ bgcolor: '#E34948', color: '#fff', width: 56, height: 56, '&:hover': { bgcolor: '#c93b3a' } }}><CallEndRounded /></IconButton>
          </Tooltip>
        </nav>
      </div>
    </Shell>
  );
}

function Control({ label, active, onClick, disabled, children }: { label: string; active: boolean; onClick: () => void; disabled?: boolean; children: React.ReactNode }) {
  return (
    <Tooltip title={label}>
      <span>
        <IconButton onClick={onClick} disabled={disabled} aria-label={label} aria-pressed={!active}
          sx={{ width: 52, height: 52, color: '#fff', bgcolor: active ? 'rgba(255,255,255,.14)' : 'rgba(255,255,255,.9)', ...(active ? {} : { color: '#0B1220' }),
            '&:hover': { bgcolor: active ? 'rgba(255,255,255,.24)' : '#fff' } }}>
          {children}
        </IconButton>
      </span>
    </Tooltip>
  );
}

function BackButton({ room }: { room: VideoRoom }) {
  return <Button variant="outlined" component={Link} to={room.myRole === 'Business' ? '/owner/bookings' : '/account'}
    sx={{ color: '#fff', borderColor: 'rgba(255,255,255,.4)' }}>Back to {room.myRole === 'Business' ? 'bookings' : 'my account'}</Button>;
}

/** The call fills the screen (dark), outside the site's header and footer. */
function Shell({ children }: { children: React.ReactNode }) {
  return <main className="fixed inset-0 z-[1200] bg-[#0B1220]">{children}</main>;
}

function Centered({ title, message, action, icon }: { title: string; message?: string; action?: React.ReactNode; icon?: React.ReactNode }) {
  return (
    <div className="grid h-full place-items-center p-6 text-center text-white">
      <div className="max-w-md">
        {icon && <div className="mb-3 text-white/70">{icon}</div>}
        <h1 className="text-xl font-bold">{title}</h1>
        {message && <p className="mt-2 text-white/75">{message}</p>}
        {action && <div className="mt-5 flex justify-center">{action}</div>}
      </div>
    </div>
  );
}

const clock = (ms: number) => {
  const s = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  return `${h ? `${h}:` : ''}${String(m).padStart(h ? 2 : 1, '0')}:${String(sec).padStart(2, '0')}`;
};


