import { createApp } from 'vue'
import UsersApp from './UsersApp.vue'
import './style.css'
import i18n from '@/i18n'
import vLoading from '@/directives/v-loading'

const app = createApp(UsersApp)
app.use(i18n)
app.directive('loading', vLoading)
app.mount('#admin-users-app')
