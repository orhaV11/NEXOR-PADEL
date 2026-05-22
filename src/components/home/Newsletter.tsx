'use client'

import { useState } from 'react'
import { motion } from 'framer-motion'
import { ArrowLeft, Mail, CheckCircle } from 'lucide-react'

export default function Newsletter() {
  const [email, setEmail] = useState('')
  const [submitted, setSubmitted] = useState(false)

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (email) {
      setSubmitted(true)
    }
  }

  return (
    <section className="py-24 relative overflow-hidden">
      <div className="absolute inset-0">
        <div className="absolute inset-0 bg-[#080808]" />
        <div
          className="absolute inset-0 opacity-25"
          style={{
            backgroundImage: 'linear-gradient(rgba(181,247,46,0.04) 1px, transparent 1px), linear-gradient(90deg, rgba(181,247,46,0.04) 1px, transparent 1px)',
            backgroundSize: '40px 40px',
          }}
        />
        <div
          className="absolute left-0 right-0 top-0 h-px opacity-40"
          style={{ background: 'linear-gradient(90deg, transparent, #b5f72e, transparent)' }}
        />
        <div
          className="absolute left-0 right-0 bottom-0 h-px opacity-40"
          style={{ background: 'linear-gradient(90deg, transparent, #b5f72e, transparent)' }}
        />
      </div>

      <div className="relative max-w-3xl mx-auto px-5 sm:px-8 lg:px-10 text-center">
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-100px' }}
          transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
        >
          <div className="w-12 h-12 mx-auto mb-6 border border-[#b5f72e]/30 bg-[#b5f72e]/5 flex items-center justify-center">
            <Mail className="w-5 h-5 text-[#b5f72e]" />
          </div>

          <div className="section-tag mx-auto inline-flex mb-4">ניוזלטר</div>

          <h2 className="section-heading mb-4">
            הישאר לפני<br />
            <span className="text-[#b5f72e]">כולם</span>
          </h2>

          <p className="text-white/40 mb-10 max-w-md mx-auto text-sm leading-relaxed">
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
                className="flex-1 bg-white/5 border border-white/10 px-5 py-4 text-white placeholder-white/25 text-sm focus:outline-none focus:border-[#b5f72e]/40 transition-colors"
              />
              <button
                type="submit"
                className="sm:flex-shrink-0 flex items-center justify-center gap-2 px-7 py-4 bg-[#b5f72e] text-black font-bold text-sm uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-sm transition-all duration-300"
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
              <CheckCircle className="w-12 h-12 text-[#b5f72e]" />
              <p className="text-white font-bold text-lg">!נרשמת בהצלחה</p>
              <p className="text-white/40 text-sm">ברוך הבא לקהילת NEXOR. עקוב אחר תיבת הדואר שלך לתוכן בלעדי.</p>
            </motion.div>
          )}

          <p className="text-white/20 text-xs mt-4">
            ללא ספאם. ביטול מנוי בכל עת. אנו מכבדים את פרטיותך.
          </p>
        </motion.div>
      </div>
    </section>
  )
}
