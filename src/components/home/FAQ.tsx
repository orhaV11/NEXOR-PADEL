'use client'

import { useState } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { Plus, Minus } from 'lucide-react'
import { faqs } from '@/lib/data'

export default function FAQ() {
  const [openIndex, setOpenIndex] = useState<number | null>(0)

  return (
    <section id="faq" className="py-24 bg-[#070707]">
      <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8">
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-100px' }}
          transition={{ duration: 0.7 }}
          className="text-center mb-16"
        >
          <div className="section-tag mx-auto inline-flex">FAQ</div>
          <h2 className="section-heading mt-2">
            Common<br />
            <span className="text-[#b5f72e]">Questions</span>
          </h2>
          <p className="text-white/40 mt-4">
            Everything you need to know before you play.
          </p>
        </motion.div>

        <div className="space-y-2">
          {faqs.map((faq, i) => (
            <motion.div
              key={i}
              initial={{ opacity: 0, y: 20 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true, margin: '-30px' }}
              transition={{ duration: 0.5, delay: i * 0.07 }}
            >
              <div
                className={`border transition-all duration-300 ${
                  openIndex === i ? 'border-[#b5f72e]/30 bg-[#b5f72e]/3' : 'border-white/5 bg-[#0f0f0f]'
                }`}
              >
                <button
                  onClick={() => setOpenIndex(openIndex === i ? null : i)}
                  className="w-full flex items-center justify-between px-6 py-5 text-left"
                >
                  <span className={`text-sm font-medium tracking-wide ${openIndex === i ? 'text-white' : 'text-white/70'}`}>
                    {faq.question}
                  </span>
                  <div className={`w-6 h-6 border flex-shrink-0 flex items-center justify-center ml-4 transition-all duration-300 ${
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
                      transition={{ duration: 0.3, ease: [0.22, 1, 0.36, 1] }}
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
          transition={{ duration: 0.5, delay: 0.4 }}
          className="text-center mt-12 pt-12 border-t border-white/5"
        >
          <p className="text-white/40 text-sm mb-4">Still have questions?</p>
          <a
            href="/contact"
            className="inline-flex items-center gap-2 px-6 py-3 border border-white/20 text-white text-sm uppercase tracking-widest hover:border-[#b5f72e]/50 hover:text-[#b5f72e] transition-all duration-300"
          >
            Contact Our Team
          </a>
        </motion.div>
      </div>
    </section>
  )
}
