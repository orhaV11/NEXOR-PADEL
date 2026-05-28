import type { Product } from '@/lib/types'
import { clsx } from 'clsx'

type Props = {
  product: Product
  className?: string
}

const categoryGradients: Record<string, string> = {
  rackets:     'radial-gradient(ellipse at 30% 30%, #1a1208 0%, #0d0d10 60%)',
  balls:       'radial-gradient(ellipse at 30% 25%, #12100a 0%, #0d0d10 65%)',
  shoes:       'radial-gradient(ellipse at 40% 35%, #0e0e16 0%, #0a0a10 65%)',
  bags:        'radial-gradient(ellipse at 35% 30%, #0a0f0a 0%, #0d0d10 65%)',
  grips:       'radial-gradient(ellipse at 30% 30%, #0d1014 0%, #0a0a0d 65%)',
  apparel:     'radial-gradient(ellipse at 50% 20%, #0a0a12 0%, #080808 65%)',
  accessories: 'radial-gradient(ellipse at 50% 50%, #12100e 0%, #0d0d10 65%)',
}

const categoryIcons: Record<string, string> = {
  rackets: `<svg viewBox="0 0 120 160" fill="none" xmlns="http://www.w3.org/2000/svg">
    <ellipse cx="60" cy="58" rx="32" ry="42" stroke="currentColor" stroke-width="2.5" opacity="0.7"/>
    <line x1="28" y1="36" x2="92" y2="36" stroke="currentColor" stroke-width="0.8" opacity="0.3"/>
    <line x1="28" y1="44" x2="92" y2="44" stroke="currentColor" stroke-width="0.8" opacity="0.3"/>
    <line x1="28" y1="52" x2="92" y2="52" stroke="currentColor" stroke-width="0.8" opacity="0.3"/>
    <line x1="28" y1="60" x2="92" y2="60" stroke="currentColor" stroke-width="0.8" opacity="0.35"/>
    <line x1="28" y1="68" x2="92" y2="68" stroke="currentColor" stroke-width="0.8" opacity="0.3"/>
    <line x1="28" y1="76" x2="92" y2="76" stroke="currentColor" stroke-width="0.8" opacity="0.3"/>
    <line x1="28" y1="84" x2="92" y2="84" stroke="currentColor" stroke-width="0.8" opacity="0.25"/>
    <line x1="44" y1="16" x2="44" y2="100" stroke="currentColor" stroke-width="0.8" opacity="0.3"/>
    <line x1="52" y1="16" x2="52" y2="100" stroke="currentColor" stroke-width="0.8" opacity="0.3"/>
    <line x1="60" y1="16" x2="60" y2="100" stroke="currentColor" stroke-width="0.8" opacity="0.35"/>
    <line x1="68" y1="16" x2="68" y2="100" stroke="currentColor" stroke-width="0.8" opacity="0.3"/>
    <line x1="76" y1="16" x2="76" y2="100" stroke="currentColor" stroke-width="0.8" opacity="0.3"/>
    <circle cx="60" cy="58" r="10" stroke="currentColor" stroke-width="1" opacity="0.2"/>
    <rect x="55" y="99" width="10" height="44" rx="5" fill="currentColor" opacity="0.5"/>
    <rect x="57" y="103" width="6" height="36" rx="3" fill="none" stroke="currentColor" stroke-width="0.5" opacity="0.4"/>
    <line x1="55" y1="110" x2="65" y2="110" stroke="currentColor" stroke-width="1.5" opacity="0.25"/>
    <line x1="55" y1="117" x2="65" y2="117" stroke="currentColor" stroke-width="1.5" opacity="0.25"/>
    <line x1="55" y1="124" x2="65" y2="124" stroke="currentColor" stroke-width="1.5" opacity="0.25"/>
    <line x1="55" y1="131" x2="65" y2="131" stroke="currentColor" stroke-width="1.5" opacity="0.25"/>
  </svg>`,

  balls: `<svg viewBox="0 0 120 120" fill="none" xmlns="http://www.w3.org/2000/svg">
    <circle cx="60" cy="60" r="36" stroke="currentColor" stroke-width="2.5" opacity="0.7"/>
    <path d="M 30 42 Q 50 60 30 78" stroke="currentColor" stroke-width="1.5" fill="none" opacity="0.45"/>
    <path d="M 90 42 Q 70 60 90 78" stroke="currentColor" stroke-width="1.5" fill="none" opacity="0.45"/>
    <ellipse cx="46" cy="44" rx="10" ry="7" fill="currentColor" opacity="0.07" transform="rotate(-30 46 44)"/>
    <path d="M 36 76 Q 60 90 84 76" stroke="currentColor" stroke-width="1" fill="none" opacity="0.2"/>
    <circle cx="60" cy="60" r="4" fill="currentColor" opacity="0.25"/>
  </svg>`,

  shoes: `<svg viewBox="0 0 140 100" fill="none" xmlns="http://www.w3.org/2000/svg">
    <path d="M 18 72 Q 20 82 50 82 Q 90 82 118 76 Q 126 74 124 68 L 18 68 Z" fill="currentColor" opacity="0.2"/>
    <path d="M 20 68 Q 22 48 42 44 Q 62 40 80 42 Q 100 44 114 52 Q 122 58 120 68 Z" stroke="currentColor" stroke-width="2" fill="currentColor" fill-opacity="0.06" opacity="0.7"/>
    <path d="M 20 68 Q 18 56 24 48 Q 30 42 42 44" stroke="currentColor" stroke-width="1.5" fill="none" opacity="0.5"/>
    <line x1="56" y1="43" x2="52" y2="67" stroke="currentColor" stroke-width="1" opacity="0.3"/>
    <line x1="64" y1="42" x2="60" y2="67" stroke="currentColor" stroke-width="1" opacity="0.3"/>
    <line x1="72" y1="42" x2="68" y2="67" stroke="currentColor" stroke-width="1" opacity="0.3"/>
    <line x1="52" y1="52" x2="68" y2="51" stroke="currentColor" stroke-width="1.5" opacity="0.35"/>
    <line x1="53" y1="58" x2="69" y2="57" stroke="currentColor" stroke-width="1.5" opacity="0.35"/>
    <line x1="53" y1="64" x2="69" y2="63" stroke="currentColor" stroke-width="1.5" opacity="0.35"/>
    <path d="M 108 52 Q 118 54 120 62 Q 122 68 116 70" stroke="currentColor" stroke-width="1.5" fill="none" opacity="0.4"/>
    <line x1="30" y1="78" x2="30" y2="82" stroke="currentColor" stroke-width="2" opacity="0.3"/>
    <line x1="40" y1="79" x2="40" y2="82" stroke="currentColor" stroke-width="2" opacity="0.3"/>
    <line x1="50" y1="79" x2="50" y2="82" stroke="currentColor" stroke-width="2" opacity="0.3"/>
    <line x1="60" y1="79" x2="60" y2="82" stroke="currentColor" stroke-width="2" opacity="0.3"/>
    <line x1="70" y1="79" x2="70" y2="82" stroke="currentColor" stroke-width="2" opacity="0.3"/>
    <line x1="80" y1="79" x2="80" y2="82" stroke="currentColor" stroke-width="2" opacity="0.3"/>
    <line x1="90" y1="78" x2="90" y2="82" stroke="currentColor" stroke-width="2" opacity="0.3"/>
  </svg>`,

  bags: `<svg viewBox="0 0 120 120" fill="none" xmlns="http://www.w3.org/2000/svg">
    <rect x="16" y="40" width="88" height="64" rx="4" stroke="currentColor" stroke-width="2.5" fill="currentColor" fill-opacity="0.04" opacity="0.7"/>
    <path d="M 38 40 Q 38 28 48 24 Q 60 20 72 24 Q 82 28 82 40" stroke="currentColor" stroke-width="2" fill="none" opacity="0.5"/>
    <rect x="24" y="56" width="30" height="36" rx="2" stroke="currentColor" stroke-width="1.2" opacity="0.4"/>
    <line x1="16" y1="52" x2="104" y2="52" stroke="currentColor" stroke-width="1.5" opacity="0.35"/>
    <line x1="28" y1="50" x2="28" y2="54" stroke="currentColor" stroke-width="1" opacity="0.25"/>
    <line x1="36" y1="50" x2="36" y2="54" stroke="currentColor" stroke-width="1" opacity="0.25"/>
    <line x1="44" y1="50" x2="44" y2="54" stroke="currentColor" stroke-width="1" opacity="0.25"/>
    <line x1="52" y1="50" x2="52" y2="54" stroke="currentColor" stroke-width="1" opacity="0.25"/>
    <line x1="60" y1="50" x2="60" y2="54" stroke="currentColor" stroke-width="1" opacity="0.25"/>
    <line x1="68" y1="50" x2="68" y2="54" stroke="currentColor" stroke-width="1" opacity="0.25"/>
    <line x1="76" y1="50" x2="76" y2="54" stroke="currentColor" stroke-width="1" opacity="0.25"/>
    <line x1="84" y1="50" x2="84" y2="54" stroke="currentColor" stroke-width="1" opacity="0.25"/>
    <line x1="92" y1="50" x2="92" y2="54" stroke="currentColor" stroke-width="1" opacity="0.25"/>
    <line x1="24" y1="64" x2="54" y2="64" stroke="currentColor" stroke-width="1.2" opacity="0.3"/>
    <rect x="60" y="58" width="32" height="22" rx="2" stroke="currentColor" stroke-width="1" opacity="0.2"/>
    <rect x="18" y="42" width="84" height="60" rx="3" stroke="currentColor" stroke-width="0.6" stroke-dasharray="4 3" opacity="0.15"/>
  </svg>`,

  grips: `<svg viewBox="0 0 80 140" fill="none" xmlns="http://www.w3.org/2000/svg">
    <rect x="26" y="12" width="28" height="116" rx="14" stroke="currentColor" stroke-width="2.5" fill="currentColor" fill-opacity="0.05" opacity="0.7"/>
    <line x1="26" y1="30" x2="54" y2="30" stroke="currentColor" stroke-width="2" opacity="0.3"/>
    <line x1="26" y1="40" x2="54" y2="40" stroke="currentColor" stroke-width="2" opacity="0.28"/>
    <line x1="26" y1="50" x2="54" y2="50" stroke="currentColor" stroke-width="2" opacity="0.3"/>
    <line x1="26" y1="60" x2="54" y2="60" stroke="currentColor" stroke-width="2" opacity="0.28"/>
    <line x1="26" y1="70" x2="54" y2="70" stroke="currentColor" stroke-width="2" opacity="0.3"/>
    <line x1="26" y1="80" x2="54" y2="80" stroke="currentColor" stroke-width="2" opacity="0.28"/>
    <line x1="26" y1="90" x2="54" y2="90" stroke="currentColor" stroke-width="2" opacity="0.3"/>
    <line x1="26" y1="100" x2="54" y2="100" stroke="currentColor" stroke-width="2" opacity="0.28"/>
    <line x1="26" y1="110" x2="54" y2="110" stroke="currentColor" stroke-width="2" opacity="0.25"/>
    <ellipse cx="40" cy="18" rx="14" ry="8" stroke="currentColor" stroke-width="1.5" opacity="0.4"/>
    <line x1="34" y1="20" x2="34" y2="118" stroke="currentColor" stroke-width="1" opacity="0.1"/>
  </svg>`,

  apparel: `<svg viewBox="0 0 120 120" fill="none" xmlns="http://www.w3.org/2000/svg">
    <path d="M 30 38 L 14 56 L 26 62 L 26 100 L 94 100 L 94 62 L 106 56 L 90 38 Q 78 46 60 46 Q 42 46 30 38 Z" stroke="currentColor" stroke-width="2.5" fill="currentColor" fill-opacity="0.04" opacity="0.7"/>
    <path d="M 30 38 Q 40 32 50 34 L 60 48 L 70 34 Q 80 32 90 38" stroke="currentColor" stroke-width="1.5" fill="none" opacity="0.5"/>
    <path d="M 30 38 L 14 56 L 26 62 L 34 48 Z" fill="currentColor" opacity="0.06"/>
    <path d="M 90 38 L 106 56 L 94 62 L 86 48 Z" fill="currentColor" opacity="0.06"/>
    <line x1="60" y1="50" x2="60" y2="98" stroke="currentColor" stroke-width="0.8" stroke-dasharray="3 4" opacity="0.2"/>
    <line x1="26" y1="96" x2="94" y2="96" stroke="currentColor" stroke-width="1" opacity="0.2"/>
    <line x1="15" y1="54" x2="25" y2="59" stroke="currentColor" stroke-width="1.5" opacity="0.3"/>
    <line x1="95" y1="59" x2="105" y2="54" stroke="currentColor" stroke-width="1.5" opacity="0.3"/>
  </svg>`,

  accessories: `<svg viewBox="0 0 120 120" fill="none" xmlns="http://www.w3.org/2000/svg">
    <rect x="20" y="20" width="80" height="80" rx="4" transform="rotate(45 60 60)" stroke="currentColor" stroke-width="2" fill="none" opacity="0.45"/>
    <rect x="34" y="34" width="52" height="52" rx="2" transform="rotate(45 60 60)" stroke="currentColor" stroke-width="1.2" fill="none" opacity="0.3"/>
    <rect x="48" y="48" width="24" height="24" rx="1" transform="rotate(45 60 60)" fill="currentColor" opacity="0.12" stroke="currentColor" stroke-width="1.5"/>
    <line x1="60" y1="35" x2="60" y2="85" stroke="currentColor" stroke-width="0.8" opacity="0.2"/>
    <line x1="35" y1="60" x2="85" y2="60" stroke="currentColor" stroke-width="0.8" opacity="0.2"/>
    <line x1="42" y1="42" x2="78" y2="78" stroke="currentColor" stroke-width="0.6" opacity="0.15"/>
    <line x1="78" y1="42" x2="42" y2="78" stroke="currentColor" stroke-width="0.6" opacity="0.15"/>
    <circle cx="60" cy="25" r="2" fill="currentColor" opacity="0.4"/>
    <circle cx="95" cy="60" r="2" fill="currentColor" opacity="0.4"/>
    <circle cx="60" cy="95" r="2" fill="currentColor" opacity="0.4"/>
    <circle cx="25" cy="60" r="2" fill="currentColor" opacity="0.4"/>
  </svg>`,
}

