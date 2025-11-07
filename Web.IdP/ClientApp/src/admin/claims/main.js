import { createApp } from 'vue'
import ClaimsApp from './ClaimsApp.vue'
import './style.css'
import i18n from '@/i18n'

const app = createApp(ClaimsApp)
app.use(i18n)
app.mount('#app')
