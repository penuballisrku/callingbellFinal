import { create } from 'zustand';
import type { AssistantFilters, AssistantReply } from '@/lib/types';

export interface AssistantRequest { message: string | null; context: AssistantFilters | null; city: string | null; areaId: string | null }

export interface Turn {
  id: number;
  role: 'user' | 'assistant';
  text?: string;
  /** The request that produced this answer, repeated while the AI is still working on it. */
  request?: AssistantRequest;
  reply?: AssistantReply;
  error?: string;
  loading?: boolean;
  /** How many times the answer was refreshed while `reply.aiPending`. */
  polls?: number;
}

interface AssistantState {
  open: boolean;
  turns: Turn[];
  /** A prompt to send as soon as the panel opens (e.g. from the home page search box). */
  queued: string | null;
  openAssistant: (prompt?: string) => void;
  close: () => void;
  reset: () => void;
  takeQueued: () => string | null;
  add: (turn: Omit<Turn, 'id'>) => number;
  update: (id: number, patch: Partial<Turn>) => void;
}

let nextId = 1;

/** The conversation lives for the browser session (not persisted), so closing and reopening the panel keeps it. */
export const useAssistant = create<AssistantState>()((set, get) => ({
  open: false,
  turns: [],
  queued: null,
  openAssistant: (prompt) => set({ open: true, queued: prompt?.trim() || null }),
  close: () => set({ open: false }),
  reset: () => set({ turns: [], queued: null }),
  takeQueued: () => { const q = get().queued; if (q) set({ queued: null }); return q; },
  add: (turn) => { const id = nextId++; set((s) => ({ turns: [...s.turns, { ...turn, id }] })); return id; },
  update: (id, patch) => set((s) => ({ turns: s.turns.map((t) => (t.id === id ? { ...t, ...patch } : t)) })),
}));
