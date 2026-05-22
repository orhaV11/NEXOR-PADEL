'use client'

import { motion } from 'framer-motion'
import Link from 'next/link'
import { ArrowLeft, Target, Heart, Zap, Award, Users, Globe } from 'lucide-react'

const values = [
  {
    icon: Target,
    title: 'נבחר בקפידה',
    description: 'אנחנו לא נושאים הכל. אנחנו נושאים את הטוב ביותר. כל מוצר בחנות שלנו נבדק ונבחר לפי ביצועים, עמידות ואיכות.',
  },
  {
    icon: Heart,
    title: 'נבנה לשחקנים',
    description: 'אנחנו עצמנו שחקני פאדל. אנחנו מבינים מה זה אומר להשקיע בציוד הנכון — ואנחנו כאן לעזור לך לבחור בביטחון.',
  },
  {
    icon: Award,
    title: 'רק ציוד מקורי',
    description: 'NEXOR הוא ספק מורשה רשמי לכל מותג שאנחנו מוכרים. לעולם לא תמצא כאן מוצרים מזויפים. כל מוצר מגיע עם אחריות יצרן.',
  },
  {
    icon: Zap,
    title: 'שירות פרימיום',
    description: 'משלוח מהיר, החזרות קלות, תמיכה אמיתית. קניה ב-NEXOR צריכה להרגיש פרימיום כמו הציוד שאתה קונה.',
  },
]

const milestones = [
  { year: '2022', title: 'NEXOR נוסדה', description: 'נולדה מתסכול עם קמעונאות פאדל באיכות נמוכה, NEXOR השיקה עם שליחות לעשות זאת טוב יותר.' },
  { year: '2023', title: 'שותפויות רשמיות', description: 'הובטחו שותפויות ספק רשמי עם Head, Bullpadel, Nox ו-Adidas Padel.' },
  { year: '2024', title: '10,000 שחקנים', description: 'הגענו לאבן הדרך הראשונה של 10,000 לקוחות. הקהילה גדלה במהירות.' },
  { year: '2025', title: 'פלטפורמה מלאה', description: 'השקנו את הפלטפורמה הפרימיום המלאה עם מגוון ציוד שלם לכל רמת שחקן.' },
]

