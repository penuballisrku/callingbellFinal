import { useEffect, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { useSnackbar } from 'notistack';
import { IconButton, Tooltip } from '@mui/material';
import MicRounded from '@mui/icons-material/MicRounded';
import MicNoneRounded from '@mui/icons-material/MicNoneRounded';

/* The browser's own speech recognition (Web Speech API): Chrome, Edge, Safari and Android browsers; not Firefox. */
interface RecognitionResult { readonly isFinal: boolean; readonly 0: { readonly transcript: string } }
interface RecognitionEvent { readonly resultIndex: number; readonly results: ArrayLike<RecognitionResult> }
interface Recognition {
  lang: string; interimResults: boolean; continuous: boolean; maxAlternatives: number;
  onresult: ((e: RecognitionEvent) => void) | null; onerror: ((e: { error: string }) => void) | null; onend: (() => void) | null;
  start(): void; stop(): void; abort(): void;
}
type RecognitionCtor = new () => Recognition;
declare global { interface Window { SpeechRecognition?: RecognitionCtor; webkitSpeechRecognition?: RecognitionCtor } }

const Recognizer: RecognitionCtor | undefined = typeof window === 'undefined' ? undefined : window.SpeechRecognition ?? window.webkitSpeechRecognition;

/** Voice input needs the API and the microphone, which browsers only allow on https:// and localhost. */
export const voiceSearchSupported = !!Recognizer && typeof window !== 'undefined' && window.isSecureContext;

/** Indian English understands Indian names and places best; the page language is used when it is another one. */
const LANG = typeof document !== 'undefined' && document.documentElement.lang && !document.documentElement.lang.startsWith('en')
  ? document.documentElement.lang : 'en-IN';

const ERRORS: Record<string, string> = {
  'not-allowed': 'Allow microphone access for this site to search by voice.',
  'service-not-allowed': 'Allow microphone access for this site to search by voice.',
  'audio-capture': 'No microphone was found.',
  'no-speech': 'Didn’t catch that. Tap the mic and try again.',
  network: 'Voice search needs an internet connection.',
};

/** Only one box listens at a time. */
let active: Recognition | null = null;

/**
 * Mic button for a search box: what the visitor says fills the box as they speak. With `submit`, the surrounding form is submitted
 * when they stop speaking (the main search boxes); without it, the text is only filled in (filter boxes). Renders nothing where
 * the browser can't do voice input, so the box looks as it always did.
 *
 * @param onText Called with the words so far (`final` false) and once with the finished phrase (`final` true).
 */
export function VoiceSearchButton({ onText, submit = false, label = 'Search by voice', size = 'small', className }: {
  onText: (text: string, final: boolean) => void; submit?: boolean; label?: string; size?: 'small' | 'medium'; className?: string;
}) {
  const { enqueueSnackbar } = useSnackbar();
  const [listening, setListening] = useState(false);
  const button = useRef<HTMLButtonElement>(null);
  const recognition = useRef<Recognition | null>(null);
  // The latest callback, so a recognition that started earlier reports to the current render.
  const report = useRef(onText);
  report.current = onText;

  useEffect(() => () => recognition.current?.abort(), []);

  if (!voiceSearchSupported) return null;

  const toggle = () => {
    if (listening) { recognition.current?.stop(); return; }
    active?.abort();
    const r = new Recognizer!();
    r.lang = LANG;
    r.interimResults = true;
    r.continuous = false;
    r.maxAlternatives = 1;
    let finalText = '';
    r.onresult = (e) => {
      let interim = '';
      for (let i = e.resultIndex; i < e.results.length; i++) {
        const res = e.results[i]!;
        if (res.isFinal) finalText += res[0].transcript; else interim += res[0].transcript;
      }
      const text = (finalText + interim).trim();
      if (text) report.current(text, false);
    };
    r.onerror = (e) => { if (e.error !== 'aborted') enqueueSnackbar(ERRORS[e.error] ?? 'Voice search isn’t available right now.', { variant: 'warning' }); };
    r.onend = () => {
      if (active === r) active = null;
      recognition.current = null;
      setListening(false);
      const text = finalText.trim();
      if (!text) return;
      // Render the finished text first, so the form submits what was said.
      flushSync(() => report.current(text, true));
      if (submit) button.current?.closest('form')?.requestSubmit();
    };
    recognition.current = r;
    active = r;
    try {
      r.start();
      setListening(true);
    } catch {
      active = null;
      enqueueSnackbar('Voice search isn’t available right now.', { variant: 'warning' });
    }
  };

  return (
    <Tooltip title={listening ? 'Listening… tap to stop' : label}>
      <IconButton ref={button} type="button" onClick={toggle} size={size} aria-label={listening ? 'Stop listening' : label} aria-pressed={listening}
        className={className}
        sx={{
          flexShrink: 0,
          color: listening ? '#fff' : 'text.secondary',
          bgcolor: listening ? 'error.main' : 'transparent',
          '&:hover': { bgcolor: listening ? 'error.dark' : 'action.hover' },
        }}>
        {listening ? <MicRounded fontSize="small" /> : <MicNoneRounded fontSize="small" />}
      </IconButton>
    </Tooltip>
  );
}
