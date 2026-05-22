'use client'

import { useState } from 'react'
import { motion } from 'framer-motion'
import { Mail, MapPin, MessageCircle, Send, CheckCircle } from 'lucide-react'

export default function ContactPage() {
  const [formData, setFormData] = useState({ name: '', email: '', subject: '', message: '' })
  const [submitted, setSubmitted] = useState(false)

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    setSubmitted(true)
  }

  return (
    <div className="min-h-screen pt-20">
      {/* Hero */}
      <section className="relative py-24 overflow-hidden">
        <div className="absolute inset-0 bg-[#050505]" />
        <div className="absolute inset-0 bg-grid opacity-25" />
        <div
          className="absolute inset-0 opacity-15"
          style={{ background: 'radial-gradient(ellipse at 50% 0%, rgba(201,165,90,0.12) 0%, transparent 60%)' }}
        />

        <div className="relative max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 text-center">
          <motion.div
            initial={{ opacity: 0, y: 40 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
          >
            <div className="section-label mx-auto inline-flex mb-6">צור קשר</div>
            <h1 className="section-title mb-6">
              יצירת<br />
              <span className="text-gold-gradient">קשר</span>
            </h1>
            <p className="text-[#f2eddf]/50 text-lg max-w-xl mx-auto leading-relaxed">
              שאלות על ציוד? צריך עזרה בבחירת מחבט? הצוות שלנו כאן בשבילך.
            </p>
          </motion.div>
        </div>
      </section>

      {/* Content */}
      <section className="py-16 max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-16">
          {/* Right: Info (in RTL layout, this is on the right side) */}
          <motion.div
            initial={{ opacity: 0, x: 40 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ duration: 0.7, ease: [0.16, 1, 0.3, 1] }}
          >
            {/* WhatsApp CTA — keep green for brand */}
            <a
              href="https://wa.me/972541234567"
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center gap-4 p-5 bg-[#0d1f0d] border border-green-500/20 hover:border-green-500/40 transition-all duration-300 mb-8 group"
            >
              <div className="w-12 h-12 bg-green-500/10 border border-green-500/20 flex items-center justify-center flex-shrink-0 group-hover:bg-green-500/20 transition-colors">
                <MessageCircle className="w-6 h-6 text-green-400" />
              </div>
              <div>
                <p className="text-[#f2eddf] font-bold mb-0.5">שוחח איתנו בוואטסאפ</p>
                <p className="text-[#f2eddf]/40 text-sm">אנחנו בדרך כלל עונים תוך דקות</p>
              </div>
              <span className="me-auto text-green-400 text-xs font-bold uppercase tracking-widest bg-green-500/10 px-2 py-1">
                זמין
              </span>
            </a>

            {/* Contact Details */}
            <div className="space-y-4 mb-8">
              {[
                {
                  icon: Mail,
                  label: 'אימייל',
                  value: 'hello@nexorpadel.co.il',
                  sub: 'אנחנו עונים תוך 24 שעות',
                },
                {
                  icon: MapPin,
                  label: 'מיקום',
                  value: 'ישראל',
                  sub: 'חנות אונליין — משלוח לכל הארץ',
                },
              ].map(({ icon: Icon, label, value, sub }) => (
                <div key={label} className="flex items-start gap-4 p-4 bg-[#0d0d10] border border-[rgba(201,165,90,0.08)] hover:border-[rgba(201,165,90,0.18)] transition-all duration-300 group">
                  <div className="w-10 h-10 bg-[rgba(201,165,90,0.05)] border border-[rgba(201,165,90,0.15)] flex items-center justify-center flex-shrink-0 group-hover:bg-[rgba(201,165,90,0.1)] transition-colors">
                    <Icon className="w-4 h-4" style={{ color: '#c9a55a' }} />
                  </div>
                  <div>
                    <p className="text-[#f2eddf]/30 text-xs uppercase tracking-widest mb-0.5">{label}</p>
                    <p className="text-[#f2eddf] font-medium text-sm">{value}</p>
                    <p className="text-[#f2eddf]/30 text-xs mt-0.5">{sub}</p>
                  </div>
                </div>
              ))}
            </div>

            {/* Map Placeholder */}
            <div className="relative h-48 bg-[#0d0d10] border border-[rgba(201,165,90,0.08)] overflow-hidden">
              <div className="absolute inset-0 bg-grid opacity-20" />
              <div className="absolute inset-0 flex items-center justify-center flex-col gap-2">
                <MapPin className="w-8 h-8" style={{ color: 'rgba(201,165,90,0.4)' }} />
                <p className="text-[#f2eddf]/20 text-xs uppercase tracking-widest">ישראל</p>
                <p className="text-[#f2eddf]/10 text-xs">חנות אונליין — משלוח לכל הארץ</p>
              </div>
            </div>
          </motion.div>

          {/* Left: Contact Form */}
          <motion.div
            initial={{ opacity: 0, x: -40 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ duration: 0.7, delay: 0.1, ease: [0.16, 1, 0.3, 1] }}
          >
            <div className="bg-[#0d0d10] border border-[rgba(201,165,90,0.1)] p-8">
              <h2 className="font-display text-3xl text-[#f2eddf] tracking-wide uppercase mb-2">שלח הודעה</h2>
              <p className="text-[#f2eddf]/40 text-sm mb-8">ספר לנו איך נוכל לעזור ואנחנו נחזור אליך מהר.</p>

              {!submitted ? (
                <form onSubmit={handleSubmit} className="space-y-4">
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-[#f2eddf]/30 text-xs uppercase tracking-widest mb-2">שם</label>
                      <input
                        type="text"
                        value={formData.name}
                        onChange={e => setFormData(f => ({ ...f, name: e.target.value }))}
                        placeholder="השם שלך"
                        required
                        className="luxury-input"
                      />
                    </div>
                    <div>
                      <label className="block text-[#f2eddf]/30 text-xs uppercase tracking-widest mb-2">אימייל</label>
                      <input
                        type="email"
                        value={formData.email}
                        onChange={e => setFormData(f => ({ ...f, email: e.target.value }))}
                        placeholder="your@email.com"
                        required
                        dir="ltr"
                        className="luxury-input"
                      />
                    </div>
                  </div>

                  <div>
                    <label className="block text-[#f2eddf]/30 text-xs uppercase tracking-widest mb-2">נושא</label>
                    <select
                      value={formData.subject}
                      onChange={e => setFormData(f => ({ ...f, subject: e.target.value }))}
                      required
                      className="luxury-input"
                    >
                      <option value="" className="bg-[#0d0d10] text-[#f2eddf]/40">בחר נושא</option>
                      <option value="product" className="bg-[#0d0d10]">שאלה על מוצר</option>
                      <option value="order" className="bg-[#0d0d10]">תמיכה בהזמנה</option>
                      <option value="return" className="bg-[#0d0d10]">החזרה / החלפה</option>
                      <option value="shipping" className="bg-[#0d0d10]">משלוח</option>
                      <option value="other" className="bg-[#0d0d10]">אחר</option>
                    </select>
                  </div>

                  <div>
                    <label className="block text-[#f2eddf]/30 text-xs uppercase tracking-widest mb-2">הודעה</label>
                    <textarea
                      value={formData.message}
                      onChange={e => setFormData(f => ({ ...f, message: e.target.value }))}
                      placeholder="ספר לנו איך נוכל לעזור..."
                      required
                      rows={6}
                      className="luxury-input resize-none"
                    />
                  </div>

                  <button type="submit" className="btn-gold w-full">
                    <Send className="w-4 h-4" />
                    שלח הודעה
                  </button>
                </form>
              ) : (
                <motion.div
                  initial={{ scale: 0.9, opacity: 0 }}
                  animate={{ scale: 1, opacity: 1 }}
                  className="flex flex-col items-center justify-center py-12 gap-4 text-center"
                >
                  <div className="w-16 h-16 bg-[rgba(201,165,90,0.1)] border border-[rgba(201,165,90,0.3)] flex items-center justify-center">
                    <CheckCircle className="w-8 h-8" style={{ color: '#c9a55a' }} />
                  </div>
                  <h3 className="text-[#f2eddf] font-bold text-xl">!ההודעה נשלחה</h3>
                  <p className="text-[#f2eddf]/40 text-sm max-w-xs">
                    תודה שפנית אלינו. הצוות שלנו יחזור ל-{formData.email} תוך 24 שעות.
                  </p>
                  <button
                    onClick={() => { setSubmitted(false); setFormData({ name: '', email: '', subject: '', message: '' }) }}
                    className="mt-4 text-[#f2eddf]/30 text-xs uppercase tracking-widest hover:text-[#f2eddf] transition-colors"
                  >
                    שלח הודעה נוספת
                  </button>
                </motion.div>
              )}
            </div>
          </motion.div>
        </div>
      </section>
    </div>
  )
}
