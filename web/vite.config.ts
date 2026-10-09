import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import basicSsl from '@vitejs/plugin-basic-ssl';
import { fileURLToPath, URL } from 'node:url';

const api = process.env.VITE_API_PROXY ?? 'http://localhost:5080';

// `npm run dev:lan`: served over HTTPS on the local network (https://<this PC's IP>:5173), so phones and other devices on the same
// Wi-Fi get the camera, microphone and location (browsers only allow them on https:// or localhost). The certificate is self-signed:
// accept the browser's warning once on each device.
export default defineConfig(({ mode }) => ({
  plugins: [react(), tailwindcss(), ...(mode === 'lan' ? [basicSsl({ name: 'callingbell-dev' })] : [])],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  // Identifies this build: the browser's saved page data (src/lib/queryCache.ts) is dropped when it changes.
  define: { 'import.meta.env.VITE_BUILD_ID': JSON.stringify(String(Date.now())) },
  server: {
    port: 5173,
    host: mode === 'lan' ? true : undefined,
    // HTTPS tunnels for testing on other devices (camera, microphone and location need HTTPS): VS Code port forwarding, Cloudflare.
    allowedHosts: ['.devtunnels.ms', '.trycloudflare.com'],
    // Transform the shell and the most visited pages as soon as the dev server starts, so the first page load doesn't wait for it.
    warmup: {
      clientFiles: [
        './src/main.tsx', './src/app/router.tsx', './src/layouts/CustomerLayout.tsx', './src/features/home/HomePage.tsx',
        './src/features/search/SearchPage.tsx', './src/features/business/BusinessPage.tsx', './src/features/categories/CategoriesPage.tsx',
      ],
    },
    proxy: {
      '/api': { target: api, changeOrigin: true },
      '/hubs': { target: api, changeOrigin: true, ws: true },
      // Crawler files come from the API (built from the database), as in production.
      '/robots.txt': { target: api, changeOrigin: true },
      '^/sitemap[\w-]*\.xml$': { target: api, changeOrigin: true },
    },
  },
  build: {
    chunkSizeWarningLimit: 1500,
    rollupOptions: {
      output: {
        // Long-lived vendor chunks, matched by package path so internal files (react-dom's client build, scheduler, @mui/system)
        // land with their package: an app deploy then doesn't make returning visitors re-download them.
        manualChunks(id) {
          if (!id.includes('node_modules')) return undefined;
          if (/node_modules[\\/](react|react-dom|react-router|scheduler)[\\/]/.test(id)) return 'react';
          if (/node_modules[\\/](@mui[\\/](material|system|utils|styled-engine|private-theming|core-downloads-tracker)|@emotion|@popperjs|react-transition-group|stylis)[\\/]/.test(id)) return 'mui';
          if (/node_modules[\\/](@tanstack|zustand|notistack)[\\/]/.test(id)) return 'data';
          if (/node_modules[\\/](recharts|d3-|victory-vendor|recharts-scale)/.test(id)) return 'charts';
          if (/node_modules[\\/]ag-grid/.test(id)) return 'grid';
          return undefined;
        },
      },
    },
  },
}));
