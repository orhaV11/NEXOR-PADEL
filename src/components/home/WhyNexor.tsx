'use client'

import { motion } from 'framer-motion'
import { Shield, Truck, RotateCcw, Award, Headphones, Zap } from 'lucide-react'

const features = [
  {
    icon: Award,
    title: 'מוצרים מקוריים 100%',
    description: 'ציוד אמיתי המסופק ישירות ממותגי הפאדל המובילים. כל מוצר מגיע עם אחריות יצרן מקורית.',
  },
  {
    icon: Truck,
    title: 'משלוח חינם מעל ₪280',
    description: 'משלוח חינם לכל הזמנה מעל ₪280. משלוח מהיר תוך 24 שעות לאלה שצריכים את הציוד כבר עכשיו.',
  },
  {
    icon: RotateCcw,
    title: 'החזרה חינם עד 30 יום',
    description: 'לא מתאים? החזר ללא טרחה תוך 30 יום. ללא שאלות, ללא עלויות נסתרות.',
  },
  {
    icon: Headphones,
    title: 'תמיכת מומחי פאדל',
    description: 'צוות מומחי הפאדל שלנו כאן לעזור לך למצוא את הציוד המושלם לרמה ולסגנון המשחק שלך.',
  },
  {
    icon: Shield,
    title: 'תשלום מאובטח',
    description: 'כל העסקאות מוצפנות ומאובטחות. ויזה, מסטרקארד, ביט, אפל פיי ועוד.',
  },
  {
    icon: Zap,
    title: 'בחירה פרימיום',
    description: 'מבחר הציוד הטוב ביותר הזמין בשוק. אנחנו בודקים ומאשרים כל מוצר לפני שהוא מגיע אליך.',
  },
]

export default function WhyNexor() {
  return (
    <section className="py-24 bg-[#060606] relative overflow-hidden">
      <div
        className="absolute top-0 right-1/2 translate-x-1/2 w-px h-full opacity-20"
        style={{ background: 'linear-gradient(to bottom, transparent, #b5f72e, transparent)' }}
      />
      <div
        className="absolute -right-40 top-1/2 -translate-y-1/2 w-80 h-80 rounded-full opacity-5 pointer-events-none"
        style={{ background: 'radial-gradient(circle, #b5f72e 0%, transparent 70%)', filter: 'blur(40px)' }}
      />

      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-100px' }}
          transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
          className="text-center mb-16"
        >
          <div className="section-tag mx-auto inline-flex">למה NEXOR</div>
          <h2 className="section-heading mt-2">
            ההבדל של<br />
            <span className="text-[#b5f72e]">NEXOR</span>
          </h2>
          <p className="text-white/40 mt-4 max-w-xl mx-auto text-sm leading-relaxed">
            אנחנו לא רק חנות. אנחנו יעד פאדל שנבנה עבור שחקנים שדורשים את הטוב ביותר.
          </p>
        </motion.div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {features.map((feature, i) => {
            const Icon = feature.icon
            return (
              <motion.div
                key={feature.title}
                initial={{ opacity: 0, y: 30 }}
                whileInView={{ opacity: 1, y: 0 }}
                viewport={{ once: true, margin: '-50px' }}
                transition={{ duration: 0.7, delay: i * 0.1, ease: [0.16, 1, 0.3, 1] }}
                className="group relative p-6 bg-[#0f0f0f] border border-white/5 hover:border-[#b5f72e]/20 transition-all duration-500 hover:shadow-card-hover"
              >
                <div className="absolute inset-0 opacity-0 group-hover:opacity-100 transition-opacity duration-500 pointer-events-none"
                  style={{ background: 'radial-gradient(ellipse at top right, rgba(181,247,46,0.03) 0%, transparent 60%)' }}
                />

                <div className="relative">
                  <div className="w-12 h-12 border border-[#b5f72e]/20 bg-[#b5f72e]/5 flex items-center justify-center mb-5 group-hover:bg-[#b5f72e]/10 group-hover:border-[#b5f72e]/40 transition-all duration-300">
                    <Icon className="w-5 h-5 text-[#b5f72e]" />
                  </div>

                  <h3 className="text-white font-bold text-sm mb-2.5 group-hover:text-[#b5f72e] transition-colors duration-300 leading-snug">
                    {feature.title}
                  </h3>
                  <p className="text-white/40 text-sm leading-relaxed">
                    {feature.description}
                  </p>
                </div>
              </motion.div>
            )
          })}
        </div>

        <motion.div
          initial={{ opacity: 0, y: 20 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true }}
          transition={{ duration: 0.7, delay: 0.4, ease: [0.16, 1, 0.3, 1] }}
          className="text-center mt-16 pt-16 border-t border-white/5"
        >
          <p className="text-3xl md:text-4xl font-display text-white tracking-wide mb-6">
            המשחק הבא שלך מתחיל כאן.
          </p>
          <a
            href="/shop"
            className="inline-flex items-center gap-3 px-8 py-4 bg-[#b5f72e] text-black font-bold text-sm uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-md transition-all duration-300"
          >
            קנה ציוד פרימיום
          </a>
        </motion.div>
      </div>
    </section>
  )
}
