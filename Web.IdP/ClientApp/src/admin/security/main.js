import { createApp } from 'vue'
import './style.css'
import SecurityApp from './SecurityApp.vue'
import i18n from '@/i18n'
import vLoading from '@/directives/v-loading'

const app = createApp(SecurityApp)
app.use(i18n)
app.directive('loading', vLoading)
app.mount('#admin-security-app')
