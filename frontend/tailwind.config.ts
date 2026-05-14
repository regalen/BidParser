import type { Config } from 'tailwindcss';

export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        ink: {
          DEFAULT: '#2a2a2a',
          soft: '#4a4a4a',
          mute: '#8a8a8a',
          faint: '#c8c8c8',
        },
        paper: {
          DEFAULT: '#fdfcf8',
          tint: '#f4f1ea',
        },
        accent: {
          DEFAULT: '#0077d4',
          soft: '#e6f1fb',
        },
      },
      fontFamily: {
        ui: ['Inter', 'system-ui', 'sans-serif'],
      },
      boxShadow: {
        panel: '0 18px 60px rgba(42, 42, 42, 0.08)',
      },
    },
  },
  plugins: [],
} satisfies Config;
