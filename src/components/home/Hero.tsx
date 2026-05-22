'use client'

import { motion } from 'framer-motion'
import Link from 'next/link'
import { ArrowLeft, ChevronDown } from 'lucide-react'

export default function Hero() {
  const container = {
    hidden: {},
    show: { transition: { staggerChildren: 0.15, delayChildren: 0.4 } },
  }

  const item = {
    hidden: { opacity: 0, y: 50 },
    show: { opacity: 1, y: 0, transition: { duration: 1.0, ease: [0.16, 1, 0.3, 1] } },
  }

  return (
    <section className="relative min-h-screen flex flex-col items-center justify-center overflow-hidden bg-brand-bg">
      {/* Slow cinematic orbs */}
      <div
        className="absolute top-[20%] left-[15%] w-[700px] h-[500px] pointer-events-none animate-orb-drift-1"
        style={{
          background: 'radial-gradient(ellipse, rgba(181,247,46,0.07) 0%, transparent 65%)',
          filter: 'blur(80px)',
        }}
        aria-hidden="true"
      />
      <div
        className="absolute bottom-[10%] right-[10%] w-[500px] h-[400px] pointer-events-none animate-orb-drift-2"
        style={{
          background: 'radial-gradient(ellipse, rgba(201,164,85,0.05) 0%, transparent 65%)',
          filter: 'blur(100px)',
        }}
        aria-hidden="true"
      />

      {/* Grid background */}
      <div className="absolute inset-0 bg-grid-fine opacity-40" aria-hidden="true" />

      {/* Top & bottom accent lines */}
      <div className="absolute top-0 left-0 right-0 h-px bg-gradient-to-l from-transparent via-[#b5f72e]/20 to-transparent" aria-hidden="true" />

      {/* Content */}
      <div className="relative z-10 max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 pt-28 pb-20 text-center">
        <motion.div variants={container} initial="hidden" animate="show">

          {/* Season badge */}
          <motion.div variants={item} className="flex justify-center mb-10">
            <div className="inline-flex items-center gap-2.5 px-5 py-2 border border-[#b5f72e]/25 bg-[#b5f72e]/[0.04] backdrop-blur-sm">
              <div className="w-1.5 h-1.5 bg-[#b5f72e] rounded-full animate-pulse" />
              <span className="text-[#b5f72e] text-[11px] font-bold uppercase tracking-[0.3em]">
                קולקציית 2025
              </span>
              <ArrowLeft className="w-3 h-3 text-[#b5f72e]" />
            </div>
          </motion.div>

          {/* Main headline — Hebrew cinematic display */}
          <motion.div variants={item}>
            <h1 className="font-display text-[11vw] sm:text-[9vw] md:text-[7.5vw] lg:text-[7rem] xl:text-[8rem] leading-[0.88] tracking-wide uppercase text-white mb-1">
              הדור הבא
            </h1>
            <h1
              className="font-display text-[11vw] sm:text-[9vw] md:text-[7.5vw] lg:text-[7rem] xl:text-[8rem] leading-[0.88] tracking-wide uppercase mb-10"
              style={{ color: '#b5f72e' }}
            >
              של הפאדל
            </h1>
          </motion.div>

          {/* Hebrew brand line */}
          <motion.div variants={item}>
            <h2 className="font-display text-[6vw] sm:text-[4.5vw] md:text-[3.5vw] lg:text-5xl xl:text-6xl leading-[1] tracking-[0.3em] uppercase text-white/20 mb-12">
              בישראל
            </h2>
          </motion.div>

          {/* Subheadline */}
          <motion.p
            variants={item}
            className="text-white/45 text-base md:text-lg lg:text-xl font-light tracking-wide max-w-xl mx-auto mb-14 leading-relaxed"
          >
            ציוד פאדל פרימיום לשחקנים שרוצים יותר.
          </motion.p>

          {/* CTA Buttons */}
          <motion.div variants={item} className="flex flex-col sm:flex-row items-center justify-center gap-4">
            <Link
              href="/shop"
              className="group inline-flex items-center gap-3 px-8 py-4 bg-[#b5f72e] text-black font-bold text-sm uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-md transition-all duration-300"
            >
              <span>קנה עכשיו</span>
              <ArrowLeft className="w-4 h-4 group-hover:-translate-x-1 transition-transform" />
            </Link>
            <Link
              href="/shop?category=rackets"
              className="inline-flex items-center gap-3 px-8 py-4 border border-white/15 text-white/70 font-medium text-sm uppercase tracking-widest hover:border-[#b5f72e]/40 hover:text-white transition-all duration-300"
            >
              מצא את המחבט שלך
            </Link>
          </motion.div>

          {/* Stats Row */}
          <motion.div
            variants={item}
            className="flex flex-wrap items-center justify-center gap-10 md:gap-16 mt-20 pt-12 border-t border-white/[0.05]"
          >
            {[
              { value: '+500', label: 'מוצרים פרימיום' },
              { value: '+10K', label: 'שחקנים מרוצים' },
              { value: '24ש׳', label: 'משלוח מהיר' },
              { value: '30 יום', label: 'החזרה חינם' },
            ].map(stat => (
              <div key={stat.label} className="text-center">
                <p className="text-2xl md:text-3xl font-display text-[#b5f72e] tracking-wide ltr-text">{stat.value}</p>
                <p className="text-white/25 text-[10px] uppercase tracking-[0.2em] mt-1">{stat.label}</p>
              </div>
            ))}
          </motion.div>
        </motion.div>
      </div>

      {/* Scroll indicator */}
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 2.2, duration: 1 }}
        className="absolute bottom-8 left-1/2 -translate-x-1/2 flex flex-col items-center gap-2 text-white/15"
      >
        <span className="text-[10px] uppercase tracking-[0.4em]">גלול</span>
        <motion.div
          animate={{ y: [0, 8, 0] }}
          transition={{ duration: 2, repeat: Infinity, ease: 'easeInOut' }}
        >
          <ChevronDown className="w-4 h-4" />
        </motion.div>
      </motion.div>
    </section>
  )
}
