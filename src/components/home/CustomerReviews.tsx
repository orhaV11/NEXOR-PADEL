'use client'

import { useState } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { Star, Quote, ChevronLeft, ChevronRight } from 'lucide-react'
import { reviews } from '@/lib/data'

export default function CustomerReviews() {
  const [activeIndex, setActiveIndex] = useState(0)

  const prev = () => setActiveIndex(i => (i - 1 + reviews.length) % reviews.length)
  const next = () => setActiveIndex(i => (i + 1) % reviews.length)

  return (
    <section className="py-24 overflow-hidden">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-100px' }}
          transition={{ duration: 0.7 }}
          className="text-center mb-16"
        >
          <div className="section-tag mx-auto inline-flex">Reviews</div>
          <h2 className="section-heading mt-2">
            What Players<br />
            <span className="text-[#b5f72e]">Are Saying</span>
          </h2>

          {/* Aggregate Rating */}
          <div className="flex items-center justify-center gap-3 mt-6">
            <div className="flex items-center gap-1">
              {[...Array(5)].map((_, i) => (
                <Star key={i} className="w-5 h-5 text-[#b5f72e] fill-[#b5f72e]" />
              ))}
            </div>
            <span className="text-white font-bold text-xl">4.9</span>
            <span className="text-white/30 text-sm">from 1,200+ reviews</span>
          </div>
        </motion.div>

        {/* Featured Review Carousel */}
        <div className="relative max-w-4xl mx-auto">
          <AnimatePresence mode="wait">
            <motion.div
              key={activeIndex}
              initial={{ opacity: 0, x: 60 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: -60 }}
              transition={{ duration: 0.4, ease: [0.22, 1, 0.36, 1] }}
              className="relative bg-[#0f0f0f] border border-white/5 p-8 md:p-12"
            >
              <Quote className="absolute top-8 right-8 w-10 h-10 text-[#b5f72e]/10" />

              <div className="flex items-center gap-1 mb-6">
                {[...Array(reviews[activeIndex].rating)].map((_, i) => (
                  <Star key={i} className="w-4 h-4 text-[#b5f72e] fill-[#b5f72e]" />
                ))}
              </div>

              <h3 className="text-white text-xl font-bold mb-3">{reviews[activeIndex].title}</h3>
              <p className="text-white/50 text-base leading-relaxed mb-8">{reviews[activeIndex].body}</p>

              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="w-10 h-10 bg-[#b5f72e]/10 border border-[#b5f72e]/20 flex items-center justify-center">
                    <span className="text-[#b5f72e] font-bold text-sm">{reviews[activeIndex].author[0]}</span>
                  </div>
                  <div>
                    <p className="text-white font-medium text-sm">{reviews[activeIndex].author}</p>
                    <p className="text-white/30 text-xs">{reviews[activeIndex].date}</p>
                  </div>
                </div>
                {reviews[activeIndex].verified && (
                  <span className="text-[#b5f72e] text-xs uppercase tracking-widest border border-[#b5f72e]/30 px-2 py-1">
                    ✓ Verified
                  </span>
                )}
              </div>
            </motion.div>
          </AnimatePresence>

          {/* Navigation */}
          <div className="flex items-center justify-between mt-6">
            <div className="flex items-center gap-2">
              {reviews.map((_, i) => (
                <button
                  key={i}
                  onClick={() => setActiveIndex(i)}
                  className={`transition-all duration-300 ${
                    i === activeIndex ? 'w-8 h-1.5 bg-[#b5f72e]' : 'w-1.5 h-1.5 bg-white/20 hover:bg-white/40 rounded-full'
                  }`}
                />
              ))}
            </div>
            <div className="flex items-center gap-2">
              <button
                onClick={prev}
                className="w-10 h-10 border border-white/10 flex items-center justify-center text-white/40 hover:text-white hover:border-white/30 transition-all"
              >
                <ChevronLeft className="w-5 h-5" />
              </button>
              <button
                onClick={next}
                className="w-10 h-10 border border-white/10 flex items-center justify-center text-white/40 hover:text-white hover:border-white/30 transition-all"
              >
                <ChevronRight className="w-5 h-5" />
              </button>
            </div>
          </div>
        </div>

        {/* All Reviews Grid */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mt-12">
          {reviews.map((review, i) => (
            <motion.div
              key={review.id}
              initial={{ opacity: 0, y: 20 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true, margin: '-30px' }}
              transition={{ duration: 0.5, delay: i * 0.08 }}
              className="p-5 bg-[#0c0c0c] border border-white/5 hover:border-[#b5f72e]/15 transition-all duration-300"
            >
              <div className="flex items-center gap-0.5 mb-3">
                {[...Array(review.rating)].map((_, j) => (
                  <Star key={j} className="w-3.5 h-3.5 text-[#b5f72e] fill-[#b5f72e]" />
                ))}
              </div>
              <p className="text-white/50 text-sm leading-relaxed line-clamp-3 mb-4">{review.body}</p>
              <div className="flex items-center gap-2">
                <div className="w-6 h-6 bg-[#b5f72e]/10 flex items-center justify-center">
                  <span className="text-[#b5f72e] text-xs font-bold">{review.author[0]}</span>
                </div>
                <span className="text-white/60 text-xs">{review.author}</span>
              </div>
            </motion.div>
          ))}
        </div>
      </div>
    </section>
  )
}
