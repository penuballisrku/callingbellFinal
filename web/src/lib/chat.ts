import { useEffect, useRef } from 'react';
import type { HubConnection } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useAuth } from '@/stores/auth';
import type { ChatMessage, ChatRole } from './types';

export interface ChatReadEvent { conversationId: string; role: ChatRole; readAt: string }
export interface ChatTypingEvent { conversationId: string; role: ChatRole }
type Handlers = { message?: (m: ChatMessage) => void; read?: (e: ChatReadEvent) => void; typing?: (e: ChatTypingEvent) => void };

/** One chat connection per signed-in user (loaded on first use), shared by every chat view and the header badge. */
let chat: { userId: string; conn: Promise<HubConnection> } | null = null;

function chatConnection(userId: string): Promise<HubConnection> {
  if (chat?.userId !== userId) {
    void chat?.conn.then((c) => c.stop());
    const conn = import('@microsoft/signalr').then(({ HubConnectionBuilder, LogLevel }) => {
      const c = new HubConnectionBuilder()
        .withUrl('/hubs/chat', { accessTokenFactory: () => useAuth.getState().accessToken ?? '' })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Error)
        .build();
      void c.start().catch(() => undefined);
      return c;
    });
    chat = { userId, conn };
  }
  return chat.conn;
}

/** Live chat events while signed in. Also keeps unread badges and conversation lists fresh. */
export function useChatEvents(handlers: Handlers) {
  const userId = useAuth((s) => s.user?.id);
  const queryClient = useQueryClient();
  const ref = useRef(handlers);
  ref.current = handlers;
  useEffect(() => {
    if (!userId) return;
    let conn: HubConnection | null = null;
    let active = true;
    const onMessage = (m: ChatMessage) => {
      ref.current.message?.(m);
      void queryClient.invalidateQueries({ queryKey: ['chat', 'unread'] });
      void queryClient.invalidateQueries({ queryKey: ['chat', 'conversations'] });
    };
    const onRead = (e: ChatReadEvent) => { ref.current.read?.(e); void queryClient.invalidateQueries({ queryKey: ['chat', 'unread'] }); };
    const onTyping = (e: ChatTypingEvent) => ref.current.typing?.(e);
    void chatConnection(userId).then((c) => {
      if (!active) return;
      conn = c;
      c.on('message', onMessage);
      c.on('read', onRead);
      c.on('typing', onTyping);
    });
    return () => {
      active = false;
      conn?.off('message', onMessage);
      conn?.off('read', onRead);
      conn?.off('typing', onTyping);
    };
  }, [userId, queryClient]);
}

/** Tells the other person "typing…", at most every 3 seconds. */
export function useTypingSignal(conversationId: string | null) {
  const userId = useAuth((s) => s.user?.id);
  const last = useRef(0);
  return () => {
    if (!userId || !conversationId || Date.now() - last.current < 3000) return;
    last.current = Date.now();
    void chatConnection(userId).then((c) => (c.state === 'Connected' ? c.invoke('Typing', conversationId) : undefined)).catch(() => undefined);
  };
}
