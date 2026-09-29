import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  base: '/miniapp/',
  build: { outDir: '../wwwroot/miniapp', emptyOutDir: true },
  cacheDir: '.vite-cache',
  server: { proxy: { '/api': 'http://localhost:5179' } }
})
