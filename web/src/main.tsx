import { StrictMode, useMemo, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router';
import { PersistQueryClientProvider } from '@tanstack/react-query-persist-client';
import { CssBaseline, GlobalStyles, StyledEngineProvider, ThemeProvider } from '@mui/material';
import { SnackbarProvider } from 'notistack';
import '@fontsource-variable/inter';
import './index.css';
import { buildTheme } from './app/theme';
import { router } from './app/router';
import { persistOptions, preloadMainPages, queryClient } from './lib/queryCache';
import { useApplyTheme } from './stores/theme';

function AppTheme({ children }: { children: ReactNode }) {
  const { resolved, accent } = useApplyTheme();
  const theme = useMemo(() => buildTheme(resolved, accent), [resolved, accent]);
  return <ThemeProvider theme={theme}><CssBaseline enableColorScheme />{children}</ThemeProvider>;
}

preloadMainPages();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <StyledEngineProvider enableCssLayer>
      <GlobalStyles styles="@layer theme, base, mui, components, utilities;" />
      <AppTheme>
        {/* Public pages are saved in the browser and shown instantly on the next visit, then refreshed in the background. */}
        <PersistQueryClientProvider client={queryClient} persistOptions={persistOptions}>
          <SnackbarProvider maxSnack={3} autoHideDuration={4000} anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}>
            <RouterProvider router={router} />
          </SnackbarProvider>
        </PersistQueryClientProvider>
      </AppTheme>
    </StyledEngineProvider>
  </StrictMode>,
);
