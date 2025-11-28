import { createApp } from 'vue'
import ResourcesApp from './ResourcesApp.vue'
import './style.css'
import i18n from '@/i18n'
import vLoading from '@/directives/v-loading'

const app = createApp(ResourcesApp)
app.use(i18n)
app.directive('loading', vLoading)
app.mount('#app')
