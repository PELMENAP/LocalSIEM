import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

const api = process.env.LOCALSIEM_API ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: api, changeOrigin: true },
      '/hubs': { target: api, changeOrigin: true, ws: true },
      '/health': { target: api, changeOrigin: true }
    }
  },
  build: {
    outDir: '../src/LocalSIEM.Api/wwwroot',
    emptyOutDir: true,
    chunkSizeWarningLimit: 1500
  }
})
