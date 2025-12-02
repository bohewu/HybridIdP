import { createApp } from 'vue';
import i18n from './i18n';
import AccountManagementApp from './AccountManagementApp.vue';

const app = createApp(AccountManagementApp);
app.use(i18n);
app.mount('#account-management-app');
