'use client'

import { useRef } from 'react'
import { motion, useScroll, useTransform } from 'framer-motion'
import Link from 'next/link'
import { Award, Truck, RotateCcw, Headphones, Shield, Zap } from 'lucide-react'

const LUXURY_EASE = [0.16, 1, 0.3, 1] as const

/* ── Animated counter ─────────────────────────── */
function AnimatedNumber({ value, suffix = '' }: { value: number; suffix?: string }) {
  const ref = useRef<HTMLSpanElement>(null)
  const { scrollYProgress } = useScroll({
    target: ref,
    offset: ['start end', 'center center'],
  })
  const count = useTransform(scrollYProgress, [0, 1], [0, value])
  const rounded = useTransform(count, (v) => Math.round(v).toLocaleString())

  return (
    <motion.span ref={ref} style={{ fontVariantNumeric: 'tabular-nums' }}>
      {rounded}
    </motion.span>
  )
}

/* ── Stats ────────────────────────────────────── */
const stats = [
  { value: 10000, suffix: '+', label: 'שחקנים מרוצים' },
  { value: 14, suffix: '+', label: 'מותגים מובילים' },
  { value: 30, suffix: ' יום', label: 'אחריות החזרה' },
  { value: 24, suffix: 'H', label: 'משלוח אקספרס' },
]

/* ── Features ─────────────────────────────────── */
const features = [
  {
    icon: Award,
    title: 'מוצרים מקוריים 100%',
    description: 'ספק מורשה רשמי של כל המותגים המובילים. כל מוצר מגיע עם אחריות יצרן מקורית.',
  },
  {
    icon: Truck,
    title: 'משלוח חינם מ-₪280',
    description: 'משלוח מהיר לכל הארץ. הזמנות אקספרס מגיעות תוך 24 שעות.',
  },
  {
    icon: RotateCcw,
    title: 'החזרה חינם 30 יום',
    description: 'לא מתאים? מחזירים ללא שאלות תוך 30 יום. פשוט וקל.',
  },
  {
    icon: Headphones,
    title: 'מומחי פאדל',
    description: 'ייעוץ מקצועי מצוות שחקנים מנוסים שיעזרו לך לבחור נכון.',
  },
  {
    icon: Shield,
    title: 'תשלום מאובטח',
    description: 'הצפנה מלאה SSL. ויזה, מסטרקארד, Apple Pay, Google Pay.',
  },
  {
    icon: Zap,
    title: 'בחירה פרימיום',
    description: 'כל מוצר עבר בדיקה קפדנית לפני שנכנס לקטלוג. רק הטוב ביותר.',
  },
]

