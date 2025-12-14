import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath, URL } from 'node:url'

// https://vitejs.dev/config/
export default defineConfig(({ command }) => ({
  plugins: [
    vue(),
    tailwindcss(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  base: command === 'build' ? '/dist/' : '/',
  build: {
    manifest: true,
    outDir: '../wwwroot/dist',
    emptyOutDir: true,
    rollupOptions: {
      // Multi-Page Application setup with backend routes for security
      // Each admin feature gets its own entry point loaded by a separate Razor Page
      input: {
        'style': './src/styles/main.css',
        'razor': './src/scripts/razor.js',
        'admin-shared': './src/admin/shared/main.js',
        'admin-dashboard': './src/admin/dashboard/main.js',
        'admin-clients': './src/admin/clients/main.js',
        'admin-scopes': './src/admin/scopes/main.js',
        'admin-claims': './src/admin/claims/main.js',
        'admin-users': './src/admin/users/main.js',
        'admin-roles': './src/admin/roles/main.js',
        'admin-people': './src/admin/persons/main.js',
        'admin-settings': './src/admin/settings/main.js',
        'admin-security': './src/admin/security/main.js',
        'admin-audit': './src/admin/audit/main.js',
        'admin-resources': './src/admin/resources/main.js',
        'admin-monitoring': './src/admin/monitoring/main.js'
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
}))
