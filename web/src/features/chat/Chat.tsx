import { useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSnackbar } from 'notistack';
import { Badge, Button, CircularProgress, Drawer, IconButton, Skeleton, TextField, Tooltip } from '@mui/material';
import SendRounded from '@mui/icons-material/SendRounded';
import AttachFileRounded from '@mui/icons-material/AttachFileRounded';
import ArrowBackRounded from '@mui/icons-material/ArrowBackRounded';
import CloseRounded from '@mui/icons-material/CloseRounded';
import DoneRounded from '@mui/icons-material/DoneRounded';
import DoneAllRounded from '@mui/icons-material/DoneAllRounded';
import PictureAsPdfOutlined from '@mui/icons-material/PictureAsPdfOutlined';
import VideocamRounded from '@mui/icons-material/VideocamRounded';
import ChatBubbleOutlineRounded from '@mui/icons-material/ChatBubbleOutlineRounded';
import ForumOutlined from '@mui/icons-material/ForumOutlined';
import dayjs from 'dayjs';
import { api, errorMessage } from '@/lib/api';
import { ago, time } from '@/lib/format';
import { useChatEvents, useTypingSignal } from '@/lib/chat';
import { useAuth, isOwner } from '@/stores/auth';
import type { ChatMessage, ChatUnread, Conversation } from '@/lib/types';
import { AvailabilityBadge, EmptyState, ErrorState, Img } from '@/components/ui';

const PAGE = 40;
const MAX_FILE_MB = 10;
/** The inbox's smallest height on tablets and desktop, and the space kept below it. */
const INBOX_MIN_HEIGHT = 480;
const INBOX_BOTTOM_GAP = 24;

/* ===================== Thread ===================== */

/**
 * One conversation: history (older pages on scroll-back), live messages, read receipts (✓ sent, ✓✓ read), "typing…", photos and PDFs,
 * and video calls started from the chat. The other side's messages are marked read while the thread is open.
 */
