'use client'

import { useState } from 'react'
import { motion } from 'framer-motion'
import { ArrowLeft, Mail, CheckCircle } from 'lucide-react'

const LUXURY_EASE = [0.16, 1, 0.3, 1] as const

export default function Newsletter() {
  const [email, setEmail] = useState('')
  const [submitted, setSubmitted] = useState(false)

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (email) setSubmitted(true)
  }

  return (
    <section className="py-24 relative overflow-hidden">
      {/* Background */}
      <div className="absolute inset-0 bg-[#08080a]" />
      <div className="bg-grid absolute inset-0 opacity-20" />
      <div
        className="absolute left-0 right-0 top-0 h-px opacity-35"
        style={{ background: 'linear-gradient(90deg, transparent, #c9a55a, transparent)' }}
      />
      <div
        className="absolute left-0 right-0 bottom-0 h-px opacity-35"
        style={{ background: 'linear-gradient(90deg, transparent, #c9a55a, transparent)' }}
      />
      {/* Ambient glow */}
      <div
        className="absolute inset-0 pointer-events-none"
        style={{ background: 'radial-gradient(ellipse at 50% 50%, rgba(201,165,90,0.05) 0%, transparent 65%)' }}
      />

      <div className="relative max-w-3xl mx-auto px-5 sm:px-8 lg:px-10 text-center">
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-100px' }}
          transition={{ duration: 0.8, ease: LUXURY_EASE }}
        >
          <div className="w-12 h-12 mx-auto mb-6 border border-[rgba(201,165,90,0.25)] bg-[rgba(201,165,90,0.05)] flex items-center justify-center">
            <Mail className="w-5 h-5 text-[#c9a55a]" />
          </div>

          <span className="section-label">ניוזלטר</span>

          <h2 className="section-title mt-3 mb-4">
            הישאר לפני{' '}
            <span className="text-gold-gradient">כולם</span>
          </h2>

          <p className="text-[rgba(242,237,223,0.4)] mb-10 max-w-md mx-auto text-sm leading-relaxed">
            קבל עסקאות בלעדיות, גישה מוקדמת למוצרים חדשים, טיפים מומחים ובחירות ציוד מאוצרות — ישירות לתיבת הדואר שלך.
          </p>

          {!submitted ? (
            <form onSubmit={handleSubmit} className="flex flex-col sm:flex-row gap-0 max-w-lg mx-auto">
              <input
                type="email"
                value={email}
                onChange={e => setEmail(e.target.value)}
                placeholder="האימייל שלך"
                required
                dir="ltr"
                className="flex-1 luxury-input sm:border-l-0"
              />
              <button
                type="submit"
                className="btn-gold sm:flex-shrink-0 gap-2"
              >
                הרשם
                <ArrowLeft className="w-4 h-4" />
              </button>
            </form>
          ) : (
            <motion.div
              initial={{ scale: 0.9, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              className="flex flex-col items-center gap-3"
            >
              <CheckCircle className="w-12 h-12 text-[#c9a55a]" />
              <p className="text-[#f2eddf] font-bold text-lg">!נרשמת בהצלחה</p>
              <p className="text-[rgba(242,237,223,0.4)] text-sm">ברוך הבא לקהילת NEXOR. עקוב אחר תיבת הדואר שלך לתוכן בלעדי.</p>
            </motion.div>
          )}

          <p className="text-[rgba(242,237,223,0.2)] text-xs mt-4">
            ללא ספאם. ביטול מנוי בכל עת. אנו מכבדים את פרטיותך.
          </p>
        </motion.div>
      </div>
    </section>
  )
}
