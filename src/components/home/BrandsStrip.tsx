'use client'

import { motion } from 'framer-motion'

const LUXURY_EASE = [0.16, 1, 0.3, 1] as const

const brands = [
  'HEAD',
  'Bullpadel',
  'NOX',
  'Adidas Padel',
  'Babolat',
  'Wilson',
  'Siux',
  'StarVie',
  'Dunlop',
  'Varlion',
]

// Double for seamless loop
const brandsDouble = [...brands, ...brands]

function BrandRow({ reverse = false }: { reverse?: boolean }) {
  return (
    <div className="relative flex overflow-hidden">
      <div
        className={`flex items-center gap-0 whitespace-nowrap ${
          reverse ? 'animate-marquee' : 'animate-marquee'
        }`}
        style={{
          animationDuration: reverse ? '42s' : '35s',
          animationDirection: reverse ? 'reverse' : 'normal',
        }}
        aria-hidden="true"
      >
        {brandsDouble.map((brand, i) => (
          <span key={i} className="inline-flex items-center">
            <span className="font-display text-2xl text-[rgba(242,237,223,0.2)] tracking-[0.25em] uppercase hover:text-[rgba(201,165,90,0.6)] transition-colors duration-300 cursor-default px-6">
              {brand}
            </span>
            <span className="text-[rgba(201,165,90,0.25)] text-xs" aria-hidden="true">◆</span>
          </span>
        ))}
      </div>
    </div>
  )
}

export default function BrandsStrip() {
  return (
    <section className="py-16 overflow-hidden bg-[#08080a]">
      <div className="divider-subtle" />

      <motion.div
        initial={{ opacity: 0, y: 40 }}
        whileInView={{ opacity: 1, y: 0 }}
        viewport={{ once: true, margin: '-80px' }}
        transition={{ duration: 0.9, ease: LUXURY_EASE }}
        className="text-center mb-10 pt-10"
      >
        <span className="section-label mx-auto">מותגים מובילים</span>
      </motion.div>

      <div className="flex flex-col gap-5">
        <BrandRow reverse={false} />
        <BrandRow reverse={true} />
      </div>

      <div className="divider-subtle mt-10" />
    </section>
  )
}
