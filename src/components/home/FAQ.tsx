'use client'

import { useState } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { Plus, Minus } from 'lucide-react'
import Link from 'next/link'
import { faqs } from '@/lib/data'

const LUXURY_EASE = [0.16, 1, 0.3, 1] as const

export default function FAQ() {
  const [openIndex, setOpenIndex] = useState<number | null>(0)

  return (
    <section id="faq" className="py-24 bg-[#0d0d10]">
      <div className="max-w-4xl mx-auto px-5 sm:px-8 lg:px-10">
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-100px' }}
          transition={{ duration: 0.8, ease: LUXURY_EASE }}
          className="text-center mb-16"
        >
          <span className="section-label">שאלות נפוצות</span>
          <h2 className="section-title mt-3">
            שאלות{' '}
            <span className="text-gold-gradient">נפוצות</span>
          </h2>
          <p className="text-[rgba(242,237,223,0.4)] mt-4 text-sm">
            כל מה שצריך לדעת לפני שמתחילים לשחק.
          </p>
        </motion.div>

        <div className="space-y-2">
          {faqs.map((faq, i) => (
            <motion.div
              key={i}
              initial={{ opacity: 0, y: 20 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true, margin: '-30px' }}
              transition={{ duration: 0.6, delay: i * 0.07, ease: LUXURY_EASE }}
            >
              <div
                className="border transition-all duration-300"
                style={{
                  borderColor: openIndex === i ? 'rgba(201,165,90,0.22)' : 'rgba(201,165,90,0.07)',
                  background: openIndex === i ? 'rgba(201,165,90,0.02)' : '#0d0d10',
                }}
              >
                <button
                  onClick={() => setOpenIndex(openIndex === i ? null : i)}
                  className="w-full flex items-center justify-between px-6 py-5 text-right"
                >
                  <span
                    className="text-sm font-medium tracking-wide leading-snug"
                    style={{ color: openIndex === i ? '#f2eddf' : 'rgba(242,237,223,0.7)' }}
                  >
                    {faq.question}
                  </span>
                  <div
                    className="w-6 h-6 border flex-shrink-0 flex items-center justify-center me-4 transition-all duration-300"
                    style={{
                      borderColor: openIndex === i ? 'rgba(201,165,90,0.5)' : 'rgba(242,237,223,0.1)',
                      color: openIndex === i ? '#c9a55a' : 'rgba(242,237,223,0.3)',
                      background: openIndex === i ? 'rgba(201,165,90,0.08)' : 'transparent',
                    }}
                  >
                    {openIndex === i ? <Minus className="w-3.5 h-3.5" /> : <Plus className="w-3.5 h-3.5" />}
                  </div>
                </button>

                <AnimatePresence>
                  {openIndex === i && (
                    <motion.div
                      initial={{ height: 0, opacity: 0 }}
                      animate={{ height: 'auto', opacity: 1 }}
                      exit={{ height: 0, opacity: 0 }}
                      transition={{ duration: 0.35, ease: LUXURY_EASE }}
                      className="overflow-hidden"
                    >
                      <p className="px-6 pb-5 text-[rgba(242,237,223,0.5)] text-sm leading-relaxed">
                        {faq.answer}
                      </p>
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            </motion.div>
          ))}
        </div>

        <motion.div
          initial={{ opacity: 0, y: 20 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true }}
          transition={{ duration: 0.6, delay: 0.4, ease: LUXURY_EASE }}
          className="text-center mt-12 pt-12"
          style={{ borderTop: '1px solid rgba(242,237,223,0.06)' }}
        >
          <p className="text-[rgba(242,237,223,0.4)] text-sm mb-4">עדיין יש שאלות?</p>
          <Link href="/contact" className="btn-outline">
            צור קשר עם הצוות
          </Link>
        </motion.div>
      </div>
    </section>
  )
}
