import type { Product } from '@/lib/types'
import { clsx } from 'clsx'

type Props = {
  product: Product
  className?: string
}

const categoryIcons: Record<string, string> = {
  rackets: `<svg viewBox="0 0 80 80" fill="none" xmlns="http://www.w3.org/2000/svg">
    <ellipse cx="35" cy="32" rx="20" ry="25" stroke="currentColor" stroke-width="2.5" stroke-opacity="0.6"/>
    <line x1="15" y1="32" x2="55" y2="32" stroke="currentColor" stroke-width="1.5" stroke-opacity="0.3"/>
    <line x1="35" y1="7" x2="35" y2="57" stroke="currentColor" stroke-width="1.5" stroke-opacity="0.3"/>
    <line x1="19" y1="18" x2="51" y2="46" stroke="currentColor" stroke-width="1" stroke-opacity="0.2"/>
    <line x1="51" y1="18" x2="19" y2="46" stroke="currentColor" stroke-width="1" stroke-opacity="0.2"/>
    <rect x="32" y="55" width="6" height="18" rx="1" fill="currentColor" fill-opacity="0.5"/>
  </svg>`,
  balls: `<svg viewBox="0 0 80 80" fill="none" xmlns="http://www.w3.org/2000/svg">
    <circle cx="40" cy="40" r="22" stroke="currentColor" stroke-width="2.5" stroke-opacity="0.6"/>
    <path d="M 18 40 Q 30 28 52 40 Q 30 52 18 40Z" stroke="currentColor" stroke-width="1.5" stroke-opacity="0.3" fill="none"/>
  </svg>`,
  bags: `<svg viewBox="0 0 80 80" fill="none" xmlns="http://www.w3.org/2000/svg">
    <rect x="15" y="28" width="50" height="36" rx="3" stroke="currentColor" stroke-width="2.5" stroke-opacity="0.6"/>
    <path d="M28 28V22a12 12 0 0 1 24 0v6" stroke="currentColor" stroke-width="2.5" stroke-opacity="0.4"/>
    <line x1="15" y1="42" x2="65" y2="42" stroke="currentColor" stroke-width="1.5" stroke-opacity="0.2"/>
  </svg>`,
  shoes: `<svg viewBox="0 0 80 80" fill="none" xmlns="http://www.w3.org/2000/svg">
    <path d="M 10 52 Q 15 35 35 38 Q 50 40 65 45 Q 70 47 68 52 Z" stroke="currentColor" stroke-width="2.5" stroke-opacity="0.6" fill="none"/>
    <path d="M 10 52 Q 15 56 35 56 Q 55 56 68 52" stroke="currentColor" stroke-width="2" stroke-opacity="0.4"/>
    <path d="M 28 38 Q 32 28 40 30" stroke="currentColor" stroke-width="2" stroke-opacity="0.3"/>
  </svg>`,
  grips: `<svg viewBox="0 0 80 80" fill="none" xmlns="http://www.w3.org/2000/svg">
    <rect x="32" y="10" width="16" height="60" rx="8" stroke="currentColor" stroke-width="2.5" stroke-opacity="0.6"/>
    <line x1="32" y1="25" x2="48" y2="25" stroke="currentColor" stroke-width="1" stroke-opacity="0.2"/>
    <line x1="32" y1="35" x2="48" y2="35" stroke="currentColor" stroke-width="1" stroke-opacity="0.2"/>
    <line x1="32" y1="45" x2="48" y2="45" stroke="currentColor" stroke-width="1" stroke-opacity="0.2"/>
    <line x1="32" y1="55" x2="48" y2="55" stroke="currentColor" stroke-width="1" stroke-opacity="0.2"/>
  </svg>`,
  apparel: `<svg viewBox="0 0 80 80" fill="none" xmlns="http://www.w3.org/2000/svg">
    <path d="M28 15 L12 28 L22 33 L22 65 L58 65 L58 33 L68 28 L52 15 Q40 22 28 15Z" stroke="currentColor" stroke-width="2.5" stroke-opacity="0.6" fill="none"/>
  </svg>`,
  accessories: `<svg viewBox="0 0 80 80" fill="none" xmlns="http://www.w3.org/2000/svg">
    <circle cx="40" cy="40" r="20" stroke="currentColor" stroke-width="2.5" stroke-opacity="0.6"/>
    <circle cx="40" cy="40" r="5" fill="currentColor" fill-opacity="0.4"/>
    <line x1="40" y1="15" x2="40" y2="25" stroke="currentColor" stroke-width="2" stroke-opacity="0.3"/>
    <line x1="40" y1="55" x2="40" y2="65" stroke="currentColor" stroke-width="2" stroke-opacity="0.3"/>
    <line x1="15" y1="40" x2="25" y2="40" stroke="currentColor" stroke-width="2" stroke-opacity="0.3"/>
    <line x1="55" y1="40" x2="65" y2="40" stroke="currentColor" stroke-width="2" stroke-opacity="0.3"/>
  </svg>`,
}

export default function ProductImagePlaceholder({ product, className }: Props) {
  const icon = categoryIcons[product.category] || categoryIcons.accessories
  const accent = product.image.accent

  return (
    <div
      className={clsx(
        'relative flex items-center justify-center overflow-hidden',
        `bg-gradient-to-br ${product.image.gradient}`,
        className
      )}
    >
      {/* Subtle grid */}
      <div
        className="absolute inset-0 opacity-[0.04]"
        style={{
          backgroundImage: 'linear-gradient(rgba(255,255,255,0.5) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.5) 1px, transparent 1px)',
          backgroundSize: '20px 20px',
        }}
      />

      {/* Glow */}
      <div
        className="absolute inset-0 opacity-10"
        style={{
          background: `radial-gradient(ellipse at 50% 50%, ${accent} 0%, transparent 70%)`,
        }}
      />

      {/* SVG Icon */}
      <div
        className="relative w-1/2 h-1/2"
        style={{ color: accent }}
        dangerouslySetInnerHTML={{ __html: icon }}
      />

      {/* Corner accent */}
      <div
        className="absolute bottom-2 right-2 w-1.5 h-1.5 rounded-full"
        style={{ backgroundColor: accent, opacity: 0.6 }}
      />
    </div>
  )
}
