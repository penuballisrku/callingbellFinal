import { useAuth } from '@/stores/auth';
import type { ApiEnvelope, AuthResult, Paged } from './types';

export class ApiError extends Error {
  constructor(message: string, public status: number, public errors?: Record<string, string[]> | null) {
    super(message);
  }
}

type Query = Record<string, string | number | boolean | null | undefined>;

export function qs(params?: Query): string {
  if (!params) return '';
  const search = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => {
    if (v !== undefined && v !== null && v !== '' && v !== false) search.set(k, String(v));
  });
  const s = search.toString();
  return s ? `?${s}` : '';
}

let refreshing: Promise<boolean> | null = null;

async function refreshSession(): Promise<boolean> {
  const { refreshToken, setSession, clear } = useAuth.getState();
  if (!refreshToken) return false;
  refreshing ??= fetch('/api/auth/refresh', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
  })
    .then(async (r) => {
      if (!r.ok) { clear(); return false; }
      const body = (await r.json()) as ApiEnvelope<AuthResult>;
      setSession(body.data);
      return true;
    })
    .catch(() => false)
    .finally(() => { refreshing = null; });
  return refreshing;
}

async function request<T>(method: string, url: string, body?: unknown, retry = true): Promise<ApiEnvelope<T>> {
  const token = useAuth.getState().accessToken;
  const res = await fetch(url, {
    method,
    headers: {
      Accept: 'application/json',
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (res.status === 401 && retry && token && (await refreshSession())) {
    return request<T>(method, url, body, false);
  }

  let payload: ApiEnvelope<T> | null = null;
  try {
    payload = (await res.json()) as ApiEnvelope<T>;
  } catch {
    /* non-JSON error */
  }

  if (!res.ok || !payload?.success) {
    const fallback =
      res.status === 401 ? 'Please sign in to continue.'
        : res.status === 403 ? 'You do not have access to this page.'
          : res.status === 429 ? 'Too many requests. Please wait a moment.'
            : 'Something went wrong. Please try again.';
    throw new ApiError(payload?.message ?? fallback, res.status, payload?.errors);
  }
  return payload;
}

/** Multipart upload with progress (fetch has no upload progress events, so this uses XHR). */
function uploadOnce<T>(url: string, form: FormData, onProgress?: (fraction: number) => void, signal?: AbortSignal): Promise<{ status: number; payload: ApiEnvelope<T> | null }> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', url);
    const token = useAuth.getState().accessToken;
    if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`);
    xhr.setRequestHeader('Accept', 'application/json');
    xhr.upload.onprogress = (e) => { if (e.lengthComputable) onProgress?.(e.loaded / e.total); };
    xhr.onload = () => {
      let payload: ApiEnvelope<T> | null = null;
      try { payload = JSON.parse(xhr.responseText) as ApiEnvelope<T>; } catch { /* non-JSON */ }
      resolve({ status: xhr.status, payload });
    };
    xhr.onerror = () => reject(new ApiError('Upload failed. Check your connection and try again.', 0));
    xhr.onabort = () => reject(new ApiError('Upload cancelled.', 499));
    signal?.addEventListener('abort', () => xhr.abort());
    xhr.send(form);
  });
}

async function upload<T>(url: string, form: FormData, onProgress?: (fraction: number) => void, signal?: AbortSignal): Promise<ApiEnvelope<T>> {
  let res = await uploadOnce<T>(url, form, onProgress, signal);
  if (res.status === 401 && (await refreshSession())) res = await uploadOnce<T>(url, form, onProgress, signal);
  if (res.status < 200 || res.status >= 300 || !res.payload?.success) {
    const fallback = res.status === 413 ? 'This file is too large to upload.' : 'Upload failed. Please try again.';
    const fieldError = res.payload?.errors ? Object.values(res.payload.errors)[0]?.[0] : undefined;
    throw new ApiError(fieldError ?? res.payload?.message ?? fallback, res.status, res.payload?.errors);
  }
  return res.payload;
}

export const api = {
  get: async <T>(url: string, params?: Query) => (await request<T>('GET', url + qs(params))).data,
  paged: async <T>(url: string, params?: Query): Promise<Paged<T>> => {
    const env = await request<T[]>('GET', url + qs(params));
    return { items: env.data, pagination: env.pagination! };
  },
  envelope: <T>(url: string, params?: Query) => request<T>('GET', url + qs(params)),
  post: async <T>(url: string, body?: unknown) => request<T>('POST', url, body ?? {}),
  put: async <T>(url: string, body?: unknown) => request<T>('PUT', url, body ?? {}),
  patch: async <T>(url: string, body?: unknown) => request<T>('PATCH', url, body ?? {}),
  del: async <T>(url: string) => request<T>('DELETE', url),
  upload,
};

export const errorMessage = (e: unknown) => (e instanceof Error ? e.message : 'Something went wrong. Please try again.');
