import { createApp } from 'vue'
import './style.css'
import ProfileApp from './ProfileApp.vue'
import i18n from '../i18n'
import vLoading from '../directives/v-loading'

const app = createApp(ProfileApp)
app.use(i18n)
app.directive('loading', vLoading)
app.mount('#profile-app')
