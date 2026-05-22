import type { Config } from 'tailwindcss'

const config: Config = {
  content: [
    './src/pages/**/*.{js,ts,jsx,tsx,mdx}',
    './src/components/**/*.{js,ts,jsx,tsx,mdx}',
    './src/app/**/*.{js,ts,jsx,tsx,mdx}',
  ],
  theme: {
    extend: {
      colors: {
        brand: {
          bg: '#08080a',
          surface: '#0d0d10',
          elevated: '#111115',
          hover: '#161619',
          border: 'rgba(201,165,90,0.12)',
        },
        gold: {
          100: '#f0e6cc',
          200: '#e2c890',
          300: '#c9a55a',
          400: '#b08840',
          500: '#8a6a32',
          DEFAULT: '#c9a55a',
          light: '#e2c890',
          dark: '#8a6a32',
          muted: 'rgba(201,165,90,0.07)',
          border: 'rgba(201,165,90,0.14)',
          glow: 'rgba(201,165,90,0.25)',
        },
        ivory: {
          DEFAULT: '#f2eddf',
          dim: 'rgba(242,237,223,0.6)',
          muted: 'rgba(242,237,223,0.3)',
          faint: 'rgba(242,237,223,0.1)',
          ghost: 'rgba(242,237,223,0.05)',
        },
        emerald: {
          DEFAULT: '#1b4d38',
          light: '#2d7556',
          dark: '#0f2d21',
          glow: 'rgba(27,77,56,0.3)',
        },
      },
      fontFamily: {
        display: ['var(--font-bebas)', 'Impact', 'sans-serif'],
        body: ['var(--font-heebo)', 'Heebo', 'Arial', 'sans-serif'],
        heebo: ['var(--font-heebo)', 'Heebo', 'Arial', 'sans-serif'],
      },
      backgroundImage: {
        'hero-radial': 'radial-gradient(ellipse 90% 60% at 50% -10%, rgba(201,165,90,0.10) 0%, transparent 65%)',
        'gold-radial': 'radial-gradient(circle, rgba(201,165,90,0.18) 0%, transparent 65%)',
        'emerald-radial': 'radial-gradient(circle, rgba(27,77,56,0.25) 0%, transparent 65%)',
        'grid-gold': `linear-gradient(rgba(201,165,90,0.04) 1px, transparent 1px), linear-gradient(90deg, rgba(201,165,90,0.04) 1px, transparent 1px)`,
        'gold-shimmer': 'linear-gradient(105deg, transparent 35%, rgba(201,165,90,0.2) 50%, transparent 65%)',
        'card-gradient': 'linear-gradient(145deg, rgba(201,165,90,0.04) 0%, transparent 60%)',
      },
      backgroundSize: {
        'grid-sm': '40px 40px',
        'grid-md': '56px 56px',
        'grid-lg': '80px 80px',
      },
      boxShadow: {
        'card': '0 4px 28px rgba(0,0,0,0.4)',
        'card-hover': '0 16px 56px rgba(0,0,0,0.55), 0 0 0 1px rgba(201,165,90,0.14)',
        'gold-sm': '0 0 20px rgba(201,165,90,0.18)',
        'gold-md': '0 0 40px rgba(201,165,90,0.22), 0 4px 20px rgba(0,0,0,0.4)',
        'gold-lg': '0 0 80px rgba(201,165,90,0.28), 0 8px 40px rgba(0,0,0,0.5)',
        'gold-glow': '0 0 120px rgba(201,165,90,0.35)',
        'luxury': '0 24px 80px rgba(0,0,0,0.55), 0 0 0 1px rgba(201,165,90,0.08), inset 0 1px 0 rgba(201,165,90,0.1)',
        'luxury-lg': '0 40px 120px rgba(0,0,0,0.65), 0 0 0 1px rgba(201,165,90,0.1)',
        'inset-gold': 'inset 0 1px 0 rgba(201,165,90,0.18)',
        'emerald': '0 0 24px rgba(27,77,56,0.3)',
      },
      keyframes: {
        'orb-drift-1': {
          '0%, 100%': { transform: 'translate(0, 0) scale(1)' },
          '30%': { transform: 'translate(80px, -50px) scale(1.08)' },
          '70%': { transform: 'translate(-50px, 30px) scale(0.94)' },
        },
        'orb-drift-2': {
          '0%, 100%': { transform: 'translate(0, 0) scale(1)' },
          '40%': { transform: 'translate(-70px, 40px) scale(0.92)' },
          '70%': { transform: 'translate(40px, -60px) scale(1.1)' },
        },
        'orb-drift-3': {
          '0%, 100%': { transform: 'translate(0, 0) scale(1)' },
          '50%': { transform: 'translate(30px, 50px) scale(1.04)' },
        },
        'float': {
          '0%, 100%': { transform: 'translateY(0)' },
          '50%': { transform: 'translateY(-12px)' },
        },
        'marquee': {
          '0%': { transform: 'translateX(0)' },
          '100%': { transform: 'translateX(-50%)' },
        },
        'pulse-soft': {
          '0%, 100%': { opacity: '0.45' },
          '50%': { opacity: '1' },
        },
      },
      animation: {
        'orb-drift-1': 'orb-drift-1 22s ease-in-out infinite',
        'orb-drift-2': 'orb-drift-2 28s ease-in-out infinite',
        'orb-drift-3': 'orb-drift-3 18s ease-in-out infinite',
        'float': 'float 6s ease-in-out infinite',
        'marquee': 'marquee 35s linear infinite',
        'pulse-soft': 'pulse-soft 2.5s ease-in-out infinite',
      },
    },
  },
  plugins: [],
}

export default config
