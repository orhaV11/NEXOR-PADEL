'use client'

import { motion } from 'framer-motion'
import Link from 'next/link'
import { ArrowLeft } from 'lucide-react'
import { categories } from '@/lib/data'

const categoryGradients: Record<string, string> = {
  rackets: 'from-[#0d0d0d] via-[#111] to-[#0a0a0a]',
  balls: 'from-[#0d0d08] via-[#12120a] to-[#0a0a08]',
  bags: 'from-[#0a0d0a] via-[#0f120f] to-[#080a08]',
  shoes: 'from-[#080810] via-[#0d0d18] to-[#080810]',
  grips: 'from-[#080c0c] via-[#0d1313] to-[#080c0c]',
  apparel: 'from-[#0a0a0a] via-[#101010] to-[#0a0a0a]',
  accessories: 'from-[#0c0808] via-[#140e0e] to-[#0c0808]',
}

const accentColors: Record<string, string> = {
  rackets: '#b5f72e',
  balls: '#eab308',
  bags: '#b5f72e',
  shoes: '#818cf8',
  grips: '#22d3ee',
  apparel: '#b5f72e',
  accessories: '#f87171',
}

export default function FeaturedCategories() {
  return (
    <section className="py-24 max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
      <motion.div
        initial={{ opacity: 0, y: 30 }}
        whileInView={{ opacity: 1, y: 0 }}
        viewport={{ once: true, margin: '-100px' }}
        transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
        className="mb-12"
      >
        <div className="section-tag">קטגוריות</div>
        <div className="flex items-end justify-between">
          <h2 className="section-heading">
            קנה לפי<br />
            <span className="text-[#b5f72e]">קטגוריה</span>
          </h2>
          <Link
            href="/shop"
            className="hidden md:flex items-center gap-2 text-white/35 text-xs uppercase tracking-widest hover:text-[#b5f72e] transition-colors group"
          >
            <ArrowLeft className="w-4 h-4 group-hover:-translate-x-1 transition-transform" />
            כל המוצרים
          </Link>
        </div>
      </motion.div>

      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3">
        {/* Featured large rackets card */}
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-50px' }}
          transition={{ duration: 0.7, delay: 0, ease: [0.16, 1, 0.3, 1] }}
          className="col-span-2 row-span-2"
        >
          <Link
            href="/shop?category=rackets"
            className={`group relative block h-64 md:h-80 bg-gradient-to-br ${categoryGradients.rackets} border border-white/5 overflow-hidden hover:border-[#b5f72e]/25 transition-all duration-700`}
          >
            <div
              className="absolute inset-0 opacity-0 group-hover:opacity-100 transition-opacity duration-700"
              style={{ background: `radial-gradient(ellipse at 50% 50%, rgba(181,247,46,0.06) 0%, transparent 70%)` }}
            />

            {/* Racket SVG */}
            <div className="absolute inset-0 flex items-center justify-center" style={{ color: accentColors.rackets }}>
              <svg viewBox="0 0 200 200" className="w-48 h-48 opacity-12 group-hover:opacity-22 transition-opacity duration-700">
                <ellipse cx="90" cy="80" rx="55" ry="65" stroke="currentColor" strokeWidth="4" fill="none"/>
                <line x1="35" y1="80" x2="145" y2="80" stroke="currentColor" strokeWidth="2" opacity="0.5"/>
                <line x1="90" y1="15" x2="90" y2="145" stroke="currentColor" strokeWidth="2" opacity="0.5"/>
                <line x1="47" y1="38" x2="133" y2="122" stroke="currentColor" strokeWidth="1.5" opacity="0.3"/>
                <line x1="133" y1="38" x2="47" y2="122" stroke="currentColor" strokeWidth="1.5" opacity="0.3"/>
                <line x1="57" y1="23" x2="57" y2="137" stroke="currentColor" strokeWidth="1" opacity="0.2"/>
                <line x1="123" y1="23" x2="123" y2="137" stroke="currentColor" strokeWidth="1" opacity="0.2"/>
                <rect x="82" y="143" width="16" height="50" rx="3" fill="currentColor" opacity="0.4"/>
              </svg>
            </div>

            <div className="absolute bottom-0 left-0 right-0 p-6">
              <p className="text-white/25 text-[10px] uppercase tracking-widest mb-1">הקטגוריה הפופולרית</p>
              <h3 className="font-display text-4xl text-white tracking-wider uppercase group-hover:text-[#b5f72e] transition-colors duration-500">
                מחבטים
              </h3>
              <div className="flex items-center gap-2 mt-2">
                <span className="text-white/25 text-sm">4 מוצרים</span>
                <span className="text-[#b5f72e]/40">·</span>
                <span className="text-white/25 text-sm">כל הרמות</span>
              </div>
              <div className="mt-3 flex items-center gap-2 text-[#b5f72e] text-xs font-bold uppercase tracking-widest opacity-0 group-hover:opacity-100 transition-opacity duration-400">
                <ArrowLeft className="w-3 h-3" /> גלה עכשיו
              </div>
            </div>

            <div className="absolute top-4 left-4 px-2 py-1 bg-[#b5f72e] text-black text-[10px] font-bold uppercase tracking-widest">
              #1
            </div>
          </Link>
        </motion.div>

        {/* Other categories */}
        {categories.slice(1).map((cat, i) => (
          <motion.div
            key={cat.id}
            initial={{ opacity: 0, y: 30 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true, margin: '-50px' }}
            transition={{ duration: 0.6, delay: (i + 1) * 0.08, ease: [0.16, 1, 0.3, 1] }}
          >
            <Link
              href={`/shop?category=${cat.id}`}
              className={`group relative block h-32 md:h-36 bg-gradient-to-br ${categoryGradients[cat.id] || categoryGradients.accessories} border border-white/5 overflow-hidden hover:border-[#b5f72e]/25 transition-all duration-500 hover:shadow-card-hover`}
            >
              <div
                className="absolute inset-0 opacity-0 group-hover:opacity-100 transition-opacity duration-500"
                style={{ background: `radial-gradient(ellipse at 50% 50%, ${accentColors[cat.id] || '#b5f72e'}0a 0%, transparent 70%)` }}
              />
              <div className="p-4 h-full flex flex-col justify-between">
                <span className="text-2xl">{cat.icon}</span>
                <div>
                  <h3 className="font-display text-xl text-white tracking-wider uppercase group-hover:text-[#b5f72e] transition-colors leading-none">
                    {cat.label}
                  </h3>
                  <p className="text-white/25 text-xs mt-0.5">{cat.count} מוצרים</p>
                </div>
              </div>
              <ArrowLeft className="absolute bottom-4 left-4 w-4 h-4 text-transparent group-hover:text-[#b5f72e] transition-all duration-300 -translate-x-2 group-hover:translate-x-0" />
            </Link>
          </motion.div>
        ))}
      </div>

      <div className="flex md:hidden justify-center mt-6">
        <Link
          href="/shop"
          className="flex items-center gap-2 text-white/35 text-xs uppercase tracking-widest hover:text-[#b5f72e] transition-colors"
        >
          <ArrowLeft className="w-4 h-4" /> כל הקטגוריות
        </Link>
      </div>
    </section>
  )
}
