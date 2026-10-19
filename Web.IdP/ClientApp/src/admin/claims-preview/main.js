import { createApp } from 'vue'
import ClaimsPreviewApp from './ClaimsPreviewApp.vue'
import i18n from '@/i18n'
import '../claims/style.css'

createApp(ClaimsPreviewApp).use(i18n).mount('#app')
