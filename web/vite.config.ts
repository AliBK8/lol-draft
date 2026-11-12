import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// Geliştirme: Vite sayfayı kendi sunar, /api ve /hubs istekleri gateway'e gider (DESIGN.md §8.17).
// API gateway'siz isteği reddeder (paylaşılan sır), bu yüzden doğrudan API'ye (5100) değil gateway'e (5000).
const gateway = 'http://localhost:5000'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': gateway,
      // SignalR: önce HTTP negotiate, sonra WebSocket yükseltmesi.
      '/hubs': { target: gateway, ws: true },
    },
  },
  build: {
    // Üretimde gateway'in wwwroot'una kopyalanır (Docker, 6. adım).
    outDir: 'dist',
    // CSP script-src 'self': satır içi script üretilmez (Vite varsayılanı), kaynak haritası yayına girmez.
    sourcemap: false,
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
})
