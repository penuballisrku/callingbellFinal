import { createTheme, alpha, type Theme } from '@mui/material/styles';
import { accents, type AccentTheme } from '@/stores/theme';

type Mode = 'light' | 'dark';

/** Token values per mode - mirror of the CSS variables in index.css (MUI needs concrete colours for alpha()). */
export const tokens = {
  light: { canvas: '#F7F8FA', surface: '#FFFFFF', subtle: '#F2F4F7', ink: '#161616', ink2: '#344054', muted: '#667085', faint: '#98A2B3', line: '#E5E7EB', lineStrong: '#CBD2DC', inverse: '#0B1220', onInverse: '#FFFFFF' },
  dark: { canvas: '#080C14', surface: '#0F1522', subtle: '#171F2E', ink: '#F2F4F7', ink2: '#CDD5DF', muted: '#98A2B3', faint: '#6B778A', line: '#1F2838', lineStrong: '#334056', inverse: '#F2F4F7', onInverse: '#0B1220' },
} as const;

/** Brand constants used by artwork on always-dark surfaces. */
export const brand = { navy: '#0B1220', amber: '#F4A62C' };

/** Validated categorical chart palette per mode (fixed order - never cycle). */
export const seriesByMode: Record<Mode, string[]> = {
  light: ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#4a3aa7', '#e34948'],
  dark: ['#3987e5', '#d95926', '#199e70', '#c98500', '#d55181', '#008300', '#9085e9', '#e66767'],
};

export function chartColors(mode: Mode) {
  const t = tokens[mode];
  return { series: seriesByMode[mode], grid: mode === 'dark' ? '#1B2433' : '#EEF0F3', axis: t.faint, track: t.subtle, cursor: t.subtle, surface: t.surface };
}

/** Soft, layered elevation scale (xs, md, lg) - mirrors --cb-shadow-* in index.css. MUI's defaults are too heavy for this design. */
const elevation: Record<Mode, [string, string, string]> = {
  light: [
    '0 1px 2px rgba(16,24,40,.04), 0 1px 3px rgba(16,24,40,.06)',
    '0 2px 4px -2px rgba(16,24,40,.05), 0 6px 16px -4px rgba(16,24,40,.08)',
    '0 4px 8px -4px rgba(16,24,40,.06), 0 16px 32px -8px rgba(16,24,40,.12)',
  ],
  dark: [
    '0 1px 2px rgba(0,0,0,.3), 0 1px 3px rgba(0,0,0,.25)',
    '0 2px 4px -2px rgba(0,0,0,.4), 0 8px 20px -4px rgba(0,0,0,.45)',
    '0 4px 8px -4px rgba(0,0,0,.45), 0 20px 40px -8px rgba(0,0,0,.55)',
  ],
};

