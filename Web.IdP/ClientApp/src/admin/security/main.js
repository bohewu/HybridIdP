import { createApp } from 'vue'
import './style.css'
import SecurityApp from './SecurityApp.vue'
import i18n from '@/i18n'

const app = createApp(SecurityApp)
app.use(i18n)
app.mount('#admin-security-app')
