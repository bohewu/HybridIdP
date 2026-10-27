import { createApp } from 'vue'
import SamlAttributesApp from './SamlAttributesApp.vue'
import i18n from '@/i18n'
import '../claims/style.css'
createApp(SamlAttributesApp).use(i18n).mount('#app')
