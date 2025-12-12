/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/profile/**/*.{vue,js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {},
  },
  corePlugins: {
    preflight: false, // CRITICAL: Disable preflight to prevent resetting Bootstrap styles
  },
  plugins: [
    require('@tailwindcss/forms'),
  ],
}
