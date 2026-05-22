'use client'

import { motion, AnimatePresence } from 'framer-motion'
import { X, Minus, Plus, Trash2, ShoppingBag, ArrowLeft } from 'lucide-react'
import Link from 'next/link'
import { useCart } from '@/context/CartContext'
import ProductImagePlaceholder from '@/components/shop/ProductImagePlaceholder'

export default function CartDrawer() {
  const { state, closeCart, removeItem, updateQuantity, totalPrice, totalItems } = useCart()

  return (
    <AnimatePresence>
      {state.isOpen && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={closeCart}
            className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50"
          />

          <motion.div
            initial={{ x: '-100%' }}
            animate={{ x: 0 }}
            exit={{ x: '-100%' }}
            transition={{ type: 'spring', damping: 28, stiffness: 260 }}
            className="fixed left-0 top-0 bottom-0 w-full max-w-sm bg-[#080808] border-r border-white/[0.05] z-50 flex flex-col"
          >
            {/* Header */}
            <div className="flex items-center justify-between px-6 py-5 border-b border-white/[0.05]">
              <button onClick={closeCart} className="p-2 text-white/30 hover:text-white hover:bg-white/[0.04] transition-all">
                <X className="w-5 h-5" />
              </button>
              <div className="flex items-center gap-3">
                {totalItems > 0 && (
                  <span className="px-2 py-0.5 bg-[#b5f72e] text-black text-xs font-bold">
                    {totalItems}
                  </span>
                )}
                <h2 className="font-bold text-base text-white tracking-wide">
                  עגלת קניות
                </h2>
                <ShoppingBag className="w-4 h-4 text-[#b5f72e]" />
              </div>
            </div>

            {/* Items */}
            <div className="flex-1 overflow-y-auto px-6 py-4">
              {state.items.length === 0 ? (
                <motion.div
                  initial={{ opacity: 0, y: 20 }}
                  animate={{ opacity: 1, y: 0 }}
                  className="flex flex-col items-center justify-center h-full gap-4 text-center"
                >
                  <div className="w-16 h-16 border border-white/[0.07] flex items-center justify-center">
                    <ShoppingBag className="w-8 h-8 text-white/15" />
                  </div>
                  <div>
                    <p className="text-white/40 text-sm font-medium">העגלה שלך ריקה</p>
                    <p className="text-white/20 text-xs mt-1">הוסף ציוד פרימיום כדי להתחיל</p>
                  </div>
                  <button
                    onClick={closeCart}
                    className="mt-2 px-6 py-3 bg-[#b5f72e] text-black text-xs font-bold uppercase tracking-widest hover:bg-[#c8ff47] transition-colors"
                  >
                    עבור לחנות
                  </button>
                </motion.div>
              ) : (
                <div className="space-y-4">
                  <AnimatePresence mode="popLayout">
                    {state.items.map((item) => (
                      <motion.div
                        key={item.product.id}
                        layout
                        initial={{ opacity: 0, x: -20 }}
                        animate={{ opacity: 1, x: 0 }}
                        exit={{ opacity: 0, x: 20, height: 0, marginBottom: 0 }}
                        transition={{ duration: 0.25 }}
                        className="flex gap-3.5 py-4 border-b border-white/[0.05]"
                      >
                        <div className="w-18 h-18 flex-shrink-0 overflow-hidden" style={{ width: 72, height: 72 }}>
                          <ProductImagePlaceholder product={item.product} className="w-full h-full" />
                        </div>

                        <div className="flex-1 min-w-0">
                          <p className="text-white/30 text-[10px] uppercase tracking-wider mb-0.5">{item.product.brand}</p>
                          <p className="text-white text-sm font-medium leading-tight line-clamp-1">{item.product.name}</p>
                          <p className="text-[#b5f72e] text-sm font-bold mt-1 ltr-text">
                            ₪{((item.product.salePrice ?? item.product.price) * item.quantity).toLocaleString()}
                          </p>

                          <div className="flex items-center gap-2 mt-2">
                            <button
                              onClick={() => updateQuantity(item.product.id, item.quantity - 1)}
                              className="w-7 h-7 border border-white/[0.08] flex items-center justify-center text-white/40 hover:text-white hover:border-white/20 transition-all"
                            >
                              <Minus className="w-3 h-3" />
                            </button>
                            <span className="w-5 text-center text-sm text-white">{item.quantity}</span>
                            <button
                              onClick={() => updateQuantity(item.product.id, item.quantity + 1)}
                              className="w-7 h-7 border border-white/[0.08] flex items-center justify-center text-white/40 hover:text-white hover:border-white/20 transition-all"
                            >
                              <Plus className="w-3 h-3" />
                            </button>
                            <button
                              onClick={() => removeItem(item.product.id)}
                              className="mr-auto p-1.5 text-white/15 hover:text-red-400 transition-colors"
                            >
                              <Trash2 className="w-3.5 h-3.5" />
                            </button>
                          </div>
                        </div>
                      </motion.div>
                    ))}
                  </AnimatePresence>
                </div>
              )}
            </div>

            {/* Footer */}
            {state.items.length > 0 && (
              <div className="border-t border-white/[0.05] px-6 py-5 space-y-3">
                <div className="flex items-center justify-between px-3 py-2 bg-[#b5f72e]/[0.04] border border-[#b5f72e]/15">
                  <span className="text-white/40 text-xs">משלוח חינם מעל ₪280</span>
                  {totalPrice >= 280 ? (
                    <span className="text-[#b5f72e] text-xs font-bold">✓ מגיע לך</span>
                  ) : (
                    <span className="text-white/30 text-xs ltr-text">₪{(280 - totalPrice).toLocaleString()} נוספים</span>
                  )}
                </div>

                <div className="flex items-center justify-between py-2">
                  <span className="text-white font-bold text-lg ltr-text">₪{totalPrice.toLocaleString()}</span>
                  <span className="text-white/50 text-sm uppercase tracking-wider">סה״כ ביניים</span>
                </div>
                <p className="text-white/20 text-xs text-left">מיסים ומשלוח יחושבו בקופה</p>

                <Link
                  href="/cart"
                  onClick={closeCart}
                  className="flex items-center justify-between w-full px-5 py-4 bg-[#b5f72e] text-black font-bold text-sm uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-sm transition-all duration-300 group"
                >
                  <ArrowLeft className="w-4 h-4 group-hover:-translate-x-1 transition-transform" />
                  <span>לתשלום</span>
                </Link>

                <button
                  onClick={closeCart}
                  className="w-full text-center text-white/25 text-xs uppercase tracking-widest hover:text-white/50 transition-colors py-1"
                >
                  המשך קנייה
                </button>
              </div>
            )}
          </motion.div>
        </>
      )}
    </AnimatePresence>
  )
}
