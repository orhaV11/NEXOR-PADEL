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
        <div className="absolute inset-0 bg-hero-radial opacity-40" />
        <div className="absolute inset-0 bg-grid-pattern bg-grid opacity-100" />

        <div className="relative max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 text-center">
          <motion.div
            initial={{ opacity: 0, y: 40 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.8, ease: [0.22, 1, 0.36, 1] }}
          >
            <div className="section-tag mx-auto inline-flex mb-6">Get in Touch</div>
            <h1 className="font-display text-6xl md:text-8xl text-white tracking-wide uppercase leading-none mb-6">
              CONTACT<br />
              <span style={{ color: '#b5f72e' }}>US</span>
            </h1>
            <p className="text-white/50 text-lg max-w-xl mx-auto">
              Questions about gear? Need help choosing a racket? Our team is here for you.
            </p>
          </motion.div>
        </div>
      </section>

      {/* Content */}
      <section className="py-16 max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-16">
          {/* Left: Info */}
          <motion.div
            initial={{ opacity: 0, x: -40 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ duration: 0.7 }}
          >
            {/* WhatsApp CTA */}
            <a
              href="https://wa.me/34600123456"
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center gap-4 p-5 bg-[#0f1f0f] border border-green-500/20 hover:border-green-500/40 transition-all duration-300 mb-8 group"
            >
              <div className="w-12 h-12 bg-green-500/10 border border-green-500/20 flex items-center justify-center flex-shrink-0 group-hover:bg-green-500/20 transition-colors">
                <MessageCircle className="w-6 h-6 text-green-400" />
              </div>
              <div>
                <p className="text-white font-bold mb-0.5">Chat on WhatsApp</p>
                <p className="text-white/40 text-sm">We typically reply within minutes</p>
              </div>
              <span className="ml-auto text-green-400 text-xs font-bold uppercase tracking-widest bg-green-500/10 px-2 py-1">
                Live
              </span>
            </a>

            {/* Contact Details */}
            <div className="space-y-4 mb-8">
              {[
                {
                  icon: Mail,
                  label: 'Email',
                  value: 'hello@nexorpadel.com',
                  sub: 'We reply within 24 hours',
                },
                {
                  icon: Phone,
                  label: 'Phone',
                  value: '+34 600 123 456',
                  sub: 'Mon–Fri, 9am–6pm',
                },
                {
                  icon: MapPin,
                  label: 'Location',
                  value: 'Barcelona, Spain',
                  sub: 'Online store — we ship EU-wide',
                },
                {
                  icon: Clock,
                  label: 'Hours',
                  value: 'Mon–Fri: 9:00–18:00',
                  sub: 'Sat: 10:00–14:00 | Sun: Closed',
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

            {/* Store Map Placeholder */}
            <div className="relative h-48 bg-[#0f0f0f] border border-white/5 overflow-hidden">
              <div className="absolute inset-0 bg-grid-pattern opacity-20" style={{
                backgroundImage: 'linear-gradient(rgba(181,247,46,0.05) 1px, transparent 1px), linear-gradient(90deg, rgba(181,247,46,0.05) 1px, transparent 1px)',
                backgroundSize: '30px 30px',
              }} />
              <div className="absolute inset-0 flex items-center justify-center flex-col gap-2">
                <MapPin className="w-8 h-8 text-[#b5f72e]/40" />
                <p className="text-white/20 text-xs uppercase tracking-widest">Barcelona, Spain</p>
                <p className="text-white/10 text-xs">Online store — EU-wide shipping</p>
              </div>
            </div>
          </motion.div>

          {/* Right: Contact Form */}
          <motion.div
            initial={{ opacity: 0, x: 40 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ duration: 0.7, delay: 0.1 }}
          >
            <div className="bg-[#0f0f0f] border border-white/5 p-8">
              <h2 className="font-display text-3xl text-white tracking-wide uppercase mb-2">Send a Message</h2>
              <p className="text-white/40 text-sm mb-8">Tell us how we can help and we&apos;ll get back to you fast.</p>

              {!submitted ? (
                <form onSubmit={handleSubmit} className="space-y-4">
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-white/30 text-xs uppercase tracking-widest mb-2">Name</label>
                      <input
                        type="text"
                        value={formData.name}
                        onChange={e => setFormData(f => ({ ...f, name: e.target.value }))}
                        placeholder="Your name"
                        required
                        className="input-field"
                      />
                    </div>
                    <div>
                      <label className="block text-white/30 text-xs uppercase tracking-widest mb-2">Email</label>
                      <input
                        type="email"
                        value={formData.email}
                        onChange={e => setFormData(f => ({ ...f, email: e.target.value }))}
                        placeholder="your@email.com"
                        required
                        className="input-field"
                      />
                    </div>
                  </div>

                  <div>
                    <label className="block text-white/30 text-xs uppercase tracking-widest mb-2">Subject</label>
                    <select
                      value={formData.subject}
                      onChange={e => setFormData(f => ({ ...f, subject: e.target.value }))}
                      required
                      className="input-field"
                    >
                      <option value="" className="bg-[#111] text-white/40">Select a subject</option>
                      <option value="product" className="bg-[#111]">Product Enquiry</option>
                      <option value="order" className="bg-[#111]">Order Support</option>
                      <option value="return" className="bg-[#111]">Return / Exchange</option>
                      <option value="shipping" className="bg-[#111]">Shipping</option>
                      <option value="other" className="bg-[#111]">Other</option>
                    </select>
                  </div>

                  <div>
                    <label className="block text-white/30 text-xs uppercase tracking-widest mb-2">Message</label>
                    <textarea
                      value={formData.message}
                      onChange={e => setFormData(f => ({ ...f, message: e.target.value }))}
                      placeholder="Tell us how we can help..."
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
                    Send Message
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
                  <h3 className="text-white font-bold text-xl">Message Sent!</h3>
                  <p className="text-white/40 text-sm max-w-xs">
                    Thanks for reaching out. Our team will reply to {formData.email} within 24 hours.
                  </p>
                  <button
                    onClick={() => { setSubmitted(false); setFormData({ name: '', email: '', subject: '', message: '' }) }}
                    className="mt-4 text-white/30 text-xs uppercase tracking-widest hover:text-white transition-colors"
                  >
                    Send Another
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
