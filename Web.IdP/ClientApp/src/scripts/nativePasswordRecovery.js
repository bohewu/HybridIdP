const anyCodeUnit = (value, pattern) => value.split('').some(character => pattern.test(character))
const hasUppercase = value => anyCodeUnit(value, /\p{Lu}/u)
const hasLowercase = value => anyCodeUnit(value, /\p{Ll}/u)
const hasDigit = value => anyCodeUnit(value, /\p{Nd}/u)
const hasSymbol = value => anyCodeUnit(value, /[^\p{L}\p{Nd}]/u)

function evaluateRule(name, requiredValue, password, confirmation) {
  switch (name) {
    case 'minimum-length':
      return password.length >= requiredValue
    case 'uppercase':
      return hasUppercase(password)
    case 'lowercase':
      return hasLowercase(password)
    case 'digit':
      return hasDigit(password)
    case 'symbol':
      return hasSymbol(password)
    case 'character-types': {
      const characterTypes = [
        hasUppercase(password),
        hasLowercase(password),
        hasDigit(password),
        hasSymbol(password)
      ].filter(Boolean).length
      return characterTypes >= requiredValue
    }
    case 'confirmation':
      return password.length > 0 && confirmation.length > 0 && password === confirmation
    default:
      return false
  }
}

export function initNativePasswordPolicy(root = document) {
  const recoveryRoot = root.querySelector('[data-native-recovery]')
  const password = recoveryRoot?.querySelector('[data-native-password]')
  const confirmation = recoveryRoot?.querySelector('[data-native-password-confirm]')
  const policy = recoveryRoot?.querySelector('[data-native-password-policy]')
  if (!password || !confirmation || !policy) return null

  const rules = [...policy.querySelectorAll('[data-native-password-rule]')]
  const progress = policy.querySelector('[data-native-password-progress]')
  const progressText = policy.querySelector('[data-native-password-progress-text]')
  const progressTemplate = policy.dataset.progressTemplate || '{valid} of {total} requirements met'

  const render = () => {
    let validCount = 0
    rules.forEach(rule => {
      const requiredValue = Number.parseInt(rule.dataset.requiredValue || '0', 10)
      const valid = evaluateRule(
        rule.dataset.nativePasswordRule,
        requiredValue,
        password.value,
        confirmation.value
      )
      rule.dataset.ruleValid = String(valid)
      rule.classList.toggle('text-green-600', valid)
      rule.classList.toggle('text-gray-500', !valid)
      const icon = rule.querySelector('[data-rule-icon]')
      if (icon) icon.textContent = valid ? '✓' : '○'
      if (valid) validCount += 1
    })

    if (progress) {
      progress.max = rules.length
      progress.value = validCount
    }
    if (progressText) {
      progressText.textContent = progressTemplate
        .replace('{valid}', String(validCount))
        .replace('{total}', String(rules.length))
    }
  }

  password.addEventListener('input', render)
  confirmation.addEventListener('input', render)
  render()
  return { render }
}
