import { beforeEach, describe, expect, it } from 'vitest'
import { initNativePasswordPolicy } from '../nativePasswordRecovery.js'

function renderPolicy(rules) {
  document.body.innerHTML = `
    <main data-native-recovery>
      <input data-native-password type="password">
      <input data-native-password-confirm type="password">
      <div data-native-password-policy data-progress-template="{valid} of {total} requirements met">
        ${rules.map(({ name, requiredValue }) => `
          <div data-native-password-rule="${name}"
               data-required-value="${requiredValue ?? ''}"
               data-rule-valid="false">
            <span data-rule-icon></span>
          </div>`).join('')}
        <span data-native-password-progress-text></span>
        <progress data-native-password-progress></progress>
      </div>
    </main>`

  const password = document.querySelector('[data-native-password]')
  const confirmation = document.querySelector('[data-native-password-confirm]')
  const progress = document.querySelector('[data-native-password-progress]')
  const progressText = document.querySelector('[data-native-password-progress-text]')
  return { password, confirmation, progress, progressText }
}

describe('native password recovery policy', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
  })

  it('updates applicable requirements and confirmation from incomplete to complete', () => {
    const controls = renderPolicy([
      { name: 'minimum-length', requiredValue: 8 },
      { name: 'uppercase' },
      { name: 'confirmation' }
    ])
    initNativePasswordPolicy()

    expect(controls.progress.value).toBe(0)
    expect(controls.progressText.textContent).toBe('0 of 3 requirements met')

    controls.password.value = 'Password'
    controls.password.dispatchEvent(new Event('input'))
    expect(controls.progress.value).toBe(2)

    controls.confirmation.value = 'Password'
    controls.confirmation.dispatchEvent(new Event('input'))
    expect(controls.progress.value).toBe(3)
    expect(controls.progressText.textContent).toBe('3 of 3 requirements met')
    expect(document.querySelectorAll('[data-rule-valid="true"]')).toHaveLength(3)
  })

  it('uses Unicode-aware character types and UTF-16 length', () => {
    const controls = renderPolicy([
      { name: 'minimum-length', requiredValue: 5 },
      { name: 'uppercase' },
      { name: 'lowercase' },
      { name: 'digit' },
      { name: 'symbol' },
      { name: 'character-types', requiredValue: 4 },
      { name: 'confirmation' }
    ])
    initNativePasswordPolicy()

    controls.password.value = 'Äß٣!𐐀'
    controls.confirmation.value = 'Äß٣!𐐀'
    controls.password.dispatchEvent(new Event('input'))
    controls.confirmation.dispatchEvent(new Event('input'))

    expect(controls.password.value.length).toBe(6)
    expect(controls.progress.value).toBe(7)
    expect(document.querySelectorAll('[data-rule-valid="true"]')).toHaveLength(7)
  })

  it('evaluates supplementary characters as the server UTF-16 char predicates do', () => {
    const controls = renderPolicy([
      { name: 'uppercase' },
      { name: 'symbol' }
    ])
    initNativePasswordPolicy()

    controls.password.value = '𐐀'
    controls.password.dispatchEvent(new Event('input'))

    expect(document.querySelector('[data-native-password-rule="uppercase"]').dataset.ruleValid).toBe('false')
    expect(document.querySelector('[data-native-password-rule="symbol"]').dataset.ruleValid).toBe('true')
  })
})
