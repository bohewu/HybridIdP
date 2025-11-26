import { createApp } from 'vue'
import DashboardApp from './DashboardApp.vue'
import i18n from '@/i18n'
import '../style.css'
import vLoading from '@/directives/v-loading'

const app = createApp(DashboardApp)
app.use(i18n)
app.directive('loading', vLoading)
app.mount('#app')
