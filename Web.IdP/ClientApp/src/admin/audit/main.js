import { createApp } from 'vue'
import AuditApp from './AuditApp.vue'
import './style.css'
import i18n from '@/i18n'
import vLoading from '@/directives/v-loading'

const app = createApp(AuditApp)
app.use(i18n)
app.directive('loading', vLoading)
app.mount('#app')