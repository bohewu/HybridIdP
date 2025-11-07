import { createI18n } from 'vue-i18n'
import zhTW from './locales/zh-TW.json'
import enUS from './locales/en-US.json'

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
    'zh-TW': zhTW,
    'en-US': enUS
  }
})

export default i18n
