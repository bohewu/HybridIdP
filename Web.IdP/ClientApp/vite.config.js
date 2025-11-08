import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  build: {
    manifest: true,
    outDir: '../wwwroot/dist',
    emptyOutDir: true,
    rollupOptions: {
      // Multi-Page Application setup with backend routes for security
      // Each admin feature gets its own entry point loaded by a separate Razor Page
      input: {
        'admin-shared': './src/admin/shared/main.js',
        'admin-dashboard': './src/admin/dashboard/main.js',
        'admin-clients': './src/admin/clients/main.js',
        'admin-scopes': './src/admin/scopes/main.js',
        'admin-claims': './src/admin/claims/main.js',
        'admin-users': './src/admin/users/main.js',
        'admin-roles': './src/admin/roles/main.js',
        'admin-settings': './src/admin/settings/main.js'
      }
    }
  },
  server: {
    port: 5173,
    strictPort: true,
    https: false,
    hmr: {
      protocol: 'ws',
      host: 'localhost',
      port: 5173
    }
  }
})