export function ChatThread({ conversation: c, onBack, onClose }: { conversation: Conversation; onBack?: () => void; onClose?: () => void }) {
  const { enqueueSnackbar } = useSnackbar();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [hasOlder, setHasOlder] = useState(false);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [otherReadAt, setOtherReadAt] = useState<string | null>(c.otherLastReadAt ?? null);
  const [typing, setTyping] = useState(false);
  const [text, setText] = useState('');
  const scroller = useRef<HTMLDivElement>(null);
  const stickToBottom = useRef(true);
  const typingTimer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const signalTyping = useTypingSignal(c.id);
  const other = c.myRole === 'Customer' ? c.businessName : c.customerName;

  const history = useQuery({
    queryKey: ['chat', 'messages', c.id],
    queryFn: () => api.get<ChatMessage[]>(`/api/chat/conversations/${c.id}/messages`, { take: PAGE }),
    staleTime: 0,
    gcTime: 0,
  });
  useEffect(() => {
    if (!history.data) return;
    setMessages(history.data);
    setHasOlder(history.data.length === PAGE);
    stickToBottom.current = true;
  }, [history.data]);

  const markRead = useMutation({
    mutationFn: () => api.post(`/api/chat/conversations/${c.id}/read`),
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: ['chat', 'unread'] }); void queryClient.invalidateQueries({ queryKey: ['chat', 'conversations'] }); },
  });
  // Opening the conversation reads it.
  useEffect(() => { if (history.isSuccess) markRead.mutate(); }, [history.isSuccess, c.id]); // eslint-disable-line react-hooks/exhaustive-deps

  useChatEvents({
    message: (m) => {
      if (m.conversationId !== c.id) return;
      setMessages((prev) => (prev.some((x) => x.id === m.id) ? prev : [...prev, m]));
      if (m.senderRole !== c.myRole) { setTyping(false); if (document.visibilityState === 'visible') markRead.mutate(); }
    },
    read: (e) => { if (e.conversationId === c.id && e.role !== c.myRole) setOtherReadAt(e.readAt); },
    typing: (e) => {
      if (e.conversationId !== c.id || e.role === c.myRole) return;
      setTyping(true);
      clearTimeout(typingTimer.current);
      typingTimer.current = setTimeout(() => setTyping(false), 4000);
    },
  });

  // Keep the newest message in view (unless the person scrolled up to read history).
  useLayoutEffect(() => {
    const el = scroller.current;
    if (el && stickToBottom.current) el.scrollTop = el.scrollHeight;
  }, [messages, typing]);

  const loadOlder = async () => {
    const first = messages[0];
    if (!first || loadingOlder) return;
    setLoadingOlder(true);
    const el = scroller.current;
    const before = el?.scrollHeight ?? 0;
    try {
      const older = await api.get<ChatMessage[]>(`/api/chat/conversations/${c.id}/messages`, { before: first.sentAt, take: PAGE });
      setHasOlder(older.length === PAGE);
      stickToBottom.current = false;
      setMessages((prev) => [...older.filter((o) => !prev.some((p) => p.id === o.id)), ...prev]);
      requestAnimationFrame(() => { if (el) el.scrollTop = el.scrollHeight - before; });
    } catch (e) { enqueueSnackbar(errorMessage(e), { variant: 'error' }); }
    finally { setLoadingOlder(false); }
  };

  const add = (m: ChatMessage) => { stickToBottom.current = true; setMessages((prev) => (prev.some((x) => x.id === m.id) ? prev : [...prev, m])); };
  const send = useMutation({
    mutationFn: (body: string) => api.post<ChatMessage>(`/api/chat/conversations/${c.id}/messages`, { body }),
    onSuccess: (res) => { add(res.data); setText(''); void queryClient.invalidateQueries({ queryKey: ['chat', 'conversations'] }); },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });
  const attach = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append('file', file);
      if (text.trim()) form.append('caption', text.trim());
      return api.upload<ChatMessage>(`/api/chat/conversations/${c.id}/attachments`, form);
    },
    onSuccess: (res) => { add(res.data); setText(''); },
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });
  const video = useMutation({
    mutationFn: () => api.post<string>(`/api/chat/conversations/${c.id}/video`),
    onSuccess: (res) => navigate(`/video/${res.data}`),
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });

  const submit = () => { const body = text.trim(); if (body && !send.isPending) send.mutate(body); };
  const pickFile = (file?: File) => {
    if (!file) return;
    if (file.size > MAX_FILE_MB * 1024 * 1024) { enqueueSnackbar(`Files can be up to ${MAX_FILE_MB} MB.`, { variant: 'warning' }); return; }
    attach.mutate(file);
  };

  // Day separators.
  const items = useMemo(() => messages.map((m, i) => ({ m, day: i === 0 || !dayjs(m.sentAt).isSame(messages[i - 1]!.sentAt, 'day') ? dayLabel(m.sentAt) : null })), [messages]);

  return (
    <div className="flex h-full min-h-0 flex-col bg-canvas">
      <header className="flex shrink-0 items-center gap-2 border-b border-line bg-surface px-3 py-2.5">
        {onBack && <IconButton onClick={onBack} aria-label="Back to conversations" size="small"><ArrowBackRounded /></IconButton>}
        {c.myRole === 'Customer'
          ? <Img src={c.businessLogoUrl} alt="" fallbackText={c.businessName} className="h-9 w-9 shrink-0" rounded="rounded-full" />
          : <span aria-hidden className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-subtle text-sm font-semibold">{initialsOf(c.customerName)}</span>}
        <div className="min-w-0 flex-1">
          <p className="truncate font-semibold leading-tight">
            {c.myRole === 'Customer' ? <Link to={`/business/${c.businessSlug}`} className="hover:underline">{c.businessName}</Link> : c.customerName}
          </p>
          <p className="truncate text-xs text-muted">
            {typing ? <span className="font-medium text-success">typing…</span>
              : c.myRole === 'Customer' && c.availabilityStatus ? <AvailabilityBadge status={c.availabilityStatus} compact /> : c.myRole === 'Business' ? 'Customer' : ''}
          </p>
        </div>
        {c.offersVideoConsultation && (
          <Tooltip title="Start a video call">
            <span><IconButton onClick={() => video.mutate()} disabled={video.isPending} aria-label="Start a video call"><VideocamRounded /></IconButton></span>
          </Tooltip>
        )}
        {onClose && <IconButton onClick={onClose} aria-label="Close chat" size="small"><CloseRounded /></IconButton>}
      </header>

      <div ref={scroller} className="min-h-0 flex-1 overflow-y-auto px-3 py-4 sm:px-5" role="log" aria-live="polite" aria-label={`Messages with ${other}`}
        onScroll={(e) => { const el = e.currentTarget; stickToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80; if (el.scrollTop < 40 && hasOlder) void loadOlder(); }}>
        {history.isLoading ? (
          <div className="space-y-3">{[60, 40, 70, 50].map((w, i) => <Skeleton key={i} variant="rounded" height={44} sx={{ width: `${w}%`, ml: i % 2 ? 'auto' : 0, borderRadius: '14px' }} />)}</div>
        ) : history.isError ? (
          <ErrorState message="We couldn’t load the messages." onRetry={() => void history.refetch()} />
        ) : (
          <>
            {hasOlder && <div className="mb-3 text-center">{loadingOlder ? <CircularProgress size={18} /> : <Button size="small" onClick={() => void loadOlder()}>Load earlier messages</Button>}</div>}
            {messages.length === 0 && (
              <div className="py-10 text-center text-sm text-muted">
                <ChatBubbleOutlineRounded className="text-faint" sx={{ fontSize: 36 }} />
                <p className="mt-2">Say hello to {other}. {c.myRole === 'Customer' ? 'Ask about prices, availability or anything else.' : ''}</p>
              </div>
            )}
            <ol className="space-y-1.5">
              {items.map(({ m, day }) => (
                <li key={m.id}>
                  {day && <div className="my-3 text-center"><span className="rounded-full bg-subtle px-2.5 py-0.5 text-[11px] font-medium text-muted">{day}</span></div>}
                  <Bubble m={m} mine={m.senderRole === c.myRole} read={!!m.readAt || (!!otherReadAt && otherReadAt >= m.sentAt)} />
                </li>
              ))}
            </ol>
            {typing && <div className="mt-2 inline-flex items-center gap-1 rounded-2xl bg-surface px-3 py-2 text-muted" aria-label={`${other} is typing`}><Dot /><Dot d={150} /><Dot d={300} /></div>}
          </>
        )}
      </div>

      <form className="flex shrink-0 items-end gap-1.5 border-t border-line bg-surface p-2 sm:p-3" onSubmit={(e) => { e.preventDefault(); submit(); }}>
        <Tooltip title={`Attach a photo or PDF (up to ${MAX_FILE_MB} MB)`}>
          <IconButton component="label" aria-label="Attach a photo or PDF" disabled={attach.isPending}>
            {attach.isPending ? <CircularProgress size={20} /> : <AttachFileRounded />}
            <input type="file" hidden accept="image/jpeg,image/png,image/webp,application/pdf" onChange={(e) => { pickFile(e.target.files?.[0]); e.target.value = ''; }} />
          </IconButton>
        </Tooltip>
        <TextField value={text} onChange={(e) => { setText(e.target.value); signalTyping(); }} placeholder="Type a message" multiline maxRows={5} fullWidth size="small"
          onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) { e.preventDefault(); submit(); } }}
          slotProps={{ htmlInput: { maxLength: 2000, 'aria-label': 'Message' } }} sx={{ '& .MuiOutlinedInput-root': { borderRadius: '20px' } }} />
        <IconButton type="submit" color="primary" disabled={!text.trim() || send.isPending} aria-label="Send">
          {send.isPending ? <CircularProgress size={20} /> : <SendRounded />}
        </IconButton>
      </form>
    </div>
  );
}

