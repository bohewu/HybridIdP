module.exports = {
  root: true,
  extends: [
    'plugin:vue/vue3-recommended',
    'eslint:recommended'
  ],
  parserOptions: {
    ecmaVersion: 'latest'
  },
  rules: {
    // Custom rule: Prevent uppercase Permissions usage
    'no-restricted-syntax': [
      'error',
      {
        selector: 'MemberExpression[object.name="Permissions"][property.name=/^[A-Z]+$/]',
        message: 'Use PascalCase for Permissions (e.g., Permissions.Clients.Read, not Permissions.Clients.READ)'
      }
    ]
  }
}
