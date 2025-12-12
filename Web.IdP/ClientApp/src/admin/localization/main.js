import { createApp } from 'vue'
import LocalizationApp from './LocalizationApp.vue'
import './style.css' // Global Tailwind Import
import i18n from '@/i18n' // Central i18n
import vLoading from '@/directives/v-loading' // Import v-loading directive

const app = createApp(LocalizationApp)

// Register v-loading directive
app.directive('loading', vLoading)

app.use(i18n)
app.mount('#app')
