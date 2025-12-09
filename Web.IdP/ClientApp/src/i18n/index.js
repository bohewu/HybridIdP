import { createI18n } from 'vue-i18n'
import zhTWRaw from './locales/zh-TW.json?raw'
import enUSRaw from './locales/en-US.json?raw'

// Get initial locale from meta tag or cookie or default to zh-TW
function getInitialLocale() {
  // Try meta tag first
  const metaLocale = document.querySelector('meta[name="locale"]')
  if (metaLocale && metaLocale.content) {
    return metaLocale.content
  }
  
  // Fallback to cookie
  const cookieMatch = document.cookie.match(/\.AspNetCore\.Culture=c=([^|;]+)/)
  if (cookieMatch && cookieMatch[1]) {
    return cookieMatch[1]
  }
  
  return 'zh-TW'
}

const legacyZhTw = JSON.parse(zhTWRaw)
const legacyEnUs = JSON.parse(enUSRaw)

// Load feature-based modules
const zhTwModules = import.meta.glob('./locales/zh-TW/*.json', { eager: true })
const enUsModules = import.meta.glob('./locales/en-US/*.json', { eager: true })

function mergeModules(legacy, modules) {
  const messages = { ...legacy }
  
  for (const path in modules) {
    const moduleName = path.split('/').pop().replace('.json', '')
    // If the file is common.json, merge at root level (or we can keep it strict)
    // But for now, let's namespace everything by filename to avoid collisions
    // EXCEPT common.json if we decide to have one.
    // So messages['users'] = content of users.json
    messages[moduleName] = modules[path].default || modules[path]
  }
  return messages
}

const i18n = createI18n({
  legacy: false,
  locale: getInitialLocale(),
  fallbackLocale: 'en-US',
  messages: {
    'zh-TW': mergeModules(legacyZhTw, zhTwModules),
    'en-US': mergeModules(legacyEnUs, enUsModules)
  }
})
// Phase 7.3: Added login history i18n keys

export default i18n
