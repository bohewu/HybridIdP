import { createApp } from 'vue'
import DashboardApp from './DashboardApp.vue'
import i18n from '@/i18n'
import '../style.css'

const app = createApp(DashboardApp)
app.use(i18n)
app.mount('#app')
