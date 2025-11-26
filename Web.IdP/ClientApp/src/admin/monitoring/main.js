import { createApp } from 'vue'
import MonitoringApp from './MonitoringApp.vue'
import './style.css'
import i18n from '@/i18n'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'

const app = createApp(MonitoringApp)
// register shared components used by monitoring children
app.component('LoadingIndicator', LoadingIndicator)
app.use(i18n)
app.mount('#app')