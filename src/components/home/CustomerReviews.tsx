'use client'

import { useState } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { Star, Quote, ChevronRight, ChevronLeft } from 'lucide-react'
import { reviews } from '@/lib/data'

const LUXURY_EASE = [0.16, 1, 0.3, 1] as const

export default function CustomerReviews() {
  const [activeIndex, setActiveIndex] = useState(0)

  const prev = () => setActiveIndex(i => (i - 1 + reviews.length) % reviews.length)
  const next = () => setActiveIndex(i => (i + 1) % reviews.length)

  return (
    <section className="py-24 overflow-hidden bg-[#08080a]">
      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-100px' }}
          transition={{ duration: 0.8, ease: LUXURY_EASE }}
          className="text-center mb-16"
        >
          <span className="section-label">ביקורות</span>
          <h2 className="section-title mt-3">
            מה השחקנים{' '}
            <span className="text-gold-gradient">אומרים</span>
          </h2>

          <div className="flex items-center justify-center gap-3 mt-6">
            <div className="flex items-center gap-1">
              {[...Array(5)].map((_, i) => (
                <Star key={i} size={16} style={{ color: '#c9a55a' }} fill="#c9a55a" />
              ))}
            </div>
            <span className="text-[#f2eddf] font-bold text-xl ltr">4.9</span>
            <span className="text-[rgba(242,237,223,0.35)] text-sm">מתוך +1,200 ביקורות</span>
          </div>
        </motion.div>

        {/* Featured Review Carousel */}
        <div className="relative max-w-4xl mx-auto">
          <AnimatePresence mode="wait">
            <motion.div
              key={activeIndex}
              initial={{ opacity: 0, x: -60 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: 60 }}
              transition={{ duration: 0.45, ease: LUXURY_EASE }}
              className="relative bg-[#0d0d10] border border-[rgba(201,165,90,0.1)] p-8 md:p-12"
            >
              <Quote className="absolute top-8 left-8 w-10 h-10 text-[rgba(201,165,90,0.08)]" />

              <div className="flex items-center gap-1 mb-6">
                {[...Array(reviews[activeIndex].rating)].map((_, i) => (
                  <Star key={i} size={14} style={{ color: '#c9a55a' }} fill="#c9a55a" />
                ))}
              </div>

              <h3 className="text-[#f2eddf] text-xl font-bold mb-3">{reviews[activeIndex].title}</h3>
              <p className="text-[rgba(242,237,223,0.5)] text-base leading-relaxed mb-8">{reviews[activeIndex].body}</p>

              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="w-10 h-10 bg-[rgba(201,165,90,0.08)] border border-[rgba(201,165,90,0.2)] flex items-center justify-center">
                    <span className="text-[#c9a55a] font-bold text-sm">{reviews[activeIndex].author[0]}</span>
                  </div>
                  <div>
                    <p className="text-[#f2eddf] font-medium text-sm">{reviews[activeIndex].author}</p>
                    <p className="text-[rgba(242,237,223,0.3)] text-xs">{reviews[activeIndex].date}</p>
                  </div>
                </div>
                {reviews[activeIndex].verified && (
                  <span className="text-[#c9a55a] text-xs uppercase tracking-widest border border-[rgba(201,165,90,0.3)] px-2 py-1">
                    ✓ מאומת
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
                    i === activeIndex ? 'w-8 h-1.5 bg-[#c9a55a]' : 'w-1.5 h-1.5 bg-[rgba(242,237,223,0.15)] hover:bg-[rgba(242,237,223,0.3)] rounded-full'
                  }`}
                />
              ))}
            </div>
            <div className="flex items-center gap-2">
              <button
                onClick={next}
                className="w-10 h-10 border border-[rgba(201,165,90,0.12)] flex items-center justify-center text-[rgba(242,237,223,0.35)] hover:text-[#f2eddf] hover:border-[rgba(201,165,90,0.35)] transition-all"
              >
                <ChevronRight className="w-5 h-5" />
              </button>
              <button
                onClick={prev}
                className="w-10 h-10 border border-[rgba(201,165,90,0.12)] flex items-center justify-center text-[rgba(242,237,223,0.35)] hover:text-[#f2eddf] hover:border-[rgba(201,165,90,0.35)] transition-all"
              >
                <ChevronLeft className="w-5 h-5" />
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
              transition={{ duration: 0.6, delay: i * 0.09, ease: LUXURY_EASE }}
              className="p-5 bg-[#0d0d10] border border-[rgba(201,165,90,0.07)] hover:border-[rgba(201,165,90,0.18)] transition-all duration-300"
            >
              <div className="flex items-center gap-0.5 mb-3">
                {[...Array(review.rating)].map((_, j) => (
                  <Star key={j} size={12} style={{ color: '#c9a55a' }} fill="#c9a55a" />
                ))}
              </div>
              <p className="text-[rgba(242,237,223,0.5)] text-sm leading-relaxed line-clamp-3 mb-4">{review.body}</p>
              <div className="flex items-center gap-2">
                <div className="w-6 h-6 bg-[rgba(201,165,90,0.08)] flex items-center justify-center">
                  <span className="text-[#c9a55a] text-xs font-bold">{review.author[0]}</span>
                </div>
                <span className="text-[rgba(242,237,223,0.6)] text-xs">{review.author}</span>
              </div>
            </motion.div>
          ))}
        </div>
      </div>
    </section>
  )
}
