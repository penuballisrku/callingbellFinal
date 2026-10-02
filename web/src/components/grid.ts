import { useMemo } from 'react';
import { AllCommunityModule, ModuleRegistry, themeQuartz } from 'ag-grid-community';
import { tokens } from '@/app/theme';
import { accents, useResolvedMode, useThemeSettings } from '@/stores/theme';

ModuleRegistry.registerModules([AllCommunityModule]);

/** AG Grid theme built from the same design tokens as the rest of the UI; follows light/dark mode and colour theme. */
export function useGridTheme() {
  const mode = useResolvedMode();
  const accentKey = useThemeSettings((s) => s.accent);
  return useMemo(() => {
    const t = tokens[mode];
    const accent = accents.find((a) => a.key === accentKey) ?? accents[0]!;
    return themeQuartz.withParams({
      browserColorScheme: mode,
      fontFamily: "'Inter Variable', 'Segoe UI', sans-serif",
      fontSize: 13,
      headerFontSize: 12,
      headerFontWeight: 600,
      backgroundColor: t.surface,
      foregroundColor: t.ink,
      headerTextColor: t.muted,
      headerBackgroundColor: t.subtle,
      borderColor: t.line,
      rowBorder: { color: t.line },
      wrapperBorder: false,
      wrapperBorderRadius: 0,
      accentColor: mode === 'dark' ? accent.dark : accent.light,
      rowHoverColor: t.subtle,
      spacing: 7,
      rowHeight: 56,
    });
  }, [mode, accentKey]);
}
