import { createApp } from 'vue'
import './style.css'
import RolesApp from './RolesApp.vue'
import i18n from '@/i18n'

const app = createApp(RolesApp)
app.use(i18n)
app.mount('#app')
