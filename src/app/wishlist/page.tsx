'use client'

import { motion, AnimatePresence } from 'framer-motion'
import Link from 'next/link'
import { Heart, ShoppingBag, Trash2 } from 'lucide-react'
import { useWishlist } from '@/context/WishlistContext'
import ProductCard from '@/components/shop/ProductCard'

const EASE = [0.16, 1, 0.3, 1] as const

export default function WishlistPage() {
  const { items, count, removeItem } = useWishlist()

  return (
    <div className="min-h-screen pt-20">
      {/* Header */}
      <section className="relative py-16 md:py-24 overflow-hidden">
        <div className="absolute inset-0 bg-[#0d0d10]" />
        <div className="absolute inset-0 bg-grid opacity-25" />
        <div
          className="absolute inset-0 opacity-10"
          style={{ background: 'radial-gradient(ellipse at 50% 0%, rgba(201,165,90,0.15) 0%, transparent 60%)' }}
        />

        <div className="relative max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.8, ease: EASE }}
            className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-6"
          >
            <div>
              <div className="section-label mb-4">הפריטים שאהבת</div>
              <h1 className="section-title flex items-center gap-4">
                מועדפים
                {count > 0 && (
                  <span className="badge-gold text-sm font-bold">
                    {count}
                  </span>
                )}
              </h1>
            </div>

            {count > 0 && (
              <motion.button
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                transition={{ delay: 0.3 }}
                onClick={() => items.forEach(p => removeItem(p.id))}
                className="flex items-center gap-2 text-[#f2eddf]/30 text-xs uppercase tracking-widest hover:text-[#f2eddf]/60 transition-colors pb-1"
              >
                <Trash2 className="w-3.5 h-3.5" />
                נקה הכל
              </motion.button>
            )}
          </motion.div>
        </div>
      </section>

      <div className="divider" />

      {/* Content */}
      <section className="py-16 max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
        <AnimatePresence mode="wait">
          {count === 0 ? (
            /* Empty State */
            <motion.div
              key="empty"
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -20 }}
              transition={{ duration: 0.5, ease: EASE }}
              className="flex flex-col items-center justify-center py-24 text-center"
            >
              <div className="w-24 h-24 mb-8 bg-[rgba(201,165,90,0.06)] border border-[rgba(201,165,90,0.12)] flex items-center justify-center">
                <Heart className="w-10 h-10" style={{ color: 'rgba(201,165,90,0.35)' }} />
              </div>

              <h2 className="text-[#f2eddf] font-bold text-2xl mb-3">ריק כאן</h2>
              <p className="text-[#f2eddf]/40 text-sm mb-2 max-w-xs leading-relaxed">
                אין מועדפים עדיין
              </p>
              <p className="text-[#f2eddf]/25 text-xs mb-10 max-w-xs leading-relaxed">
                לחץ על לב על כל מוצר כדי לשמור אותו כאן לצפייה מאוחרת
              </p>

              <Link href="/shop" className="btn-gold">
                <ShoppingBag className="w-4 h-4" />
                גלה מוצרים
              </Link>
            </motion.div>
          ) : (
            /* Products Grid */
            <motion.div
              key="grid"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              transition={{ duration: 0.4 }}
            >
              <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3 sm:gap-4">
                <AnimatePresence>
                  {items.map((product, i) => (
                    <motion.div
                      key={product.id}
                      layout
                      initial={{ opacity: 0, scale: 0.95 }}
                      animate={{ opacity: 1, scale: 1 }}
                      exit={{ opacity: 0, scale: 0.9 }}
                      transition={{ duration: 0.4, delay: i * 0.05, ease: EASE }}
                    >
                      <ProductCard product={product} index={i} />
                    </motion.div>
                  ))}
                </AnimatePresence>
              </div>

              {/* Bottom CTA */}
              <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.4, duration: 0.6, ease: EASE }}
                className="mt-16 text-center"
              >
                <div className="divider-subtle mb-8" />
                <p className="text-[#f2eddf]/30 text-xs uppercase tracking-widest mb-6">
                  המשך לגלות
                </p>
                <Link href="/shop" className="btn-outline">
                  <ShoppingBag className="w-4 h-4" />
                  כל המוצרים
                </Link>
              </motion.div>
            </motion.div>
          )}
        </AnimatePresence>
      </section>
    </div>
  )
}