function Bubble({ m, mine, read }: { m: ChatMessage; mine: boolean; read: boolean }) {
  const a = m.attachment;
  return (
    <div className={`flex ${mine ? 'justify-end' : 'justify-start'}`}>
      <div className={`max-w-[85%] rounded-2xl px-3 py-2 shadow-[var(--cb-shadow-xs)] sm:max-w-[70%] ${mine ? 'rounded-br-md bg-inverse text-on-inverse' : 'rounded-bl-md bg-surface text-ink'}`}>
        {a && (a.isImage ? (
          <a href={a.url} target="_blank" rel="noreferrer" className="mb-1 block overflow-hidden rounded-lg">
            <img src={a.url} alt={a.name} loading="lazy" className="max-h-64 w-full bg-subtle object-cover" />
          </a>
        ) : (
          <a href={a.url} target="_blank" rel="noreferrer" className={`mb-1 flex items-center gap-2 rounded-lg px-2 py-1.5 ${mine ? 'bg-white/10' : 'bg-subtle'}`}>
            <PictureAsPdfOutlined sx={{ fontSize: 22 }} /><span className="min-w-0 truncate text-sm font-medium">{a.name}</span>
            <span className="shrink-0 text-[11px] opacity-70">{Math.max(1, Math.round(a.size / 1024))} KB</span>
          </a>
        ))}
        {m.body && <p className="whitespace-pre-wrap break-words text-[14px] leading-snug">{m.body}</p>}
        {m.videoRoomId && (
          <Button component={Link} to={`/video/${m.videoRoomId}`} size="small" variant="contained" disableElevation startIcon={<VideocamRounded />}
            sx={{ mt: 1, bgcolor: 'var(--cb-accent)', color: 'var(--cb-on-accent)', '&:hover': { bgcolor: 'var(--cb-accent)' } }}>Join video call</Button>
        )}
        <p className={`mt-0.5 flex items-center justify-end gap-0.5 text-[10px] ${mine ? 'opacity-75' : 'text-muted'}`}>
          {time(m.sentAt)}
          {mine && (read
            ? <DoneAllRounded sx={{ fontSize: 14, color: 'var(--cb-accent)' }} aria-label="Read" />
            : <DoneRounded sx={{ fontSize: 14 }} aria-label="Sent" />)}
        </p>
      </div>
    </div>
  );
}