export default function WhyNexor() {
  return (
    <section className="py-28 md:py-36 bg-[#07070a] relative overflow-hidden">
      {/* bg-grid overlay at ~3% opacity */}
      <div
        aria-hidden="true"
        className="absolute inset-0 bg-grid pointer-events-none"
        style={{ opacity: 0.03 }}
      />

      {/* Ambient glow */}
      <div
        aria-hidden="true"
        className="absolute top-0 left-0 w-[500px] h-[500px] rounded-full pointer-events-none opacity-[0.04]"
        style={{
          background: 'radial-gradient(circle, #c9a55a 0%, transparent 70%)',
          filter: 'blur(80px)',
        }}
      />
      <div
        aria-hidden="true"
        className="absolute bottom-0 right-0 w-[400px] h-[400px] rounded-full pointer-events-none opacity-[0.03]"
        style={{
          background: 'radial-gradient(circle, #c9a55a 0%, transparent 70%)',
          filter: 'blur(100px)',
        }}
      />

      <div className="max-w-7xl mx-auto px-6 sm:px-10 lg:px-12">
        {/* Section header */}
        <motion.div
          initial={{ opacity: 0, y: 40 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.9, ease: LUXURY_EASE }}
          className="text-center mb-20"
        >
          <span className="section-label mx-auto">
            <span className="w-1.5 h-1.5 rounded-full bg-[#c9a55a] inline-block animate-pulse-soft" />
            למה NEXOR
          </span>
          <h2 className="section-title mt-3">
            ההבדל שמשנה{' '}
            <span className="text-gold-gradient">כל דבר</span>
          </h2>
          <p className="text-[rgba(242,237,223,0.4)] mt-5 max-w-xl mx-auto text-sm leading-relaxed">
            אנחנו לא רק חנות. אנחנו יעד פאדל שנבנה עבור שחקנים שדורשים את הטוב ביותר.
          </p>
        </motion.div>

        {/* 2-column layout on desktop */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-14 lg:gap-20 items-start">
          {/* Left column — Stats */}
          <motion.div
            initial={{ opacity: 0, x: 40 }}
            whileInView={{ opacity: 1, x: 0 }}
            viewport={{ once: true, margin: '-80px' }}
            transition={{ duration: 0.9, ease: LUXURY_EASE }}
            className="grid grid-cols-2 gap-px bg-[rgba(201,165,90,0.08)] border border-[rgba(201,165,90,0.1)]"
          >
            {stats.map((stat) => (
              <div
                key={stat.label}
                className="bg-[#07070a] p-10 flex flex-col items-center text-center"
              >
                <p className="stat-number ltr mb-2">
                  <AnimatedNumber value={stat.value} suffix={stat.suffix} />
                  <span>{stat.suffix}</span>
                </p>
                <p className="text-[rgba(242,237,223,0.45)] text-xs uppercase tracking-widest mt-1">
                  {stat.label}
                </p>
              </div>
            ))}
          </motion.div>

          {/* Right column — Feature cards */}
          <div className="space-y-4">
            {features.map((feature, i) => {
              const Icon = feature.icon
              return (
                <motion.div
                  key={feature.title}
                  initial={{ opacity: 0, y: 30 }}
                  whileInView={{ opacity: 1, y: 0 }}
                  viewport={{ once: true, margin: '-80px' }}
                  transition={{ duration: 0.7, delay: i * 0.08, ease: LUXURY_EASE }}
                  className="glass-card card-gold-top relative flex items-start gap-6 p-6 overflow-hidden border-r-2 border-transparent hover:border-[#c9a55a] transition-colors duration-300"
                >
                  {/* Hover glow */}
                  <div
                    className="absolute inset-0 opacity-0 transition-opacity duration-500 pointer-events-none"
                    style={{
                      background:
                        'radial-gradient(ellipse at top right, rgba(201,165,90,0.06) 0%, transparent 65%)',
                    }}
                    aria-hidden="true"
                  />

                  {/* Diamond icon container */}
                  <div className="relative w-12 h-12 shrink-0">
                    <div
                      className="absolute inset-0 rotate-45"
                      style={{
                        background:
                          'linear-gradient(135deg, rgba(201,165,90,0.15) 0%, rgba(201,165,90,0.05) 100%)',
                        border: '1px solid rgba(201,165,90,0.35)',
                      }}
                    />
                    <div className="relative w-full h-full flex items-center justify-center">
                      <Icon className="w-5 h-5 text-[#c9a55a]" aria-hidden="true" />
                    </div>
                  </div>

                  <div className="relative min-w-0 pt-0.5">
                    <h3 className="text-[#f2eddf] font-bold text-sm mb-2 leading-snug">
                      {feature.title}
                    </h3>
                    <p className="text-[rgba(242,237,223,0.4)] text-sm leading-relaxed">
                      {feature.description}
                    </p>
                  </div>
                </motion.div>
              )
            })}
          </div>
        </div>

        {/* Bottom CTA */}
        <motion.div
          initial={{ opacity: 0, y: 40 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.9, delay: 0.3, ease: LUXURY_EASE }}
          className="text-center mt-24 pt-16 border-t border-[rgba(242,237,223,0.06)]"
        >
          <p className="font-display text-3xl md:text-5xl text-[#f2eddf] tracking-wide leading-tight mb-8">
            המשחק הבא שלך מתחיל כאן
          </p>
          <Link href="/shop" className="btn-gold inline-flex">
            קנה ציוד פרימיום
          </Link>
        </motion.div>
      </div>
    </section>
  )
}