export default function AboutPage() {
  return (
    <div className="min-h-screen pt-20">
      {/* Hero */}
      <section className="relative py-24 md:py-36 overflow-hidden">
        <div className="absolute inset-0 bg-[#050505]" />
        <div className="absolute inset-0 bg-grid-fine opacity-40" />
        <div
          className="absolute inset-0 opacity-20"
          style={{ background: 'radial-gradient(ellipse at 50% 0%, rgba(181,247,46,0.15) 0%, transparent 60%)' }}
        />

        <div className="relative max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 text-center">
          <motion.div
            initial={{ opacity: 0, y: 40 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
          >
            <div className="section-tag mx-auto inline-flex mb-6">הסיפור שלנו</div>
            <h1 className="font-display text-6xl md:text-8xl lg:text-9xl text-white tracking-wide uppercase leading-none mb-6">
              אודות<br />
              <span style={{ color: '#b5f72e' }}>NEXOR</span>
            </h1>
            <p className="text-white/50 text-lg md:text-xl max-w-2xl mx-auto leading-relaxed">
              בנינו את NEXOR כי נמאס לנו לפשרות. כשחקני פאדל, רצינו חנות שתתאים לרמת הספורט שאנחנו אוהבים.
            </p>
          </motion.div>
        </div>
      </section>

      {/* Mission Statement */}
      <section className="py-24 bg-[#070707]">
        <div className="max-w-5xl mx-auto px-5 sm:px-8 lg:px-10">
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true }}
            transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
            className="grid grid-cols-1 lg:grid-cols-2 gap-16 items-center"
          >
            <div>
              <div className="section-tag mb-4">מי אנחנו</div>
              <h2 className="font-display text-4xl md:text-5xl text-white tracking-wide uppercase mb-6 leading-tight">
                ציוד פרימיום.<br />
                <span className="text-[#b5f72e]">מטרה רצינית.</span>
              </h2>
              <p className="text-white/50 text-base leading-relaxed mb-4">
                NEXOR Padel נוסדה על ידי שחקנים שהיו מתוסכלים ממצב הקמעונאות בפאדל — בסיסיות יקרות מדי, עצות גרועות, וציוד שלא תאם את האיכות של הספורט.
              </p>
              <p className="text-white/50 text-base leading-relaxed mb-6">
                יצאנו לבנות משהו אחר. יעד פאדל פרימיום שבו כל מוצר נבחר בקפידה, כל מותג רשמי, וכל לקוח מקבל את השירות שמגיע לו.
              </p>
              <p className="text-white/50 text-base leading-relaxed">
                בין אם אתה מרים מחבט פאדל לראשונה או שחקן ברמת תחרות — NEXOR נבנתה בשבילך.
              </p>
            </div>

            {/* Stats */}
            <div className="grid grid-cols-2 gap-4">
              {[
                { icon: Users, value: '+10,000', label: 'שחקנים מרוצים' },
                { icon: Award, value: '+6', label: 'שותפי מותג רשמיים' },
                { icon: Globe, value: 'ישראל', label: 'חנות אונליין מקומית' },
                { icon: Heart, value: '4.9★', label: 'דירוג ממוצע' },
              ].map(({ icon: Icon, value, label }, i) => (
                <motion.div
                  key={label}
                  initial={{ opacity: 0, scale: 0.9 }}
                  whileInView={{ opacity: 1, scale: 1 }}
                  viewport={{ once: true }}
                  transition={{ duration: 0.5, delay: i * 0.1 }}
                  className="p-6 bg-[#0f0f0f] border border-white/5 text-center"
                >
                  <div className="w-10 h-10 mx-auto mb-3 bg-[#b5f72e]/10 border border-[#b5f72e]/20 flex items-center justify-center">
                    <Icon className="w-5 h-5 text-[#b5f72e]" />
                  </div>
                  <p className="font-display text-3xl text-[#b5f72e] tracking-wide ltr-text">{value}</p>
                  <p className="text-white/30 text-xs uppercase tracking-widest mt-1">{label}</p>
                </motion.div>
              ))}
            </div>
          </motion.div>
        </div>
      </section>

      {/* Values */}
      <section className="py-24">
        <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true }}
            transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
            className="text-center mb-16"
          >
            <div className="section-tag mx-auto inline-flex mb-4">מה אנחנו מאמינים</div>
            <h2 className="section-heading">
              הערכים<br />
              <span className="text-[#b5f72e]">שלנו</span>
            </h2>
          </motion.div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {values.map((value, i) => {
              const Icon = value.icon
              return (
                <motion.div
                  key={value.title}
                  initial={{ opacity: 0, y: 30 }}
                  whileInView={{ opacity: 1, y: 0 }}
                  viewport={{ once: true }}
                  transition={{ duration: 0.7, delay: i * 0.1, ease: [0.16, 1, 0.3, 1] }}
                  className="p-8 bg-[#0f0f0f] border border-white/5 hover:border-[#b5f72e]/20 transition-all duration-500 group"
                >
                  <div className="w-12 h-12 border border-[#b5f72e]/20 bg-[#b5f72e]/5 flex items-center justify-center mb-5 group-hover:bg-[#b5f72e]/10 transition-all duration-300">
                    <Icon className="w-5 h-5 text-[#b5f72e]" />
                  </div>
                  <h3 className="text-white font-bold text-lg mb-3">{value.title}</h3>
                  <p className="text-white/40 text-sm leading-relaxed">{value.description}</p>
                </motion.div>
              )
            })}
          </div>
        </div>
      </section>

      {/* Timeline */}
      <section className="py-24 bg-[#070707]">
        <div className="max-w-4xl mx-auto px-5 sm:px-8 lg:px-10">
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true }}
            transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
            className="text-center mb-16"
          >
            <div className="section-tag mx-auto inline-flex mb-4">המסע שלנו</div>
            <h2 className="section-heading">
              איך הגענו<br />
              <span className="text-[#b5f72e]">לכאן</span>
            </h2>
          </motion.div>

          <div className="relative">
            <div className="absolute right-1/2 translate-x-px top-0 bottom-0 w-px bg-white/5" />
            {milestones.map((m, i) => (
              <motion.div
                key={m.year}
                initial={{ opacity: 0, x: i % 2 === 0 ? 40 : -40 }}
                whileInView={{ opacity: 1, x: 0 }}
                viewport={{ once: true }}
                transition={{ duration: 0.7, delay: i * 0.15, ease: [0.16, 1, 0.3, 1] }}
                className={`relative flex items-center gap-8 mb-12 ${i % 2 === 0 ? 'flex-row' : 'flex-row-reverse'}`}
              >
                <div className={`flex-1 ${i % 2 === 0 ? 'text-right' : 'text-left'}`}>
                  <div className="inline-block p-5 bg-[#0f0f0f] border border-white/5 hover:border-[#b5f72e]/20 transition-all duration-300">
                    <p className="text-[#b5f72e] text-xs font-bold uppercase tracking-widest mb-1 ltr-text">{m.year}</p>
                    <h3 className="text-white font-bold text-base mb-1">{m.title}</h3>
                    <p className="text-white/40 text-sm">{m.description}</p>
                  </div>
                </div>
                <div className="relative z-10 w-3 h-3 bg-[#b5f72e] flex-shrink-0 shadow-neon-sm" />
                <div className="flex-1" />
              </motion.div>
            ))}
          </div>
        </div>
      </section>

      {/* CTA Section */}
      <section className="py-24">
        <div className="max-w-4xl mx-auto px-5 sm:px-8 lg:px-10 text-center">
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true }}
            transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
          >
            <h2 className="font-display text-5xl md:text-6xl text-white tracking-wide uppercase mb-6">
              מוכן לשחק<br />
              <span className="text-[#b5f72e]">את הטוב ביותר?</span>
            </h2>
            <p className="text-white/40 mb-10 max-w-md mx-auto text-sm leading-relaxed">
              מהמשחק הראשון שלך עד הרמה הבאה. ציוד מאוצר. ביצועים רציניים.
            </p>
            <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
              <Link
                href="/shop"
                className="flex items-center gap-2 px-8 py-4 bg-[#b5f72e] text-black font-bold text-sm uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-md transition-all group"
              >
                קנה עכשיו <ArrowLeft className="w-4 h-4 group-hover:-translate-x-1 transition-transform" />
              </Link>
              <Link
                href="/contact"
                className="flex items-center gap-2 px-8 py-4 border border-white/20 text-white text-sm uppercase tracking-widest hover:border-[#b5f72e]/40 hover:text-[#b5f72e] transition-all"
              >
                צור קשר
              </Link>
            </div>
          </motion.div>
        </div>
      </section>
    </div>
  )
}
