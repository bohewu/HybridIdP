import { createApp } from 'vue'
import i18n from '@/i18n'
import DataSourcesApp from './DataSourcesApp.vue'
import '../settings/style.css'

createApp(DataSourcesApp).use(i18n).mount('#app')
