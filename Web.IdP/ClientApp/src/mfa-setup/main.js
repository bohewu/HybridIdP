import { createApp } from 'vue'
import './style.css'
import MfaSetupApp from './MfaSetupApp.vue'
import i18n from '../i18n'
import vLoading from '../directives/v-loading'

const app = createApp(MfaSetupApp)
app.use(i18n)
app.directive('loading', vLoading)
app.mount('#mfa-setup-app')
