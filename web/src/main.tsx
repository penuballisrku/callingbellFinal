import { StrictMode, useMemo, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CssBaseline, GlobalStyles, StyledEngineProvider, ThemeProvider } from '@mui/material';
import { SnackbarProvider } from 'notistack';
import '@fontsource-variable/inter';
import './index.css';
import { buildTheme } from './app/theme';
import { router } from './app/router';
import { ApiError } from './lib/api';
import { useApplyTheme } from './stores/theme';

function AppTheme({ children }: { children: ReactNode }) {
  const { resolved, accent } = useApplyTheme();
  const theme = useMemo(() => buildTheme(resolved, accent), [resolved, accent]);
  return <ThemeProvider theme={theme}><CssBaseline enableColorScheme />{children}</ThemeProvider>;
}

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 60_000,
      refetchOnWindowFocus: false,
      retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
    },
  },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <StyledEngineProvider enableCssLayer>
      <GlobalStyles styles="@layer theme, base, mui, components, utilities;" />
      <AppTheme>
        <QueryClientProvider client={queryClient}>
          <SnackbarProvider maxSnack={3} autoHideDuration={4000} anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}>
            <RouterProvider router={router} />
          </SnackbarProvider>
        </QueryClientProvider>
      </AppTheme>
    </StyledEngineProvider>
  </StrictMode>,
);