export function buildTheme(mode: Mode, accentKey: AccentTheme): Theme {
  const t = tokens[mode];
  const accent = accents.find((a) => a.key === accentKey) ?? accents[0]!;
  const accentMain = mode === 'dark' ? accent.dark : accent.light;
  const onAccent = accentKey === 'amber' || mode === 'dark' ? '#0B1220' : '#FFFFFF';
  const errorMain = mode === 'dark' ? '#F97066' : '#D92D20';
  const successMain = mode === 'dark' ? '#3CCB7F' : '#12B76A';
  const [shXs, shMd, shLg] = elevation[mode];
  const shadows = ['none', ...Array.from({ length: 24 }, (_, i) => (i < 2 ? shXs : i < 8 ? shMd : shLg))] as Theme['shadows'];
  const ring = alpha(accentMain, mode === 'dark' ? 0.35 : 0.28);
  const focusRing = { outline: 'none', boxShadow: `0 0 0 3px ${ring}` };
  const tooltipBg = mode === 'dark' ? '#2A3445' : t.inverse;

  return createTheme({
    palette: {
      mode,
      primary: { main: t.inverse, contrastText: t.onInverse },
      secondary: { main: accentMain, contrastText: onAccent },
      background: { default: t.canvas, paper: t.surface },
      text: { primary: t.ink, secondary: t.muted, disabled: t.faint },
      divider: t.line,
      success: { main: successMain, contrastText: '#FFFFFF' },
      warning: { main: '#F79009' },
      error: { main: errorMain },
      info: { main: '#2E90FA' },
      action: { hover: alpha(t.ink, 0.05), selected: alpha(accentMain, 0.16), focus: alpha(accentMain, 0.12) },
    },
    shape: { borderRadius: 10 },
    shadows,
    typography: {
      fontFamily: "'Inter Variable', 'Segoe UI', Roboto, Helvetica, Arial, sans-serif",
      h1: { fontWeight: 700, letterSpacing: '-0.025em' },
      h2: { fontWeight: 700, letterSpacing: '-0.022em' },
      h3: { fontWeight: 700, letterSpacing: '-0.02em' },
      h4: { fontWeight: 700, letterSpacing: '-0.02em', fontSize: '1.625rem', lineHeight: 1.25 },
      h5: { fontWeight: 650, fontSize: '1.25rem', letterSpacing: '-0.01em' },
      h6: { fontWeight: 650, fontSize: '1.0625rem', letterSpacing: '-0.005em' },
      subtitle1: { fontWeight: 600 },
      subtitle2: { fontWeight: 600 },
      body1: { fontSize: '0.9375rem', lineHeight: 1.6 },
      body2: { fontSize: '0.875rem', lineHeight: 1.55 },
      overline: { fontWeight: 600, letterSpacing: '0.08em', fontSize: '0.6875rem' },
      button: { fontWeight: 600, textTransform: 'none', letterSpacing: 0 },
    },
    components: {
      MuiButtonBase: { defaultProps: { disableRipple: true } },
      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          root: {
            borderRadius: 8, paddingInline: 16, minHeight: 38,
            transition: 'background-color 150ms ease, border-color 150ms ease, box-shadow 150ms ease, color 150ms ease',
            '&.Mui-focusVisible': focusRing,
          },
          sizeSmall: { minHeight: 32, paddingInline: 12, fontSize: '0.8125rem' },
          sizeLarge: { minHeight: 46, paddingBlock: 10, paddingInline: 20, fontSize: '0.9375rem' },
          containedPrimary: { '&:hover': { backgroundColor: alpha(t.inverse, 0.88) } },
          outlined: {
            borderColor: t.line, backgroundColor: t.surface, boxShadow: mode === 'light' ? '0 1px 2px rgba(16,24,40,.04)' : 'none',
            '&:hover': { borderColor: t.lineStrong, backgroundColor: t.subtle },
          },
          outlinedInherit: { borderColor: t.line },
          text: { '&:hover': { backgroundColor: alpha(t.ink, 0.05) } },
        },
      },
      MuiIconButton: {
        styleOverrides: {
          root: { borderRadius: 8, transition: 'background-color 150ms ease', '&:hover': { backgroundColor: alpha(t.ink, 0.06) }, '&.Mui-focusVisible': focusRing },
        },
      },
      MuiPaper: { defaultProps: { elevation: 0 }, styleOverrides: { root: { backgroundImage: 'none' }, outlined: { borderColor: t.line } } },
      MuiCard: {
        defaultProps: { variant: 'outlined' },
        styleOverrides: { root: { borderRadius: 12, borderColor: t.line, boxShadow: mode === 'light' ? '0 1px 2px rgba(16,24,40,.04)' : 'none' } },
      },
      MuiChip: {
        styleOverrides: {
          root: { fontWeight: 600, borderRadius: 6, fontSize: '0.75rem' },
          sizeSmall: { height: 24 },
          outlined: { borderColor: t.line, backgroundColor: t.surface },
          filled: { '&.MuiChip-colorDefault': { backgroundColor: t.subtle, color: t.ink2 } },
        },
      },
      MuiTextField: { defaultProps: { size: 'small', fullWidth: true } },
      MuiSelect: { defaultProps: { size: 'small' } },
      MuiInputLabel: { styleOverrides: { root: { '&.Mui-focused:not(.Mui-error)': { color: t.ink } } } },
      MuiFormHelperText: { styleOverrides: { root: { marginInline: 2, marginTop: 6 } } },
      MuiOutlinedInput: {
        styleOverrides: {
          root: {
            backgroundColor: t.surface,
            borderRadius: 8,
            transition: 'box-shadow 150ms ease',
            '& .MuiOutlinedInput-notchedOutline': { borderColor: t.line, transition: 'border-color 150ms ease' },
            '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: t.lineStrong },
            '&.Mui-focused': { boxShadow: `0 0 0 3px ${ring}` },
            '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: accentMain, borderWidth: 1 },
            '&.Mui-error.Mui-focused': { boxShadow: `0 0 0 3px ${alpha(errorMain, 0.2)}` },
            '&.Mui-error.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: errorMain },
            '&.Mui-disabled': { backgroundColor: t.subtle },
          },
          input: { '&::placeholder': { color: t.faint, opacity: 1 } },
        },
      },
      // Segmented control look: a tinted track with the selected option raised.
      MuiToggleButtonGroup: {
        styleOverrides: {
          root: { backgroundColor: t.subtle, padding: 2, gap: 2, borderRadius: 8 },
          grouped: { border: 0, borderRadius: '6px !important', margin: '0 !important' },
        },
      },
      MuiToggleButton: {
        styleOverrides: {
          root: {
            border: 0, textTransform: 'none', fontWeight: 600, color: t.muted, paddingBlock: 5,
            '&:hover': { backgroundColor: alpha(t.ink, 0.04) },
            '&.Mui-selected': { backgroundColor: t.surface, color: t.ink, boxShadow: shXs, '&:hover': { backgroundColor: t.surface } },
            '&.Mui-focusVisible': focusRing,
          },
        },
      },
      MuiSwitch: {
        defaultProps: { disableRipple: true },
        styleOverrides: {
          root: { width: 40, height: 24, padding: 0, margin: 8 },
          switchBase: {
            padding: 3,
            '&.Mui-checked': { transform: 'translateX(16px)', color: '#FFFFFF', '& + .MuiSwitch-track': { backgroundColor: successMain, opacity: 1 } },
            '&.Mui-focusVisible + .MuiSwitch-track': focusRing,
            '&.Mui-disabled + .MuiSwitch-track': { opacity: 0.4 },
          },
          thumb: { width: 18, height: 18, boxShadow: '0 1px 2px rgba(16,24,40,.2)', color: '#FFFFFF' },
          track: { borderRadius: 12, backgroundColor: t.lineStrong, opacity: 1, transition: 'background-color 150ms ease' },
        },
      },
      MuiCheckbox: { styleOverrides: { root: { color: t.lineStrong, '&.Mui-checked': { color: accentMain } } } },
      MuiRadio: { styleOverrides: { root: { color: t.lineStrong, '&.Mui-checked': { color: accentMain } } } },
      MuiAlert: {
        styleOverrides: {
          root: { borderRadius: 10, border: '1px solid', fontSize: '0.875rem' },
          standardSuccess: { borderColor: alpha(successMain, 0.3) },
          standardError: { borderColor: alpha(errorMain, 0.3) },
          standardWarning: { borderColor: alpha('#F79009', 0.35) },
          standardInfo: { borderColor: alpha('#2E90FA', 0.3) },
        },
      },
      MuiPaginationItem: {
        styleOverrides: {
          root: {
            borderRadius: 8, fontWeight: 600,
            '&.Mui-selected': { backgroundColor: t.inverse, color: t.onInverse, '&:hover': { backgroundColor: alpha(t.inverse, 0.88) } },
          },
          outlined: { borderColor: t.line },
        },
      },
      MuiTooltip: {
        defaultProps: { arrow: true, enterDelay: 300 },
        styleOverrides: {
          tooltip: { backgroundColor: tooltipBg, color: '#FFFFFF', fontSize: 12, fontWeight: 500, borderRadius: 6, padding: '6px 10px' },
          arrow: { color: tooltipBg },
        },
      },
      MuiBackdrop: {
        styleOverrides: { root: { '&:not(.MuiBackdrop-invisible)': { backgroundColor: alpha('#0B1220', mode === 'dark' ? 0.7 : 0.45), backdropFilter: 'blur(2px)' } } },
      },
      MuiDialog: { styleOverrides: { paper: { borderRadius: 14, border: `1px solid ${t.line}`, boxShadow: shLg } } },
      MuiDialogTitle: { styleOverrides: { root: { fontSize: '1.0625rem', fontWeight: 700, letterSpacing: '-0.01em' } } },
      MuiDrawer: { styleOverrides: { paper: { borderColor: t.line } } },
      MuiMenu: { styleOverrides: { paper: { border: `1px solid ${t.line}`, boxShadow: shMd }, list: { padding: 4 } } },
      MuiMenuItem: {
        styleOverrides: {
          root: {
            borderRadius: 6, fontSize: '0.875rem', minHeight: 36,
            '&.Mui-selected': { backgroundColor: alpha(accentMain, 0.14), '&:hover': { backgroundColor: alpha(accentMain, 0.2) } },
          },
        },
      },
      MuiListItemIcon: { styleOverrides: { root: { color: t.muted } } },
      MuiPopover: { styleOverrides: { paper: { border: `1px solid ${t.line}`, boxShadow: shMd } } },
      MuiAutocomplete: { styleOverrides: { paper: { border: `1px solid ${t.line}`, boxShadow: shMd }, option: { fontSize: '0.875rem' } } },
      MuiBadge: { styleOverrides: { badge: { fontWeight: 700, fontSize: 10.5, minWidth: 18, height: 18, padding: '0 5px' } } },
      MuiTab: { styleOverrides: { root: { textTransform: 'none', fontWeight: 600, minHeight: 44, color: t.muted, '&.Mui-selected': { color: t.ink } } } },
      MuiTabs: { styleOverrides: { root: { minHeight: 44 }, indicator: { backgroundColor: accentMain, height: 2, borderRadius: 2 } } },
      MuiTableCell: {
        styleOverrides: {
          head: { fontWeight: 600, color: t.muted, fontSize: 12, textTransform: 'uppercase', letterSpacing: '0.04em', backgroundColor: t.subtle },
          root: { borderColor: t.line },
        },
      },
      MuiTableRow: { styleOverrides: { root: { '&.MuiTableRow-hover:hover': { backgroundColor: alpha(t.ink, 0.025) } } } },
      MuiListItemButton: {
        styleOverrides: {
          root: {
            borderRadius: 8,
            '&.Mui-selected': { backgroundColor: alpha(accentMain, 0.14), '&:hover': { backgroundColor: alpha(accentMain, 0.2) } },
          },
        },
      },
      MuiLinearProgress: { styleOverrides: { root: { backgroundColor: t.subtle, borderRadius: 4 }, bar: { borderRadius: 4 } } },
      MuiSkeleton: { defaultProps: { animation: 'wave' }, styleOverrides: { root: { backgroundColor: alpha(t.ink, mode === 'dark' ? 0.07 : 0.06) } } },
    },
  });
}
