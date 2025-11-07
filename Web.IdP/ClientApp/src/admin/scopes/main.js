import { createApp } from 'vue'
import ScopesApp from './ScopesApp.vue'
import './style.css'
import i18n from '@/i18n'

const app = createApp(ScopesApp)
app.use(i18n)
app.mount('#app')
