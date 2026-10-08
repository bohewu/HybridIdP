export function countConditionNodes(rule, depth = 1) {
  if (!rule || typeof rule !== 'object' || Array.isArray(rule) || depth > 4) return 33
  if (['All', 'Any'].includes(rule.operator)) {
    if (!Array.isArray(rule.children) || rule.children.length < 1 || rule.children.length > 8) return 33
    return 1 + rule.children.reduce((count, child) => count + countConditionNodes(child, depth + 1), 0)
  }
  return ['Equals', 'NotEquals', 'StartsWith', 'Contains'].includes(rule.operator) ? 1 : 33
}
