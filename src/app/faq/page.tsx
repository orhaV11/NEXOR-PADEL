'use client'

import { useState, useMemo } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import Link from 'next/link'
import { ChevronDown, Search, MessageCircle } from 'lucide-react'
import { faqs } from '@/lib/data'

const EASE = [0.16, 1, 0.3, 1] as const

export default function FAQPage() {
  const [search, setSearch] = useState('')
  const [openIndex, setOpenIndex] = useState<number | null>(null)

  const filtered = useMemo(() => {
    if (!search.trim()) return faqs
    const q = search.toLowerCase()
    return faqs.filter(
      f => f.question.toLowerCase().includes(q) || f.answer.toLowerCase().includes(q)
    )
  }, [search])

  function toggle(i: number) {
    setOpenIndex(prev => (prev === i ? null : i))
  }

  return (
    <div className="min-h-screen pt-20">
      {/* Hero */}
      <section className="relative py-24 md:py-32 overflow-hidden">
        <div className="absolute inset-0 bg-[#050505]" />
        <div className="absolute inset-0 bg-grid opacity-25" />
        <div
          className="absolute inset-0 opacity-12"
          style={{ background: 'radial-gradient(ellipse at 50% 0%, rgba(201,165,90,0.12) 0%, transparent 60%)' }}
        />

        <div className="relative max-w-3xl mx-auto px-5 sm:px-8 text-center">
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.8, ease: EASE }}
          >
            <div className="section-label mx-auto inline-flex mb-6">תמיכה</div>
            <h1 className="section-title mb-6">
              שאלות<br />
              <span className="text-gold-gradient">נפוצות</span>
            </h1>
            <p className="text-[#f2eddf]/50 text-lg max-w-xl mx-auto leading-relaxed mb-10">
              מצאת תשובה? מצוין. לא מצאת? דבר איתנו.
            </p>

            {/* Search */}
            <div className="relative max-w-lg mx-auto">
              <Search
                className="absolute top-1/2 -translate-y-1/2 right-4 w-4 h-4 pointer-events-none"
                style={{ color: 'rgba(201,165,90,0.5)' }}
              />
              <input
                type="text"
                value={search}
                onChange={e => { setSearch(e.target.value); setOpenIndex(null) }}
                placeholder="חפש שאלה..."
                className="luxury-input pr-11"
              />
            </div>
          </motion.div>
        </div>
      </section>

      <div className="divider" />

      {/* FAQ Accordion */}
      <section className="py-16 max-w-3xl mx-auto px-5 sm:px-8 lg:px-10">
        <AnimatePresence mode="wait">
          {filtered.length === 0 ? (
            <motion.div
              key="no-results"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="text-center py-16"
            >
              <p className="text-[#f2eddf]/30 text-sm">לא נמצאו תוצאות עבור &ldquo;{search}&rdquo;</p>
            </motion.div>
          ) : (
            <motion.div
              key="results"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="space-y-3"
            >
              {filtered.map((faq, i) => {
                const isOpen = openIndex === i
                return (
                  <motion.div
                    key={faq.question}
                    initial={{ opacity: 0, y: 16 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.5, delay: i * 0.05, ease: EASE }}
                    className="overflow-hidden"
                    style={{
                      background: '#0d0d10',
                      border: isOpen
                        ? '1px solid rgba(201,165,90,0.25)'
                        : '1px solid rgba(201,165,90,0.08)',
                      transition: 'border-color 0.3s ease',
                    }}
                  >
                    <button
                      onClick={() => toggle(i)}
                      className="w-full flex items-center justify-between gap-4 p-6 text-right"
                    >
                      <span
                        className="text-sm font-semibold leading-snug transition-colors duration-200"
                        style={{ color: isOpen ? '#c9a55a' : '#f2eddf' }}
                      >
                        {faq.question}
                      </span>
                      <ChevronDown
                        className="w-4 h-4 flex-shrink-0 transition-transform duration-300"
                        style={{
                          color: isOpen ? '#c9a55a' : 'rgba(242,237,223,0.3)',
                          transform: isOpen ? 'rotate(180deg)' : 'rotate(0deg)',
                        }}
                      />
                    </button>

                    <AnimatePresence initial={false}>
                      {isOpen && (
                        <motion.div
                          key="content"
                          initial={{ height: 0, opacity: 0 }}
                          animate={{ height: 'auto', opacity: 1 }}
                          exit={{ height: 0, opacity: 0 }}
                          transition={{ duration: 0.35, ease: EASE }}
                          style={{ overflow: 'hidden' }}
                        >
                          <div className="px-6 pb-6">
                            <div
                              className="h-px mb-5"
                              style={{ background: 'rgba(201,165,90,0.12)' }}
                            />
                            <p className="text-[#f2eddf]/55 text-sm leading-relaxed">
                              {faq.answer}
                            </p>
                          </div>
                        </motion.div>
                      )}
                    </AnimatePresence>
                  </motion.div>
                )
              })}
            </motion.div>
          )}
        </AnimatePresence>

        {/* Bottom CTA */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true }}
          transition={{ duration: 0.7, ease: EASE }}
          className="mt-20 text-center"
        >
          <div className="divider mb-12" />
          <div className="luxury-card p-8 md:p-10 inline-block w-full max-w-md mx-auto">
            <div className="w-12 h-12 bg-[rgba(201,165,90,0.08)] border border-[rgba(201,165,90,0.15)] flex items-center justify-center mx-auto mb-5">
              <MessageCircle className="w-5 h-5" style={{ color: '#c9a55a' }} />
            </div>
            <h3 className="text-[#f2eddf] font-bold text-lg mb-2">עדיין יש שאלות?</h3>
            <p className="text-[#f2eddf]/35 text-sm mb-6 leading-relaxed">
              כתוב לנו — הצוות שלנו עונה תוך שעות ספורות
            </p>
            <Link href="/contact" className="btn-gold">
              צור קשר עכשיו
            </Link>
          </div>
        </motion.div>
      </section>
    </div>
  )
}
