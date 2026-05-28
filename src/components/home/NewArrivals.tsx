'use client'

import { motion } from 'framer-motion'
import Link from 'next/link'
import { ArrowLeft, Sparkles } from 'lucide-react'
import { newArrivals } from '@/lib/data'
import ProductCard from '@/components/shop/ProductCard'

const LUXURY_EASE = [0.16, 1, 0.3, 1] as const

export default function NewArrivals() {
  return (
    <section className="py-24 md:py-32 bg-[#08080a]">
      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
        {/* Header */}
        <motion.div
          initial={{ opacity: 0, y: 40 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.9, ease: LUXURY_EASE }}
          className="flex flex-col md:flex-row md:items-end justify-between gap-6 mb-12 md:mb-16"
        >
          <div>
            <div className="flex items-center gap-2 mb-3">
              <Sparkles className="w-4 h-4 text-[#c9a55a]" aria-hidden="true" />
              <span className="section-label" style={{ marginBottom: 0 }}>
                <span className="w-1.5 h-1.5 rounded-full bg-[#c9a55a] inline-block animate-pulse-soft" />
                הגיע עכשיו
              </span>
            </div>
            <h2 className="section-title">
              חדש{' '}
              <span className="text-gold-gradient">בחנות</span>
            </h2>
            <p className="text-[rgba(242,237,223,0.4)] mt-4 max-w-md text-sm leading-relaxed">
              הציוד הכי עדכני ממותגי הפאדל המובילים. ראשון במגרש, ראשון בתיק.
            </p>
          </div>
          <Link
            href="/shop?filter=new"
            className="flex items-center gap-2 text-[rgba(242,237,223,0.35)] text-xs uppercase tracking-[0.2em] hover:text-[#c9a55a] transition-colors duration-300 whitespace-nowrap pb-1"
          >
            כל החדשים
            <ArrowLeft className="w-4 h-4" />
          </Link>
        </motion.div>

        {/* Mobile horizontal scroll */}
        <motion.div
          initial="hidden"
          whileInView="visible"
          viewport={{ once: true, margin: '-80px' }}
          variants={{
            hidden: {},
            visible: { transition: { staggerChildren: 0.1, delayChildren: 0.2 } },
          }}
          className="md:hidden scroll-x flex gap-3 pb-2"
        >
          {newArrivals.map((product, i) => (
            <div key={product.id} className="flex-shrink-0 w-[70vw]">
              <ProductCard product={product} index={i} />
            </div>
          ))}
        </motion.div>

        {/* Desktop grid */}
        <motion.div
          initial="hidden"
          whileInView="visible"
          viewport={{ once: true, margin: '-80px' }}
          variants={{
            hidden: {},
            visible: { transition: { staggerChildren: 0.1, delayChildren: 0.2 } },
          }}
          className="hidden md:grid grid-cols-2 lg:grid-cols-4 gap-4"
        >
          {newArrivals.map((product, i) => (
            <ProductCard key={product.id} product={product} index={i} />
          ))}
        </motion.div>
      </div>
    </section>
  )
}
