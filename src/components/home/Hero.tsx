'use client'

import { motion } from 'framer-motion'
import Link from 'next/link'
import { ArrowLeft, ChevronDown } from 'lucide-react'

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
  { value: '+500', label: 'מוצרים' },
  { value: '+10,000', label: 'שחקנים' },
  { value: '24ש׳', label: 'משלוח' },
  { value: '30 יום', label: 'החזרה' },
]

export default function Hero() {
  return (
    <section className="relative min-h-screen flex flex-col items-center justify-center overflow-hidden bg-[#08080a]">
      {/* Top gold accent line */}
      <div className="gold-line absolute top-0 inset-x-0 z-10" />

      {/* Drifting orbs */}
      <div
        aria-hidden="true"
        className="animate-orb-drift-1 absolute top-[-18%] right-[-8%] w-[700px] h-[700px] rounded-full pointer-events-none"
        style={{
          background: 'radial-gradient(circle, rgba(201,165,90,0.14) 0%, transparent 65%)',
          filter: 'blur(60px)',
        }}
      />
      <div
        aria-hidden="true"
        className="animate-orb-drift-2 absolute bottom-[-14%] left-[-6%] w-[600px] h-[600px] rounded-full pointer-events-none"
        style={{
          background: 'radial-gradient(circle, rgba(27,77,56,0.2) 0%, transparent 65%)',
          filter: 'blur(80px)',
        }}
      />
      <div
        aria-hidden="true"
        className="animate-orb-drift-3 absolute top-[35%] left-[18%] w-[380px] h-[380px] rounded-full pointer-events-none"
        style={{
          background: 'radial-gradient(circle, rgba(201,165,90,0.07) 0%, transparent 65%)',
          filter: 'blur(50px)',
        }}
      />

      {/* Grid overlay at 30% opacity */}
      <div
        aria-hidden="true"
        className="bg-grid absolute inset-0 pointer-events-none"
        style={{ opacity: 0.3 }}
      />

      {/* Main content */}
      <div className="relative z-10 w-full max-w-7xl mx-auto px-6 md:px-12 text-center flex flex-col items-center pt-28 pb-20">
        <motion.div
          variants={containerVariants}
          initial="hidden"
          animate="visible"
          className="flex flex-col items-center w-full"
        >
          {/* Section label badge */}
          <motion.div variants={itemVariants}>
            <span className="section-label">
              <span
                className="w-1.5 h-1.5 rounded-full bg-[#c9a55a] inline-block animate-pulse-soft"
                aria-hidden="true"
              />
              קולקציית 2025
            </span>
          </motion.div>

          {/* Headline line 1 — ivory display */}
          <motion.h1
            variants={itemVariants}
            className="font-display text-[18vw] sm:text-[14vw] lg:text-[12rem] text-[#f2eddf] leading-none tracking-wide mt-4 select-none"
          >
            פאדל
          </motion.h1>

          {/* Headline line 2 — gold gradient display */}
          <motion.div
            variants={itemVariants}
            className="font-display text-[18vw] sm:text-[14vw] lg:text-[12rem] leading-none tracking-wide -mt-4 md:-mt-6 select-none"
          >
            <span className="text-gold-gradient">ברמה אחרת</span>
          </motion.div>

          {/* Subheadline */}
          <motion.p
            variants={itemVariants}
            className="text-[rgba(242,237,223,0.5)] text-lg md:text-xl font-light mt-7 max-w-lg"
          >
            ציוד פרימיום לשחקנים שלא מתפשרים.
          </motion.p>

          {/* CTA row */}
          <motion.div
            variants={itemVariants}
            className="flex flex-wrap items-center justify-center gap-3 mt-9"
          >
            <Link href="/shop" className="btn-gold">
              <ArrowLeft className="w-4 h-4" />
              קנה עכשיו
            </Link>
            <Link href="/racket-guide" className="btn-outline">
              מצא את המחבט שלך
            </Link>
          </motion.div>

          {/* Thin divider */}
          <motion.div variants={itemVariants} className="w-full max-w-md mt-14">
            <div className="divider" />
          </motion.div>

          {/* Stats row */}
          <motion.div
            variants={itemVariants}
            className="flex flex-wrap items-center justify-center gap-8 md:gap-14 mt-8"
          >
            {stats.map((stat) => (
              <div key={stat.label} className="flex flex-col items-center gap-1">
                <span className="font-display text-3xl md:text-4xl text-gold-gradient ltr">
                  {stat.value}
                </span>
                <span className="text-[rgba(242,237,223,0.38)] text-[10px] uppercase tracking-[0.22em]">
                  {stat.label}
                </span>
              </div>
            ))}
          </motion.div>
        </motion.div>
      </div>

      {/* Scroll indicator */}
      <motion.div
        className="absolute bottom-8 left-1/2 -translate-x-1/2 z-10 flex flex-col items-center gap-2"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 2.2, duration: 0.9 }}
      >
        <span className="text-[rgba(242,237,223,0.22)] text-[10px] uppercase tracking-[0.35em]">
          גלול
        </span>
        <motion.div
          animate={{ y: [0, 8, 0] }}
          transition={{ duration: 1.6, repeat: Infinity, ease: 'easeInOut' }}
        >
          <ChevronDown className="w-5 h-5 text-[rgba(201,165,90,0.45)]" />
        </motion.div>
      </motion.div>
    </section>
  )
}
