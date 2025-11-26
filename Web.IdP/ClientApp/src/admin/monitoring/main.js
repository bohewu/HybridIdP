import { createApp } from 'vue'
import MonitoringApp from './MonitoringApp.vue'
import './style.css'
import i18n from '@/i18n'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'
import vLoading from '@/directives/v-loading'

const app = createApp(MonitoringApp)
// register shared components used by monitoring children
app.component('LoadingIndicator', LoadingIndicator)
app.directive('loading', vLoading)
app.use(i18n)
app.mount('#app')