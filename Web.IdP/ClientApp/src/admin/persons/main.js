import { createApp } from 'vue'
import PersonsApp from './PersonsApp.vue'
import './style.css'
import i18n from '@/i18n'
import vLoading from '@/directives/v-loading'

const app = createApp(PersonsApp)
app.use(i18n)
app.directive('loading', vLoading)
app.mount('#app')
