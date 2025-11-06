/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{vue,js,ts,jsx,tsx}",
  ],
  theme: {
    // Match Bootstrap 5 breakpoints for consistency
    screens: {
      'sm': '576px',   // Bootstrap: @media (min-width: 576px)
      'md': '768px',   // Bootstrap: @media (min-width: 768px)
      'lg': '992px',   // Bootstrap: @media (min-width: 992px)
      'xl': '1200px',  // Bootstrap: @media (min-width: 1200px)
      '2xl': '1400px', // Bootstrap: @media (min-width: 1400px)
    },
    extend: {
      // Additional z-index values to match admin layout
      zIndex: {
        '30': '30',  // top-header
        '35': '35',  // mobile-overlay
        '40': '40',  // sidebar
        '50': '50',  // Vue modals
      },
      // Match the sidebar width
      spacing: {
        'sidebar': '260px',
        'sidebar-md': '220px',
      },
    },
  },
  plugins: [],
}
