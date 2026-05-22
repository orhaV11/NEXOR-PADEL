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
          bg: '#020202',
          surface: '#0a0a0a',
          card: '#111111',
          elevated: '#181818',
          border: '#1a1a1a',
          muted: '#252525',
        },
        lime: {
          neon: '#b5f72e',
          glow: '#c8ff47',
          dim: '#7db800',
          subtle: 'rgba(181,247,46,0.07)',
        },
        gold: {
          DEFAULT: '#c9a455',
          light: '#e8c97e',
          dark: '#9a7a30',
          subtle: 'rgba(201,164,85,0.08)',
          border: 'rgba(201,164,85,0.2)',
        },
      },
      fontFamily: {
        display: ['var(--font-bebas)', 'Impact', 'sans-serif'],
        body: ['var(--font-heebo)', 'Heebo', 'Arial', 'sans-serif'],
        heebo: ['var(--font-heebo)', 'Heebo', 'Arial', 'sans-serif'],
      },
      keyframes: {
        'glow-pulse': {
          '0%, 100%': { boxShadow: '0 0 15px rgba(181,247,46,0.25), 0 0 30px rgba(181,247,46,0.08)' },
          '50%': { boxShadow: '0 0 25px rgba(181,247,46,0.5), 0 0 50px rgba(181,247,46,0.15)' },
        },
        'gold-pulse': {
          '0%, 100%': { boxShadow: '0 0 15px rgba(201,164,85,0.2)' },
          '50%': { boxShadow: '0 0 30px rgba(201,164,85,0.4)' },
        },
        float: {
          '0%, 100%': { transform: 'translateY(0px)' },
          '50%': { transform: 'translateY(-10px)' },
        },
        shimmer: {
          '0%': { backgroundPosition: '-200% 0' },
          '100%': { backgroundPosition: '200% 0' },
        },
        'fade-up': {
          '0%': { opacity: '0', transform: 'translateY(24px)' },
          '100%': { opacity: '1', transform: 'translateY(0)' },
        },
        'orb-drift-1': {
          '0%, 100%': { transform: 'translate(0, 0) scale(1)' },
          '33%': { transform: 'translate(-40px, -50px) scale(1.08)' },
          '66%': { transform: 'translate(30px, 30px) scale(0.94)' },
        },
        'orb-drift-2': {
          '0%, 100%': { transform: 'translate(0, 0) scale(1)' },
          '33%': { transform: 'translate(50px, 30px) scale(0.96)' },
          '66%': { transform: 'translate(-30px, -40px) scale(1.06)' },
        },
        'line-scan': {
          '0%': { transform: 'translateX(100%)' },
          '100%': { transform: 'translateX(-100%)' },
        },
        'marquee': {
          '0%': { transform: 'translateX(0%)' },
          '100%': { transform: 'translateX(-50%)' },
        },
      },
      animation: {
        'glow-pulse': 'glow-pulse 3s ease-in-out infinite',
        'gold-pulse': 'gold-pulse 3s ease-in-out infinite',
        float: 'float 7s ease-in-out infinite',
        shimmer: 'shimmer 2.5s linear infinite',
        'fade-up': 'fade-up 0.7s ease-out forwards',
        'orb-drift-1': 'orb-drift-1 18s ease-in-out infinite',
        'orb-drift-2': 'orb-drift-2 22s ease-in-out infinite',
        'line-scan': 'line-scan 4s linear infinite',
        'marquee': 'marquee 20s linear infinite',
      },
      backgroundImage: {
        'grid-pattern': 'linear-gradient(rgba(255,255,255,0.02) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.02) 1px, transparent 1px)',
        'grid-fine': 'linear-gradient(rgba(255,255,255,0.015) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.015) 1px, transparent 1px)',
        'hero-radial': 'radial-gradient(ellipse 70% 50% at 50% -10%, rgba(181,247,46,0.05) 0%, transparent 70%)',
        'gold-radial': 'radial-gradient(ellipse 50% 40% at 50% 100%, rgba(201,164,85,0.04) 0%, transparent 60%)',
        'card-shine': 'linear-gradient(135deg, rgba(255,255,255,0.04) 0%, transparent 40%, rgba(255,255,255,0.02) 100%)',
        'luxury-gradient': 'linear-gradient(135deg, #111 0%, #0a0a0a 50%, #111 100%)',
      },
      backgroundSize: {
        'grid': '60px 60px',
        'grid-fine': '30px 30px',
      },
      boxShadow: {
        'neon-xs': '0 0 8px rgba(181,247,46,0.2)',
        'neon-sm': '0 0 15px rgba(181,247,46,0.3)',
        'neon-md': '0 0 25px rgba(181,247,46,0.4), 0 0 50px rgba(181,247,46,0.12)',
        'neon-lg': '0 0 40px rgba(181,247,46,0.5), 0 0 80px rgba(181,247,46,0.15)',
        'gold-sm': '0 0 15px rgba(201,164,85,0.25)',
        'gold-md': '0 0 25px rgba(201,164,85,0.35)',
        'card': '0 4px 32px rgba(0,0,0,0.7)',
        'card-hover': '0 12px 48px rgba(0,0,0,0.9), 0 0 0 1px rgba(181,247,46,0.15)',
        'card-gold': '0 12px 48px rgba(0,0,0,0.9), 0 0 0 1px rgba(201,164,85,0.2)',
        'luxury': '0 20px 60px rgba(0,0,0,0.8)',
        'luxury-sm': '0 8px 30px rgba(0,0,0,0.6)',
      },
    },
  },
  plugins: [],
}

export default config
