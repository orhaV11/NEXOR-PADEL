'use client'

import { motion } from 'framer-motion'
import Link from 'next/link'
import { Award, Truck, RotateCcw, Headphones, Shield, Zap } from 'lucide-react'

const LUXURY_EASE = [0.16, 1, 0.3, 1] as const

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
    <section className="py-24 md:py-32 bg-[#07070a] relative overflow-hidden">
      {/* Subtle gold orb top right */}
      <div
        aria-hidden="true"
        className="absolute top-0 left-0 w-[500px] h-[500px] rounded-full pointer-events-none opacity-[0.04]"
        style={{
          background: 'radial-gradient(circle, #c9a55a 0%, transparent 70%)',
          filter: 'blur(80px)',
        }}
      />

      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
        {/* Section header */}
        <motion.div
          initial={{ opacity: 0, y: 40 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.9, ease: LUXURY_EASE }}
          className="text-center mb-16"
        >
          <span className="section-label mx-auto">למה NEXOR</span>
          <h2 className="section-title mt-3">
            ההבדל שמשנה{' '}
            <span className="text-gold-gradient">כל דבר</span>
          </h2>
          <p className="text-[rgba(242,237,223,0.4)] mt-4 max-w-xl mx-auto text-sm leading-relaxed">
            אנחנו לא רק חנות. אנחנו יעד פאדל שנבנה עבור שחקנים שדורשים את הטוב ביותר.
          </p>
        </motion.div>

        {/* Feature cards grid */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {features.map((feature, i) => {
            const Icon = feature.icon
            return (
              <motion.div
                key={feature.title}
                initial={{ opacity: 0, y: 40 }}
                whileInView={{ opacity: 1, y: 0 }}
                viewport={{ once: true, margin: '-80px' }}
                transition={{ duration: 0.9, delay: i * 0.1, ease: LUXURY_EASE }}
                className="luxury-card relative p-7 overflow-hidden"
              >
                {/* Hover glow */}
                <div
                  className="absolute inset-0 opacity-0 transition-opacity duration-500 pointer-events-none"
                  style={{
                    background: 'radial-gradient(ellipse at top right, rgba(201,165,90,0.05) 0%, transparent 65%)',
                  }}
                  aria-hidden="true"
                />

                <div className="relative">
                  {/* Icon box */}
                  <div className="w-12 h-12 border border-[rgba(201,165,90,0.22)] bg-[rgba(201,165,90,0.06)] flex items-center justify-center mb-5 transition-colors duration-300">
                    <Icon className="w-5 h-5 text-[#c9a55a]" aria-hidden="true" />
                  </div>

                  <h3 className="text-[#f2eddf] font-bold text-sm mb-2.5 leading-snug transition-colors duration-300">
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

        {/* Bottom CTA */}
        <motion.div
          initial={{ opacity: 0, y: 40 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-80px' }}
          transition={{ duration: 0.9, delay: 0.3, ease: LUXURY_EASE }}
          className="text-center mt-20 pt-16 border-t border-[rgba(242,237,223,0.06)]"
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
