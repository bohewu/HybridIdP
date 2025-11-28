import { createApp } from 'vue'
import i18n from '@/i18n'
import vLoading from '@/directives/v-loading'
import SettingsApp from './SettingsApp.vue'
import './style.css'

const app = createApp(SettingsApp)
app.use(i18n)
app.directive('loading', vLoading)
app.mount('#app')
