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
          bg: '#050505',
          surface: '#0f0f0f',
          card: '#111111',
          border: '#1e1e1e',
          muted: '#2a2a2a',
        },
        lime: {
          neon: '#b5f72e',
          glow: '#c8ff47',
          dim: '#8abc1e',
          subtle: 'rgba(181,247,46,0.1)',
        },
      },
      fontFamily: {
        display: ['var(--font-bebas)', 'sans-serif'],
        body: ['var(--font-inter)', 'sans-serif'],
      },
      keyframes: {
        'glow-pulse': {
          '0%, 100%': { boxShadow: '0 0 20px rgba(181,247,46,0.3), 0 0 40px rgba(181,247,46,0.1)' },
          '50%': { boxShadow: '0 0 30px rgba(181,247,46,0.6), 0 0 60px rgba(181,247,46,0.2)' },
        },
        float: {
          '0%, 100%': { transform: 'translateY(0px)' },
          '50%': { transform: 'translateY(-12px)' },
        },
        'speed-line': {
          '0%': { transform: 'translateX(-100%)', opacity: '0' },
          '50%': { opacity: '1' },
          '100%': { transform: 'translateX(100vw)', opacity: '0' },
        },
        shimmer: {
          '0%': { backgroundPosition: '-200% 0' },
          '100%': { backgroundPosition: '200% 0' },
        },
        'fade-up': {
          '0%': { opacity: '0', transform: 'translateY(30px)' },
          '100%': { opacity: '1', transform: 'translateY(0)' },
        },
        'orb-drift': {
          '0%, 100%': { transform: 'translate(0, 0) scale(1)' },
          '33%': { transform: 'translate(30px, -20px) scale(1.05)' },
          '66%': { transform: 'translate(-20px, 10px) scale(0.95)' },
        },
        scan: {
          '0%': { top: '-2px' },
          '100%': { top: '100%' },
        },
      },
      animation: {
        'glow-pulse': 'glow-pulse 2.5s ease-in-out infinite',
        float: 'float 6s ease-in-out infinite',
        'speed-line': 'speed-line 2s linear infinite',
        shimmer: 'shimmer 2.5s linear infinite',
        'fade-up': 'fade-up 0.6s ease-out forwards',
        'orb-drift': 'orb-drift 12s ease-in-out infinite',
        scan: 'scan 3s linear infinite',
      },
      backgroundImage: {
        'grid-pattern': 'linear-gradient(rgba(181,247,46,0.03) 1px, transparent 1px), linear-gradient(90deg, rgba(181,247,46,0.03) 1px, transparent 1px)',
        'hero-radial': 'radial-gradient(ellipse 80% 60% at 50% 0%, rgba(181,247,46,0.06) 0%, transparent 70%)',
        'card-shine': 'linear-gradient(135deg, rgba(255,255,255,0.05) 0%, transparent 50%, rgba(255,255,255,0.02) 100%)',
      },
      backgroundSize: {
        'grid': '60px 60px',
      },
      boxShadow: {
        'neon-sm': '0 0 10px rgba(181,247,46,0.3)',
        'neon-md': '0 0 20px rgba(181,247,46,0.4), 0 0 40px rgba(181,247,46,0.15)',
        'neon-lg': '0 0 30px rgba(181,247,46,0.5), 0 0 60px rgba(181,247,46,0.2)',
        'card': '0 4px 24px rgba(0,0,0,0.6)',
        'card-hover': '0 8px 40px rgba(0,0,0,0.8), 0 0 1px rgba(181,247,46,0.3)',
      },
    },
  },
  plugins: [],
}

export default config
