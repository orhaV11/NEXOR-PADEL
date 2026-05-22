'use client'

import { motion } from 'framer-motion'
import Link from 'next/link'
import { ArrowLeft } from 'lucide-react'
import { bestSellers } from '@/lib/data'
import ProductCard from '@/components/shop/ProductCard'

const LUXURY_EASE = [0.16, 1, 0.3, 1] as const

export default function BestSellers() {
  return (
    <section className="py-24 md:py-32 bg-[#070708] relative overflow-hidden">
      {/* Subtle radial glow behind grid */}
      <div
        className="absolute inset-0 pointer-events-none"
        aria-hidden="true"
        style={{
          background:
            'radial-gradient(ellipse at 50% 50%, rgba(201,165,90,0.03) 0%, transparent 70%)',
        }}
      />

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
            <span className="section-label">
              <span className="w-1.5 h-1.5 rounded-full bg-[#c9a55a] inline-block animate-pulse-soft" />
              הנמכרים ביותר
            </span>
            <h2 className="section-title mt-3">
              המועדפים של{' '}
              <span className="text-gold-gradient">השחקנים</span>
            </h2>
            <p className="text-[rgba(242,237,223,0.4)] mt-4 max-w-md text-sm leading-relaxed">
              הציוד המהימן ביותר על ידי שחקני פאדל רציניים. נבדק במגרש. אהוב על ידי אלפים.
            </p>
            {/* Animated gold line */}
            <motion.div
              initial={{ scaleX: 0 }}
              whileInView={{ scaleX: 1 }}
              viewport={{ once: true }}
              transition={{ duration: 1.2, ease: [0.16, 1, 0.3, 1], delay: 0.2 }}
              className="h-px bg-gradient-to-r from-[#c9a55a] to-transparent origin-right mt-4 max-w-[120px]"
            />
          </div>

          <Link href="/shop?filter=bestseller">
            <motion.div
              whileHover={{ x: -4 }}
              className="flex items-center gap-2 text-[#c9a55a] text-xs uppercase tracking-widest cursor-pointer"
            >
              כל המחבטים <ArrowLeft className="w-3.5 h-3.5" />
            </motion.div>
          </Link>
        </motion.div>

        {/* Product grid with stagger */}
        <motion.div
          initial="hidden"
          whileInView="visible"
          viewport={{ once: true, margin: '-80px' }}
          variants={{
            hidden: {},
            visible: { transition: { staggerChildren: 0.1, delayChildren: 0.2 } },
          }}
          className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-2.5 md:gap-4"
        >
          {bestSellers.map((product, i) => (
            <ProductCard key={product.id} product={product} index={i} />
          ))}
        </motion.div>
      </div>
    </section>
  )
}
