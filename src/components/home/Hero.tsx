'use client'

import { motion, useScroll, useTransform, AnimatePresence } from 'framer-motion'
import Link from 'next/link'
import Image from 'next/image'
import { ArrowLeft } from 'lucide-react'
import { useEffect, useState } from 'react'

const LUXURY_EASE = [0.16, 1, 0.3, 1] as const

const containerVariants = {
  hidden: {},
  visible: {
    transition: {
      staggerChildren: 0.1,
      delayChildren: 0.2,
    },
  },
}

const itemVariants = {
  hidden: { opacity: 0, y: 60 },
  visible: {
    opacity: 1,
    y: 0,
    transition: { duration: 1.2, ease: LUXURY_EASE },
  },
}

const stats = [
  { value: '10K+', label: 'שחקנים' },
  { value: '14',   label: 'מותגי עילית' },
  { value: '30',   label: 'יום החזרה' },
  { value: '24H',  label: 'משלוח מהיר' },
]

const trustItems = [
  'משלוח חינם על הזמנות מעל ₪299',
  'אחריות יצרן על כל המחבטים',
  'ייעוץ מקצועי ממומחי פאדל',
]

export default function Hero() {
  const { scrollY } = useScroll()
  const headlineY = useTransform(scrollY, [0, 400], [0, -60])
  const heroOpacity = useTransform(scrollY, [0, 300], [1, 0])
  const [mounted, setMounted] = useState(false)

  useEffect(() => {
    setMounted(true)
  }, [])

  return (
    <section className="relative min-h-screen flex flex-col items-center justify-center overflow-hidden bg-[#08080a]">
      {/* Top gold accent line */}
      <div className="gold-line absolute top-0 inset-x-0 z-10" />

      {/* Pulsing radial top arc */}
      <motion.div
        className="absolute inset-0 pointer-events-none"
        animate={{ opacity: [0.4, 0.65, 0.4] }}
        transition={{ duration: 6, repeat: Infinity, ease: 'easeInOut' }}
        style={{ background: 'radial-gradient(ellipse 80% 60% at 50% -5%, rgba(201,165,90,0.09) 0%, transparent 65%)' }}
      />

      {/* Bottom gold radial arc */}
      <div
        aria-hidden="true"
        className="absolute bottom-0 inset-x-0 h-[380px] pointer-events-none"
        style={{
          background: 'radial-gradient(ellipse 90% 60% at 50% 110%, rgba(201,165,90,0.07) 0%, transparent 70%)',
        }}
      />

      {/* Orb 1 — gold top-right */}
      <div
        aria-hidden="true"
        className="animate-orb-drift-1 absolute top-[-18%] right-[-8%] w-[700px] h-[700px] rounded-full pointer-events-none"
        style={{
          background: 'radial-gradient(circle, rgba(201,165,90,0.14) 0%, transparent 65%)',
          filter: 'blur(60px)',
        }}
      />
      {/* Orb 2 — emerald bottom-left */}
      <div
        aria-hidden="true"
        className="animate-orb-drift-2 absolute bottom-[-14%] left-[-6%] w-[600px] h-[600px] rounded-full pointer-events-none"
        style={{
          background: 'radial-gradient(circle, rgba(27,77,56,0.2) 0%, transparent 65%)',
          filter: 'blur(80px)',
        }}
      />
      {/* Orb 3 — faint gold mid-left */}
      <div
        aria-hidden="true"
        className="animate-orb-drift-3 absolute top-[35%] left-[18%] w-[380px] h-[380px] rounded-full pointer-events-none"
        style={{
          background: 'radial-gradient(circle, rgba(201,165,90,0.07) 0%, transparent 65%)',
          filter: 'blur(50px)',
        }}
      />
      {/* Orb 4 — faint gold bottom-right (new) */}
      <div
        aria-hidden="true"
        className="animate-orb-drift-2 absolute bottom-[-10%] right-[-5%] w-[550px] h-[550px] rounded-full pointer-events-none"
        style={{
          background: 'radial-gradient(circle, rgba(201,165,90,0.06) 0%, transparent 65%)',
          filter: 'blur(90px)',
          animationDelay: '-12s',
        }}
      />

      {/* Grid overlay */}
      <div
        aria-hidden="true"
        className="bg-grid absolute inset-0 pointer-events-none"
        style={{ opacity: 0.3 }}
      />

      {/* ── Main content wrapper ── */}
      <motion.div
        style={{ y: headlineY, opacity: heroOpacity }}
        className="relative z-10 w-full max-w-7xl mx-auto px-6 md:px-12 pt-28 pb-24"
      >
        {/* Desktop grid: text (right in RTL) + floating panel (left in RTL) */}
        <div className="flex flex-col lg:grid lg:grid-cols-[1fr_auto] lg:gap-16 lg:items-center">

          {/* ── LEFT COLUMN (RTL = text side) ── */}
          <motion.div
            variants={containerVariants}
            initial="hidden"
            animate="visible"
            className="flex flex-col items-center lg:items-start text-center lg:text-start"
          >
            {/* Brand logo mark */}
            <motion.div variants={itemVariants}>
              <Image
                src="/images/nexor-logo.webp"
                alt="NEXOR Padel"
                width={200}
                height={200}
                className="h-20 sm:h-24 lg:h-28 w-auto mx-auto lg:mx-0 -mb-2"
                style={{ mixBlendMode: 'screen' }}
                priority
              />
            </motion.div>

            {/* Headline line 1 — giant ivory display */}
            <motion.h1
              variants={itemVariants}
              className="font-display text-[25vw] sm:text-[16vw] lg:text-[13rem] text-[#f2eddf] leading-none tracking-wide mt-2 select-none"
            >
              פאדל
            </motion.h1>

            {/* Headline line 2 — animated gold shimmer */}
            <motion.div
              variants={itemVariants}
              className="leading-none tracking-wide -mt-4 md:-mt-6 select-none"
            >
              <motion.div
                animate={{ backgroundPosition: ['0% 50%', '100% 50%', '0% 50%'] }}
                transition={{ duration: 5, repeat: Infinity, ease: 'linear' }}
                className="font-display text-[25vw] sm:text-[16vw] lg:text-[13rem] bg-clip-text"
                style={{
                  backgroundImage: 'linear-gradient(135deg, #b08840 0%, #e2c890 25%, #c9a55a 50%, #f5d98a 65%, #c9a55a 80%, #b08840 100%)',
                  backgroundSize: '300% 100%',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                }}
              >
                ברמה אחרת
              </motion.div>
            </motion.div>

            {/* Store identity badge */}
            <motion.div
              variants={itemVariants}
              className="flex items-center justify-center lg:justify-start gap-2 mt-5"
            >
              <span className="section-label text-xs">חנות פאדל פרימיום בישראל</span>
            </motion.div>

            {/* Subheadline */}
            <motion.p
              variants={itemVariants}
              className="text-[rgba(242,237,223,0.5)] text-lg md:text-xl font-light mt-3 max-w-lg"
            >
              מחבטים, נעליים, כדורים ואביזרים ממותגים מובילים בעולם.
            </motion.p>

            {/* CTA row */}
            <motion.div
              variants={itemVariants}
              className="flex flex-col sm:flex-row items-center justify-center lg:justify-start gap-3 mt-8 w-full lg:w-auto px-4 sm:px-0"
            >
              <Link href="/shop" className="btn-gold w-full sm:w-auto min-h-[52px]">
                <ArrowLeft className="w-4 h-4" />
                לחנות
              </Link>
              <Link href="/racket-guide" className="btn-outline w-full sm:w-auto min-h-[52px]">
                מדריך מחבטים
              </Link>
            </motion.div>

            {/* Divider */}
            <motion.div variants={itemVariants} className="w-full max-w-md mt-12">
              <div className="divider" />
            </motion.div>

            {/* Stats — vertical with gold separator lines */}
            <motion.div
              variants={itemVariants}
              className="flex items-stretch justify-center lg:justify-start mt-8 w-full"
            >
              {stats.map((stat, i) => (
                <div key={stat.label} className="flex items-stretch">
                  <motion.div
                    className="flex flex-col items-center gap-1 px-5 sm:px-7"
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: 1.4 + i * 0.1, duration: 0.7, ease: LUXURY_EASE }}
                  >
                    <motion.span
                      className="font-playfair text-3xl md:text-4xl ltr font-bold"
                      style={{
                        backgroundImage: 'linear-gradient(135deg, #c9a55a 0%, #e2c890 50%, #c9a55a 100%)',
                        WebkitBackgroundClip: 'text',
                        WebkitTextFillColor: 'transparent',
                        backgroundClip: 'text',
                      }}
                      initial={{ opacity: 0, scale: 0.85 }}
                      animate={{ opacity: 1, scale: 1 }}
                      transition={{ delay: 1.5 + i * 0.12, duration: 0.6, ease: LUXURY_EASE }}
                    >
                      {stat.value}
                    </motion.span>
                    <span className="text-[rgba(242,237,223,0.38)] text-[10px] uppercase tracking-[0.22em] whitespace-nowrap">
                      {stat.label}
                    </span>
                  </motion.div>
                  {/* Gold vertical separator — not after last item */}
                  {i < stats.length - 1 && (
                    <div
                      className="w-px self-stretch mx-0"
                      style={{ background: 'linear-gradient(to bottom, transparent, rgba(201,165,90,0.28), transparent)' }}
                    />
                  )}
                </div>
              ))}
            </motion.div>
          </motion.div>

          {/* ── RIGHT COLUMN (RTL = floating panel side, desktop only) ── */}
          <AnimatePresence>
            {mounted && (
              <motion.div
                className="hidden lg:flex lg:flex-col lg:items-end"
                initial={{ opacity: 0, x: -40 }}
                animate={{ opacity: 1, x: 0 }}
                transition={{ delay: 0.8, duration: 1.0, ease: LUXURY_EASE }}
              >
                <motion.div
                  animate={{ y: [0, -8, 0] }}
                  transition={{ duration: 6, repeat: Infinity, ease: 'easeInOut' }}
                  className="glass rounded-sm p-7 flex flex-col gap-5"
                  style={{ width: '280px' }}
                >
                  {/* Panel title */}
                  <div>
                    <p className="text-[rgba(242,237,223,0.38)] text-[9px] uppercase tracking-[0.3em] mb-2">
                      למה לבחור בנו
                    </p>
                    <h2 className="text-ivory text-base font-semibold leading-snug" style={{ fontFamily: 'var(--font-heebo)' }}>
                      החנות הפרימיום<br />לפאדל בישראל
                    </h2>
                  </div>

                  {/* Thin divider */}
                  <div className="divider" />

                  {/* Trust bullet items */}
                  <ul className="flex flex-col gap-3">
                    {trustItems.map((item) => (
                      <li key={item} className="flex items-start gap-3 text-[rgba(242,237,223,0.65)] text-sm leading-snug">
                        <span
                          className="mt-1 shrink-0 w-1.5 h-1.5 rounded-full bg-[#c9a55a] opacity-80"
                          aria-hidden="true"
                        />
                        {item}
                      </li>
                    ))}
                  </ul>

                  {/* Thin divider */}
                  <div className="divider" />

                  {/* Panel CTA */}
                  <Link href="/shop" className="btn-gold w-full justify-center min-h-[44px] text-xs cursor-pointer">
                    <ArrowLeft className="w-3.5 h-3.5" />
                    לחנות המלאה
                  </Link>
                </motion.div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </motion.div>

      {/* ── Scroll indicator — vertical line with running dot ── */}
      <motion.div
        className="absolute bottom-8 left-1/2 -translate-x-1/2 z-10 flex flex-col items-center gap-2"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 2.2, duration: 0.9 }}
        aria-hidden="true"
      >
        <span className="text-[9px] tracking-[0.4em] text-[rgba(242,237,223,0.18)] uppercase">גלול</span>
        {/* Track */}
        <div className="relative h-14 w-px bg-[rgba(201,165,90,0.1)]">
          {/* Running dot */}
          <motion.div
            className="absolute left-1/2 -translate-x-1/2 w-1 h-1 rounded-full bg-[#c9a55a]"
            animate={{ top: ['0%', '100%', '0%'] }}
            transition={{ duration: 2.4, repeat: Infinity, ease: 'easeInOut' }}
          />
          {/* Filling line */}
          <motion.div
            className="absolute top-0 left-0 right-0 bg-gradient-to-b from-[#c9a55a] to-transparent"
            animate={{ height: ['0%', '100%', '0%'] }}
            transition={{ duration: 2.4, repeat: Infinity, ease: 'easeInOut' }}
          />
        </div>
      </motion.div>
    </section>
  )
}
