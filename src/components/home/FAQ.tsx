'use client'

import { useState } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { Plus, Minus } from 'lucide-react'
import { faqs } from '@/lib/data'

export default function FAQ() {
  const [openIndex, setOpenIndex] = useState<number | null>(0)

  return (
    <section id="faq" className="py-24 bg-[#070707]">
      <div className="max-w-4xl mx-auto px-5 sm:px-8 lg:px-10">
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-100px' }}
          transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
          className="text-center mb-16"
        >
          <div className="section-tag mx-auto inline-flex">שאלות נפוצות</div>
          <h2 className="section-heading mt-2">
            שאלות<br />
            <span className="text-[#b5f72e]">נפוצות</span>
          </h2>
          <p className="text-white/40 mt-4 text-sm">
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
              transition={{ duration: 0.6, delay: i * 0.07, ease: [0.16, 1, 0.3, 1] }}
            >
              <div
                className={`border transition-all duration-300 ${
                  openIndex === i ? 'border-[#b5f72e]/25 bg-[#b5f72e]/[0.02]' : 'border-white/5 bg-[#0f0f0f]'
                }`}
              >
                <button
                  onClick={() => setOpenIndex(openIndex === i ? null : i)}
                  className="w-full flex items-center justify-between px-6 py-5 text-right"
                >
                  <span className={`text-sm font-medium tracking-wide leading-snug ${openIndex === i ? 'text-white' : 'text-white/70'}`}>
                    {faq.question}
                  </span>
                  <div className={`w-6 h-6 border flex-shrink-0 flex items-center justify-center me-4 transition-all duration-300 ${
                    openIndex === i ? 'border-[#b5f72e]/50 text-[#b5f72e] bg-[#b5f72e]/10' : 'border-white/10 text-white/30'
                  }`}>
                    {openIndex === i ? <Minus className="w-3.5 h-3.5" /> : <Plus className="w-3.5 h-3.5" />}
                  </div>
                </button>

                <AnimatePresence>
                  {openIndex === i && (
                    <motion.div
                      initial={{ height: 0, opacity: 0 }}
                      animate={{ height: 'auto', opacity: 1 }}
                      exit={{ height: 0, opacity: 0 }}
                      transition={{ duration: 0.35, ease: [0.16, 1, 0.3, 1] }}
                      className="overflow-hidden"
                    >
                      <p className="px-6 pb-5 text-white/50 text-sm leading-relaxed">
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
          transition={{ duration: 0.6, delay: 0.4, ease: [0.16, 1, 0.3, 1] }}
          className="text-center mt-12 pt-12 border-t border-white/5"
        >
          <p className="text-white/40 text-sm mb-4">עדיין יש שאלות?</p>
          <a
            href="/contact"
            className="inline-flex items-center gap-2 px-6 py-3 border border-white/20 text-white text-sm uppercase tracking-widest hover:border-[#b5f72e]/50 hover:text-[#b5f72e] transition-all duration-300"
          >
            צור קשר עם הצוות
          </a>
        </motion.div>
      </div>
    </section>
  )
}
