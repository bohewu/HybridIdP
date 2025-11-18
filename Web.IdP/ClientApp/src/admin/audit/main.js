import { createApp } from 'vue'
import AuditApp from './AuditApp.vue'
import './style.css'
import i18n from '@/i18n'

const app = createApp(AuditApp)
app.use(i18n)
app.mount('#app')