export default function ProductImagePlaceholder({ product, className }: Props) {
  const icon = categoryIcons[product.category] || categoryIcons.accessories
  const accent = product.image.accent
  const gradient = categoryGradients[product.category] || categoryGradients.accessories

  return (
    <div
      className={clsx('relative flex items-center justify-center overflow-hidden group', className)}
      style={{ background: gradient }}
    >
      {/* Product image gradient from data */}
      <div
        className={clsx('absolute inset-0 bg-gradient-to-br', product.image.gradient)}
        style={{ opacity: 0.5 }}
      />

      {/* Luxury top-left highlight */}
      <div
        className="absolute inset-0"
        style={{
          background: 'radial-gradient(ellipse at 25% 20%, rgba(255,255,255,0.035) 0%, transparent 55%)',
        }}
      />

      {/* Subtle grid texture */}
      <div
        className="absolute inset-0 opacity-[0.03]"
        style={{
          backgroundImage:
            'linear-gradient(rgba(255,255,255,0.5) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.5) 1px, transparent 1px)',
          backgroundSize: '16px 16px',
        }}
      />

      {/* Accent glow — brightens on hover */}
      <div
        className="absolute inset-0 transition-opacity duration-500 opacity-[0.08] group-hover:opacity-[0.14]"
        style={{
          background: `radial-gradient(ellipse at 60% 60%, ${accent} 0%, transparent 65%)`,
        }}
      />

      {/* SVG icon */}
      <div
        className="relative w-[55%] h-[55%] transition-transform duration-500 group-hover:scale-[1.06] drop-shadow-[0_0_20px_rgba(0,0,0,0.8)]"
        style={{ color: accent }}
        dangerouslySetInnerHTML={{ __html: icon }}
      />

      {/* Bottom accent line — fades in on hover */}
      <div
        className="absolute bottom-0 inset-x-0 h-[2px] opacity-0 group-hover:opacity-100 transition-opacity duration-500"
        style={{ background: `linear-gradient(90deg, transparent, ${accent}55, transparent)` }}
      />

      {/* Corner marks — fade in on hover */}
      <div className="absolute top-2.5 right-2.5 w-3 h-3 border-t border-r border-[rgba(201,165,90,0.25)] transition-opacity duration-300 opacity-0 group-hover:opacity-100" />
      <div className="absolute bottom-2.5 left-2.5 w-3 h-3 border-b border-l border-[rgba(201,165,90,0.25)] transition-opacity duration-300 opacity-0 group-hover:opacity-100" />
    </div>
  )
}
