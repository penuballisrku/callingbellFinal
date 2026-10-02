import { createTheme, alpha, type Theme } from '@mui/material/styles';
import { accents, type AccentTheme } from '@/stores/theme';

type Mode = 'light' | 'dark';

/** Token values per mode - mirror of the CSS variables in index.css (MUI needs concrete colours for alpha()). */
export const tokens = {
  light: { canvas: '#F7F8FA', surface: '#FFFFFF', subtle: '#F2F4F7', ink: '#161616', ink2: '#344054', muted: '#667085', faint: '#98A2B3', line: '#E5E7EB', lineStrong: '#CBD2DC', inverse: '#0B1220', onInverse: '#FFFFFF' },
  dark: { canvas: '#0A0E15', surface: '#111723', subtle: '#1A2130', ink: '#F2F4F7', ink2: '#CDD5DF', muted: '#98A2B3', faint: '#6B778A', line: '#232C3B', lineStrong: '#364256', inverse: '#F2F4F7', onInverse: '#0B1220' },
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
  return { series: seriesByMode[mode], grid: mode === 'dark' ? '#1F2735' : '#EEF0F3', axis: t.faint, track: t.subtle, cursor: t.subtle };
}

export function buildTheme(mode: Mode, accentKey: AccentTheme): Theme {
  const t = tokens[mode];
  const accent = accents.find((a) => a.key === accentKey) ?? accents[0]!;
  const accentMain = mode === 'dark' ? accent.dark : accent.light;
  const onAccent = accentKey === 'amber' || mode === 'dark' ? '#0B1220' : '#FFFFFF';

  return createTheme({
    palette: {
      mode,
      primary: { main: t.inverse, contrastText: t.onInverse },
      secondary: { main: accentMain, contrastText: onAccent },
      background: { default: t.canvas, paper: t.surface },
      text: { primary: t.ink, secondary: t.muted, disabled: t.faint },
      divider: t.line,
      success: { main: mode === 'dark' ? '#3CCB7F' : '#12B76A' },
      warning: { main: '#F79009' },
      error: { main: mode === 'dark' ? '#F97066' : '#D92D20' },
      info: { main: '#2E90FA' },
      action: { hover: alpha(t.ink, 0.05), selected: alpha(accentMain, 0.16) },
    },
    shape: { borderRadius: 10 },
    typography: {
      fontFamily: "'Inter Variable', 'Segoe UI', Roboto, Helvetica, Arial, sans-serif",
      h1: { fontWeight: 700, letterSpacing: '-0.02em' },
      h2: { fontWeight: 700, letterSpacing: '-0.02em' },
      h3: { fontWeight: 700, letterSpacing: '-0.015em' },
      h4: { fontWeight: 700, letterSpacing: '-0.01em', fontSize: '1.75rem' },
      h5: { fontWeight: 650, fontSize: '1.25rem' },
      h6: { fontWeight: 650, fontSize: '1.05rem' },
      subtitle1: { fontWeight: 600 },
      subtitle2: { fontWeight: 600 },
      button: { fontWeight: 600, textTransform: 'none', letterSpacing: 0 },
    },
    components: {
      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          root: { borderRadius: 8, paddingInline: 16 },
          sizeLarge: { paddingBlock: 10, fontSize: '0.975rem' },
          outlined: { borderColor: t.line, '&:hover': { borderColor: t.lineStrong, backgroundColor: t.subtle } },
          outlinedInherit: { borderColor: t.line },
        },
      },
      MuiPaper: { defaultProps: { elevation: 0 }, styleOverrides: { root: { backgroundImage: 'none' }, outlined: { borderColor: t.line } } },
      MuiCard: { defaultProps: { variant: 'outlined' }, styleOverrides: { root: { borderRadius: 12, borderColor: t.line } } },
      MuiChip: { styleOverrides: { root: { fontWeight: 600, borderRadius: 6 }, sizeSmall: { height: 24 }, outlined: { borderColor: t.line } } },
      MuiTextField: { defaultProps: { size: 'small', fullWidth: true } },
      MuiSelect: { defaultProps: { size: 'small' } },
      MuiOutlinedInput: {
        styleOverrides: {
          root: {
            backgroundColor: t.surface,
            '& .MuiOutlinedInput-notchedOutline': { borderColor: t.line },
            '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: t.lineStrong },
          },
        },
      },
      MuiToggleButton: { styleOverrides: { root: { borderColor: t.line, textTransform: 'none', fontWeight: 600 } } },
      MuiTooltip: {
        defaultProps: { arrow: true },
        styleOverrides: { tooltip: { backgroundColor: t.inverse, color: t.onInverse, fontSize: 12 }, arrow: { color: t.inverse } },
      },
      MuiDialog: { styleOverrides: { paper: { borderRadius: 14, border: mode === 'dark' ? `1px solid ${t.line}` : undefined } } },
      MuiMenu: { styleOverrides: { paper: { border: `1px solid ${t.line}` } } },
      MuiPopover: { styleOverrides: { paper: { border: `1px solid ${t.line}` } } },
      MuiTab: { styleOverrides: { root: { textTransform: 'none', fontWeight: 600, minHeight: 44 } } },
      MuiTabs: { styleOverrides: { root: { minHeight: 44 }, indicator: { backgroundColor: accentMain, height: 3, borderRadius: 3 } } },
      MuiTableCell: {
        styleOverrides: {
          head: { fontWeight: 600, color: t.muted, fontSize: 12, textTransform: 'uppercase', letterSpacing: '0.04em', backgroundColor: t.subtle },
          root: { borderColor: t.line },
        },
      },
      MuiListItemButton: {
        styleOverrides: {
          root: {
            borderRadius: 8,
            '&.Mui-selected': { backgroundColor: alpha(accentMain, 0.14), '&:hover': { backgroundColor: alpha(accentMain, 0.2) } },
          },
        },
      },
      MuiLinearProgress: { styleOverrides: { root: { backgroundColor: t.subtle } } },
      MuiSkeleton: { defaultProps: { animation: 'wave' }, styleOverrides: { root: { backgroundColor: alpha(t.ink, mode === 'dark' ? 0.08 : 0.07) } } },
    },
  });
}
