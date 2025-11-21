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

const i18n = createI18n({
  legacy: false,
  locale: getInitialLocale(),
  fallbackLocale: 'en-US',
  messages: {
    'zh-TW': JSON.parse(zhTWRaw),
    'en-US': JSON.parse(enUSRaw)
  }
})
// Phase 7.3: Added login history i18n keys

export default i18n
