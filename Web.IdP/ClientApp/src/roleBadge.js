import { createApp } from 'vue';
import i18n from './i18n';
import RoleBadge from './components/navigation/RoleBadge.vue';

const app = createApp(RoleBadge);
app.use(i18n);
app.mount('#role-badge-container');
