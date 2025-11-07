import { createApp } from 'vue'
import ClientsApp from './ClientsApp.vue'
import './style.css'
import i18n from '@/i18n'

const app = createApp(ClientsApp)
app.use(i18n)
app.mount('#app')
