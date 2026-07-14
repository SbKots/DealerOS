import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { loadEnv } from 'vite'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  return {
    base: env.VITE_BASE_PATH || '/',
    plugins: [react()],
    server: { proxy: { '/api': 'http://localhost:5080', '/health': 'http://localhost:5080' } },
    test: { environment: 'jsdom', setupFiles: './src/test/setup.ts', css: true, exclude: ['e2e/**', 'node_modules/**'] },
  }
})
