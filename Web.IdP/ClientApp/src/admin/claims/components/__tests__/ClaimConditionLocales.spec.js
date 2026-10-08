import { describe, it, expect } from 'vitest'
import { createI18n } from 'vue-i18n'
import en from '@/i18n/locales/en-US/claims.json'
import zh from '@/i18n/locales/zh-TW/claims.json'

describe('Claim condition translations', () => {
  it.each([['en-US', en], ['zh-TW', zh]])('compiles the actual %s help and operator messages', (locale, claims) => {
    const i18n = createI18n({ legacy: false, locale, messages: { [locale]: { claims } } })
    expect(() => i18n.global.t('claims.form.conditionHelp')).not.toThrow()
    expect(i18n.global.t('claims.form.conditionHelp')).toContain('Contains')
    expect(i18n.global.t('claims.form.operators.All')).toContain('AND')
    expect(i18n.global.t('claims.form.operators.Any')).toContain('OR')
  })
})
