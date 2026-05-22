'use client'

import { motion } from 'framer-motion'
import Link from 'next/link'
import { ArrowLeft } from 'lucide-react'
import { categories } from '@/lib/data'

const LUXURY_EASE = [0.16, 1, 0.3, 1] as const

const categoryHrefs: Record<string, string> = {
  rackets: '/shop?category=rackets',
  balls: '/shop?category=balls',
  bags: '/shop?category=bags',
  shoes: '/shop?category=shoes',
  grips: '/shop?category=grips',
  apparel: '/shop?category=apparel',
  accessories: '/shop?category=accessories',
}

const cardVariants = {
  hidden: { opacity: 0, y: 40 },
  visible: (i: number) => ({
    opacity: 1,
    y: 0,
    transition: { duration: 0.9, delay: i * 0.08, ease: LUXURY_EASE },
  }),
}

export default function FeaturedCategories() {
  const rackets = categories.find((c) => c.id === 'rackets')!
  const rest = categories.filter((c) => c.id !== 'rackets')

  return (
    <section className="py-24 md:py-32 bg-[#08080a]">
      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
        {/* Section header */}
        <motion.div
          initial={{ opacity: 0, y: 40 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.9, ease: LUXURY_EASE }}
          className="flex items-end justify-between mb-12 md:mb-16"
        >
          <div>
            <span className="section-label">
              <span className="w-1.5 h-1.5 rounded-full bg-[#c9a55a] inline-block animate-pulse-soft" />
              קטגוריות
            </span>
            <h2 className="section-title mt-3">
              גלה את הציוד{' '}
              <span className="text-gold-gradient">שלך</span>
            </h2>
          </div>
          <Link
            href="/shop"
            className="hidden md:flex items-center gap-2 text-[rgba(242,237,223,0.35)] text-xs uppercase tracking-[0.2em] hover:text-[#c9a55a] transition-colors duration-300 whitespace-nowrap pb-1"
          >
            כל הקטגוריות
            <ArrowLeft className="w-4 h-4" />
          </Link>
        </motion.div>

        {/* Grid */}
        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3">
          {/* Large featured rackets card — col-span-2 row-span-2 */}
          <motion.div
            custom={0}
            variants={cardVariants}
            initial="hidden"
            whileInView="visible"
            viewport={{ once: true, margin: '-80px' }}
            className="col-span-2 row-span-2"
          >
            <Link
              href={categoryHrefs.rackets}
              className="luxury-card-shine group relative block h-48 md:h-[340px] overflow-hidden"
              style={{ background: '#0d0d10' }}
            >
              {/* Gold radial glow on hover */}
              <div
                className="absolute inset-0 opacity-0 transition-opacity duration-700 pointer-events-none"
                style={{
                  background:
                    'radial-gradient(ellipse at 50% 100%, rgba(201,165,90,0.1) 0%, transparent 65%)',
                }}
                aria-hidden="true"
              />

              {/* Slowly rotating racket SVG watermark */}
              <motion.div
                animate={{ rotate: 360 }}
                transition={{ duration: 60, repeat: Infinity, ease: 'linear' }}
                className="absolute inset-0 flex items-center justify-center pointer-events-none opacity-[0.04]"
                aria-hidden="true"
              >
                <svg
                  viewBox="0 0 200 240"
                  className="w-52 h-52 md:w-64 md:h-64"
                  fill="none"
                  stroke="#c9a55a"
                >
                  <ellipse cx="100" cy="90" rx="62" ry="74" strokeWidth="3" />
                  <line x1="38" y1="90" x2="162" y2="90" strokeWidth="1.5" opacity="0.6" />
                  <line x1="100" y1="16" x2="100" y2="164" strokeWidth="1.5" opacity="0.6" />
                  <line x1="54" y1="35" x2="146" y2="145" strokeWidth="1" opacity="0.35" />
                  <line x1="146" y1="35" x2="54" y2="145" strokeWidth="1" opacity="0.35" />
                  <line x1="63" y1="19" x2="63" y2="161" strokeWidth="0.8" opacity="0.2" />
                  <line x1="137" y1="19" x2="137" y2="161" strokeWidth="0.8" opacity="0.2" />
                  <line x1="38" y1="58" x2="162" y2="58" strokeWidth="0.8" opacity="0.2" />
                  <line x1="38" y1="122" x2="162" y2="122" strokeWidth="0.8" opacity="0.2" />
                  <rect x="92" y="163" width="16" height="58" rx="4" fill="#c9a55a" opacity="0.3" strokeWidth="0" />
                </svg>
              </motion.div>

              {/* Content */}
              <div className="absolute inset-0 flex flex-col justify-between p-7 md:p-8">
                <div>
                  <span className="text-[10px] uppercase tracking-[0.28em] text-[rgba(201,165,90,0.55)] font-semibold">
                    המחבטים הנבחרים
                  </span>
                </div>
                <div>
                  <p className="text-[rgba(242,237,223,0.28)] text-xs uppercase tracking-[0.18em] mb-2">
                    <span className="inline-flex items-center gap-1.5">
                      <span className="px-1.5 py-0.5 bg-[rgba(201,165,90,0.15)] border border-[rgba(201,165,90,0.25)] text-[#c9a55a] text-[9px] tracking-wider font-bold">
                        {rackets.count}
                      </span>
                      <span>מוצרים · כל הרמות</span>
                    </span>
                  </p>
                  <h3 className="font-display text-5xl md:text-6xl text-[#f2eddf] tracking-wider uppercase leading-none">
                    מחבטים
                  </h3>
                  <div className="flex items-center gap-2 mt-4 text-[#c9a55a] text-xs font-bold uppercase tracking-[0.2em]">
                    <span>גלה עכשיו</span>
                    <ArrowLeft className="w-3.5 h-3.5 opacity-30 group-hover:opacity-100 group-hover:text-[#c9a55a] transition-all duration-300" />
                  </div>
                </div>
              </div>
            </Link>
          </motion.div>

          {/* Smaller category cards */}
          {rest.map((cat, i) => (
            <motion.div
              key={cat.id}
              custom={i + 1}
              variants={cardVariants}
              initial="hidden"
              whileInView="visible"
              viewport={{ once: true, margin: '-80px' }}
            >
              <Link
                href={categoryHrefs[cat.id] ?? '/shop'}
                className="luxury-card-shine group relative block h-32 md:h-[162px] overflow-hidden"
                style={{ background: '#0d0d10' }}
              >
                {/* Gold glow on hover */}
                <div
                  className="absolute inset-0 opacity-0 transition-opacity duration-500 pointer-events-none"
                  style={{
                    background:
                      'radial-gradient(ellipse at 50% 50%, rgba(201,165,90,0.08) 0%, transparent 70%)',
                  }}
                  aria-hidden="true"
                />

                <div className="relative h-full flex flex-col justify-between p-5">
                  <span className="text-2xl leading-none">{cat.icon}</span>
                  <div>
                    <h3 className="font-display text-xl md:text-2xl text-[#f2eddf] tracking-wider uppercase leading-none mb-1">
                      {cat.label}
                    </h3>
                    <p className="text-[rgba(242,237,223,0.28)] text-xs flex items-center gap-1.5">
                      <span className="inline-flex items-center px-1.5 py-0.5 bg-[rgba(201,165,90,0.12)] border border-[rgba(201,165,90,0.2)] text-[#c9a55a] text-[9px] tracking-wider font-bold">
                        {cat.count}
                      </span>
                      מוצרים
                    </p>
                  </div>
                </div>

                <ArrowLeft
                  className="absolute bottom-4 left-4 w-4 h-4 opacity-30 group-hover:opacity-100 group-hover:text-[#c9a55a] transition-all duration-300"
                  aria-hidden="true"
                />
              </Link>
            </motion.div>
          ))}
        </div>

        {/* Mobile "all categories" link */}
        <div className="flex md:hidden justify-center mt-8">
          <Link
            href="/shop"
            className="flex items-center gap-2 text-[rgba(242,237,223,0.35)] text-xs uppercase tracking-[0.2em] hover:text-[#c9a55a] transition-colors"
          >
            כל הקטגוריות
            <ArrowLeft className="w-4 h-4" />
          </Link>
        </div>
      </div>
    </section>
  )
}
