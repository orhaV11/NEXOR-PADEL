'use client'

import { useState } from 'react'
import { motion } from 'framer-motion'
import { Mail, Phone, MapPin, Clock, MessageCircle, Send, CheckCircle } from 'lucide-react'

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
        <div className="absolute inset-0 bg-grid-fine opacity-35" />
        <div
          className="absolute inset-0 opacity-15"
          style={{ background: 'radial-gradient(ellipse at 50% 0%, rgba(181,247,46,0.15) 0%, transparent 60%)' }}
        />

        <div className="relative max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 text-center">
          <motion.div
            initial={{ opacity: 0, y: 40 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
          >
            <div className="section-tag mx-auto inline-flex mb-6">צור קשר</div>
            <h1 className="font-display text-6xl md:text-8xl text-white tracking-wide uppercase leading-none mb-6">
              יצירת<br />
              <span style={{ color: '#b5f72e' }}>קשר</span>
            </h1>
            <p className="text-white/50 text-lg max-w-xl mx-auto leading-relaxed">
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
            {/* WhatsApp CTA */}
            <a
              href="https://wa.me/972541234567"
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center gap-4 p-5 bg-[#0f1f0f] border border-green-500/20 hover:border-green-500/40 transition-all duration-300 mb-8 group"
            >
              <div className="w-12 h-12 bg-green-500/10 border border-green-500/20 flex items-center justify-center flex-shrink-0 group-hover:bg-green-500/20 transition-colors">
                <MessageCircle className="w-6 h-6 text-green-400" />
              </div>
              <div>
                <p className="text-white font-bold mb-0.5">שוחח איתנו בוואטסאפ</p>
                <p className="text-white/40 text-sm">אנחנו בדרך כלל עונים תוך דקות</p>
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
                  icon: Phone,
                  label: 'טלפון',
                  value: '054-123-4567',
                  sub: 'ראשון–חמישי, 09:00–18:00',
                },
                {
                  icon: MapPin,
                  label: 'מיקום',
                  value: 'ישראל',
                  sub: 'חנות אונליין — משלוח לכל הארץ',
                },
                {
                  icon: Clock,
                  label: 'שעות פעילות',
                  value: 'ראשון–חמישי: 09:00–18:00',
                  sub: 'שישי: 09:00–14:00 | שבת: סגור',
                },
              ].map(({ icon: Icon, label, value, sub }) => (
                <div key={label} className="flex items-start gap-4 p-4 bg-[#0f0f0f] border border-white/5 hover:border-[#b5f72e]/15 transition-all duration-300 group">
                  <div className="w-10 h-10 bg-[#b5f72e]/5 border border-[#b5f72e]/15 flex items-center justify-center flex-shrink-0 group-hover:bg-[#b5f72e]/10 transition-colors">
                    <Icon className="w-4 h-4 text-[#b5f72e]" />
                  </div>
                  <div>
                    <p className="text-white/30 text-xs uppercase tracking-widest mb-0.5">{label}</p>
                    <p className="text-white font-medium text-sm">{value}</p>
                    <p className="text-white/30 text-xs mt-0.5">{sub}</p>
                  </div>
                </div>
              ))}
            </div>

            {/* Map Placeholder */}
            <div className="relative h-48 bg-[#0f0f0f] border border-white/5 overflow-hidden">
              <div className="absolute inset-0 opacity-20" style={{
                backgroundImage: 'linear-gradient(rgba(181,247,46,0.05) 1px, transparent 1px), linear-gradient(90deg, rgba(181,247,46,0.05) 1px, transparent 1px)',
                backgroundSize: '30px 30px',
              }} />
              <div className="absolute inset-0 flex items-center justify-center flex-col gap-2">
                <MapPin className="w-8 h-8 text-[#b5f72e]/40" />
                <p className="text-white/20 text-xs uppercase tracking-widest">ישראל</p>
                <p className="text-white/10 text-xs">חנות אונליין — משלוח לכל הארץ</p>
              </div>
            </div>
          </motion.div>

          {/* Left: Contact Form */}
          <motion.div
            initial={{ opacity: 0, x: -40 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ duration: 0.7, delay: 0.1, ease: [0.16, 1, 0.3, 1] }}
          >
            <div className="bg-[#0f0f0f] border border-white/5 p-8">
              <h2 className="font-display text-3xl text-white tracking-wide uppercase mb-2">שלח הודעה</h2>
              <p className="text-white/40 text-sm mb-8">ספר לנו איך נוכל לעזור ואנחנו נחזור אליך מהר.</p>

              {!submitted ? (
                <form onSubmit={handleSubmit} className="space-y-4">
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-white/30 text-xs uppercase tracking-widest mb-2">שם</label>
                      <input
                        type="text"
                        value={formData.name}
                        onChange={e => setFormData(f => ({ ...f, name: e.target.value }))}
                        placeholder="השם שלך"
                        required
                        className="input-field"
                      />
                    </div>
                    <div>
                      <label className="block text-white/30 text-xs uppercase tracking-widest mb-2">אימייל</label>
                      <input
                        type="email"
                        value={formData.email}
                        onChange={e => setFormData(f => ({ ...f, email: e.target.value }))}
                        placeholder="your@email.com"
                        required
                        dir="ltr"
                        className="input-field"
                      />
                    </div>
                  </div>

                  <div>
                    <label className="block text-white/30 text-xs uppercase tracking-widest mb-2">נושא</label>
                    <select
                      value={formData.subject}
                      onChange={e => setFormData(f => ({ ...f, subject: e.target.value }))}
                      required
                      className="input-field"
                    >
                      <option value="" className="bg-[#111] text-white/40">בחר נושא</option>
                      <option value="product" className="bg-[#111]">שאלה על מוצר</option>
                      <option value="order" className="bg-[#111]">תמיכה בהזמנה</option>
                      <option value="return" className="bg-[#111]">החזרה / החלפה</option>
                      <option value="shipping" className="bg-[#111]">משלוח</option>
                      <option value="other" className="bg-[#111]">אחר</option>
                    </select>
                  </div>

                  <div>
                    <label className="block text-white/30 text-xs uppercase tracking-widest mb-2">הודעה</label>
                    <textarea
                      value={formData.message}
                      onChange={e => setFormData(f => ({ ...f, message: e.target.value }))}
                      placeholder="ספר לנו איך נוכל לעזור..."
                      required
                      rows={6}
                      className="input-field resize-none"
                    />
                  </div>

                  <button
                    type="submit"
                    className="w-full flex items-center justify-center gap-2 py-4 bg-[#b5f72e] text-black font-bold text-sm uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-sm transition-all duration-300"
                  >
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
                  <div className="w-16 h-16 bg-[#b5f72e]/10 border border-[#b5f72e]/30 flex items-center justify-center">
                    <CheckCircle className="w-8 h-8 text-[#b5f72e]" />
                  </div>
                  <h3 className="text-white font-bold text-xl">!ההודעה נשלחה</h3>
                  <p className="text-white/40 text-sm max-w-xs">
                    תודה שפנית אלינו. הצוות שלנו יחזור ל-{formData.email} תוך 24 שעות.
                  </p>
                  <button
                    onClick={() => { setSubmitted(false); setFormData({ name: '', email: '', subject: '', message: '' }) }}
                    className="mt-4 text-white/30 text-xs uppercase tracking-widest hover:text-white transition-colors"
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