const Dot = ({ d = 0 }: { d?: number }) => <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-current" style={{ animationDelay: `${d}ms` }} />;
const initialsOf = (name: string) => name.split(' ').filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('');
function dayLabel(v: string) {
  const d = dayjs(v);
  if (d.isSame(dayjs(), 'day')) return 'Today';
  if (d.isSame(dayjs().subtract(1, 'day'), 'day')) return 'Yesterday';
  return d.format(d.isSame(dayjs(), 'year') ? 'ddd, D MMM' : 'D MMM YYYY');
}

/* ===================== Inbox ===================== */

/**
 * Conversations and the open thread: side by side from 900 px, one at a time on smaller screens. The open conversation is in the URL
 * (?c=), so notification links open it directly.
 */
export function ChatInbox({ role, businessId }: { role: 'Customer' | 'Business'; businessId?: string }) {
  const [params, setParams] = useSearchParams();
  const selectedId = params.get('c');
  const select = (id: string | null) => setParams((p) => { const n = new URLSearchParams(p); if (id) n.set('c', id); else n.delete('c'); return n; }, { replace: !id });
  const list = useQuery({
    queryKey: ['chat', 'conversations', role, businessId ?? null],
    queryFn: () => api.get<Conversation[]>('/api/chat/conversations', { role, businessId }),
    enabled: role === 'Customer' || !!businessId,
  });
  const conversations = list.data ?? [];
  const selected = conversations.find((c) => c.id === selectedId) ?? null;

  // On phones an open conversation covers the screen (see the section below): the page behind it must not scroll.
  useEffect(() => {
    if (!selected || !window.matchMedia('(max-width: 767px)').matches) return;
    const overflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => { document.body.style.overflow = overflow; };
  }, [selected]);

  // Tablets and desktop: the inbox fills the screen below wherever the page places it (the customer account page has more above it
  // than the owner portal), so the message box is always in view. Measured before paint, so nothing jumps.
  const box = useRef<HTMLDivElement>(null);
  const [height, setHeight] = useState<number | null>(null);
  useLayoutEffect(() => {
    const fit = () => {
      if (!box.current) return;
      const top = box.current.getBoundingClientRect().top + window.scrollY;
      setHeight(Math.max(INBOX_MIN_HEIGHT, Math.floor(window.innerHeight - top - INBOX_BOTTOM_GAP)));
    };
    fit();
    window.addEventListener('resize', fit);
    return () => window.removeEventListener('resize', fit);
  }, []);

  return (
    // Phones: the list is part of the page; tablets and desktop: list and conversation side by side, filling the screen.
    <div ref={box} style={height ? ({ '--inbox-h': `${height}px` } as CSSProperties) : undefined}
      className="card grid min-h-[320px] overflow-hidden md:h-[var(--inbox-h,calc(100svh-210px))] md:min-h-[480px] md:grid-cols-[320px_1fr]">
      <aside className={`min-h-0 flex-col border-r border-line ${selected ? 'hidden md:flex' : 'flex'}`} aria-label="Conversations">
        <div className="border-b border-line px-4 py-3 text-sm font-semibold">Messages</div>
        <div className="min-h-0 flex-1 overflow-y-auto">
          {list.isLoading ? (
            <div className="space-y-2 p-3">{[0, 1, 2, 3].map((i) => <Skeleton key={i} variant="rounded" height={58} />)}</div>
          ) : list.isError ? (
            <ErrorState onRetry={() => void list.refetch()} />
          ) : conversations.length === 0 ? (
            <EmptyState icon={<ForumOutlined />} title="No messages yet"
              message={role === 'Customer' ? 'Chat with a business from its page to ask about prices or availability.' : 'When customers message your business, the conversations appear here.'} />
          ) : (
            <ul>
              {conversations.map((c) => (
                <li key={c.id}>
                  <button type="button" onClick={() => select(c.id)} aria-current={c.id === selectedId ? 'true' : undefined}
                    className={`flex w-full items-center gap-3 border-b border-line px-3 py-3 text-left transition-colors hover:bg-subtle ${c.id === selectedId ? 'bg-subtle' : ''}`}>
                    {role === 'Customer'
                      ? <Img src={c.businessLogoUrl} alt="" fallbackText={c.businessName} className="h-10 w-10 shrink-0" rounded="rounded-full" />
                      : <span aria-hidden className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-subtle text-sm font-semibold">{initialsOf(c.customerName)}</span>}
                    <span className="min-w-0 flex-1">
                      <span className="flex items-baseline justify-between gap-2">
                        <span className={`truncate text-sm ${c.unreadCount ? 'font-bold' : 'font-semibold'}`}>{role === 'Customer' ? c.businessName : c.customerName}</span>
                        <span className="shrink-0 text-[11px] text-muted">{c.lastMessageAt ? ago(c.lastMessageAt) : ''}</span>
                      </span>
                      <span className="flex items-center justify-between gap-2">
                        <span className={`truncate text-[13px] ${c.unreadCount ? 'text-ink' : 'text-muted'}`}>
                          {c.lastSenderRole === c.myRole && 'You: '}{c.lastMessagePreview ?? 'No messages yet'}
                        </span>
                        {c.unreadCount > 0 && <span className="grid h-5 min-w-5 shrink-0 place-items-center rounded-full bg-accent px-1.5 text-[11px] font-bold text-on-accent">{c.unreadCount}</span>}
                      </span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </aside>
      {/* Phones: an open conversation fills the screen (dvh follows the browser bars and keyboard), so the message box is always in view. */}
      <section className={`min-h-0 ${selected
        ? 'fixed inset-x-0 top-0 z-[1250] flex h-dvh flex-col bg-canvas pb-[env(safe-area-inset-bottom)] md:static md:z-auto md:h-auto md:pb-0'
        : 'hidden md:flex md:flex-col'}`}>
        {selected ? <ChatThread key={selected.id} conversation={selected} onBack={() => select(null)} />
          : <div className="grid h-full place-items-center p-6"><EmptyState icon={<ChatBubbleOutlineRounded />} title="Choose a conversation" message="Pick a conversation on the left to read and reply." /></div>}
      </section>
    </div>
  );
}

/* ===================== From a business page ===================== */

/** Opens the customer's conversation with a business in a side panel (full screen on phones). */
export function ChatDrawer({ businessId, open, onClose }: { businessId: string; open: boolean; onClose: () => void }) {
  const { enqueueSnackbar } = useSnackbar();
  const start = useQuery({
    queryKey: ['chat', 'start', businessId],
    queryFn: () => api.post<Conversation>('/api/chat/conversations', { businessId }).then((r) => r.data),
    enabled: open,
    staleTime: 60_000,
    retry: false,
  });
  useEffect(() => { if (start.isError) { enqueueSnackbar(errorMessage(start.error), { variant: 'error' }); onClose(); } }, [start.isError]); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <Drawer anchor="right" open={open} onClose={onClose} slotProps={{ paper: { sx: { width: { xs: '100%', sm: 440 }, maxWidth: '100%' } } }}>
      <div className="flex h-full flex-col">
        {start.data ? <ChatThread conversation={start.data} onClose={onClose} />
          : <div className="grid h-full place-items-center"><CircularProgress /></div>}
      </div>
    </Drawer>
  );
}

/* ===================== Header ===================== */

/** Messages icon with the unread count (as a customer and for the businesses I own); live via the chat connection. */
export function MessagesButton({ dark }: { dark?: boolean }) {
  const user = useAuth((s) => s.user);
  const unread = useQuery({ queryKey: ['chat', 'unread'], queryFn: () => api.get<ChatUnread>('/api/chat/unread'), enabled: !!user, refetchInterval: 120_000 });
  useChatEvents({});
  if (!user) return null;
  const owner = isOwner(user);
  const count = (unread.data?.asCustomer ?? 0) + (unread.data?.asBusiness ?? 0);
  return (
    <Tooltip title="Messages">
      <IconButton component={Link} to={owner ? '/owner/messages' : '/account?tab=messages'} aria-label={`Messages, ${count} unread`} sx={{ color: dark ? '#fff' : 'text.primary' }}>
        <Badge badgeContent={count} color="secondary" max={99}><ChatBubbleOutlineRounded /></Badge>
      </IconButton>
    </Tooltip>
  );
}
