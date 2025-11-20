import { createApp } from 'vue'
import MonitoringApp from './MonitoringApp.vue'
import './style.css'
import i18n from '@/i18n'

const app = createApp(MonitoringApp)
app.use(i18n)
app.mount('#app')