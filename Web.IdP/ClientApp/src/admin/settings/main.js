import { createApp } from 'vue'
import i18n from '@/i18n'
import SettingsApp from './SettingsApp.vue'
import './style.css'

const app = createApp(SettingsApp)
app.use(i18n)
app.mount('#app')
