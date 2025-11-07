import { createApp } from 'vue'
import UsersApp from './UsersApp.vue'
import './style.css'
import i18n from '@/i18n'

const app = createApp(UsersApp)
app.use(i18n)
app.mount('#app')
