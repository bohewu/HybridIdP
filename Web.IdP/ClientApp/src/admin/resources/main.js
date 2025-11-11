import { createApp } from 'vue'
import ResourcesApp from './ResourcesApp.vue'
import './style.css'
import i18n from '@/i18n'

const app = createApp(ResourcesApp)
app.use(i18n)
app.mount('#app')
