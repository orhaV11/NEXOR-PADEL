'use client'

import { motion, useScroll, useTransform } from 'framer-motion'
import Link from 'next/link'
import { ArrowLeft } from 'lucide-react'

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
  const { scrollY } = useScroll()
  const headlineY = useTransform(scrollY, [0, 400], [0, -60])
  const heroOpacity = useTransform(scrollY, [0, 300], [1, 0])

  return (
    <section className="relative min-h-screen flex flex-col items-center justify-center overflow-hidden bg-[#08080a]">
      {/* Top gold accent line */}
      <div className="gold-line absolute top-0 inset-x-0 z-10" />

      {/* Pulsing radial background texture */}
      <motion.div
        className="absolute inset-0 pointer-events-none"
        animate={{ opacity: [0.4, 0.6, 0.4] }}
        transition={{ duration: 6, repeat: Infinity, ease: 'easeInOut' }}
        style={{ background: 'radial-gradient(ellipse 80% 60% at 50% -5%, rgba(201,165,90,0.08) 0%, transparent 65%)' }}
      />

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
      {/* Third orb — emerald for depth */}
      <div
        aria-hidden="true"
        className="animate-orb-drift-2 absolute top-[10%] left-[-10%] w-[500px] h-[500px] rounded-full pointer-events-none"
        style={{
          background: 'radial-gradient(circle, rgba(27,77,56,0.12) 0%, transparent 65%)',
          filter: 'blur(90px)',
          animationDelay: '-8s',
        }}
      />

      {/* Grid overlay at 30% opacity */}
      <div
        aria-hidden="true"
        className="bg-grid absolute inset-0 pointer-events-none"
        style={{ opacity: 0.3 }}
      />

      {/* Main content */}
      <motion.div
        style={{ y: headlineY, opacity: heroOpacity }}
        className="relative z-10 w-full max-w-7xl mx-auto px-6 md:px-12 text-center flex flex-col items-center pt-28 pb-32 sm:pb-20"
      >
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
            className="font-display text-[22vw] sm:text-[14vw] lg:text-[12rem] text-[#f2eddf] leading-none tracking-wide mt-4 select-none"
          >
            פאדל
          </motion.h1>

          {/* Headline line 2 — animated gold shimmer gradient */}
          <motion.div
            variants={itemVariants}
            className="leading-none tracking-wide -mt-4 md:-mt-6 select-none"
          >
            <motion.div
              animate={{ backgroundPosition: ['0% 50%', '100% 50%', '0% 50%'] }}
              transition={{ duration: 5, repeat: Infinity, ease: 'linear' }}
              className="font-display text-[22vw] sm:text-[14vw] lg:text-[12rem] bg-clip-text"
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
            className="flex items-center justify-center gap-2 mt-6"
          >
            <span className="section-label text-xs">חנות פאדל פרימיום בישראל</span>
          </motion.div>

          {/* Subheadline */}
          <motion.p
            variants={itemVariants}
            className="text-[rgba(242,237,223,0.5)] text-lg md:text-xl font-light mt-4 max-w-lg"
          >
            מחבטים, נעליים, כדורים ואביזרים ממותגים מובילים בעולם.
          </motion.p>

          {/* CTA row — stacks on mobile */}
          <motion.div
            variants={itemVariants}
            className="flex flex-col sm:flex-row items-center justify-center gap-3 mt-9 w-full px-4 sm:px-0"
          >
            <Link href="/shop" className="btn-gold w-full sm:w-auto min-h-[52px]">
              <ArrowLeft className="w-4 h-4" />
              קנה עכשיו
            </Link>
            <Link href="/racket-guide" className="btn-outline w-full sm:w-auto min-h-[52px]">
              מצא את המחבט שלך
            </Link>
          </motion.div>

          {/* Thin divider */}
          <motion.div variants={itemVariants} className="w-full max-w-md mt-14">
            <div className="divider" />
          </motion.div>

          {/* Stats — 2x2 grid on mobile, single row on sm+ */}
          <motion.div
            variants={itemVariants}
            className="grid grid-cols-2 sm:flex sm:flex-wrap items-center justify-center gap-6 sm:gap-14 mt-8 w-full"
          >
            {stats.map((stat, i) => (
              <motion.div
                key={stat.label}
                className="flex flex-col items-center gap-1"
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 1.4 + i * 0.1, duration: 0.7, ease: LUXURY_EASE }}
              >
                <motion.span
                  className="font-display text-3xl md:text-4xl ltr"
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
                <span className="text-[rgba(242,237,223,0.38)] text-[10px] uppercase tracking-[0.22em]">
                  {stat.label}
                </span>
              </motion.div>
            ))}
          </motion.div>
        </motion.div>
      </motion.div>

      {/* Scroll indicator — animated line */}
      <motion.div
        className="absolute bottom-8 left-1/2 -translate-x-1/2 z-10 flex flex-col items-center gap-1"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 2.2, duration: 0.9 }}
      >
        <span className="text-[9px] tracking-[0.4em] text-[rgba(242,237,223,0.2)] uppercase">גלול</span>
        <div className="relative h-12 w-px bg-[rgba(201,165,90,0.12)]">
          <motion.div
            className="absolute top-0 left-0 right-0 bg-[#c9a55a]"
            animate={{ height: ['0%', '100%', '0%'] }}
            transition={{ duration: 2, repeat: Infinity, ease: 'easeInOut' }}
          />
        </div>
      </motion.div>
    </section>
  )
}
