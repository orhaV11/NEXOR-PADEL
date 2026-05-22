'use client'

import { motion, AnimatePresence } from 'framer-motion'
import { X, Minus, Plus, Trash2, ShoppingBag, ArrowRight } from 'lucide-react'
import Link from 'next/link'
import { useCart } from '@/context/CartContext'
import ProductImagePlaceholder from '@/components/shop/ProductImagePlaceholder'

export default function CartDrawer() {
  const { state, closeCart, removeItem, updateQuantity, totalPrice, totalItems } = useCart()

  return (
    <AnimatePresence>
      {state.isOpen && (
        <>
          {/* Backdrop */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={closeCart}
            className="fixed inset-0 bg-black/70 backdrop-blur-sm z-50"
          />

          {/* Drawer */}
          <motion.div
            initial={{ x: '100%' }}
            animate={{ x: 0 }}
            exit={{ x: '100%' }}
            transition={{ type: 'spring', damping: 30, stiffness: 300 }}
            className="fixed right-0 top-0 bottom-0 w-full max-w-md bg-[#0a0a0a] border-l border-white/5 z-50 flex flex-col"
          >
            {/* Header */}
            <div className="flex items-center justify-between px-6 py-5 border-b border-white/5">
              <div className="flex items-center gap-3">
                <ShoppingBag className="w-5 h-5 text-[#b5f72e]" />
                <h2 className="text-lg font-display tracking-widest uppercase text-white">
                  Your Cart
                </h2>
                {totalItems > 0 && (
                  <span className="px-2 py-0.5 bg-[#b5f72e] text-black text-xs font-bold">
                    {totalItems}
                  </span>
                )}
              </div>
              <button
                onClick={closeCart}
                className="p-2 text-white/40 hover:text-white hover:bg-white/5 transition-all duration-200"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            {/* Cart Items */}
            <div className="flex-1 overflow-y-auto px-6 py-4">
              {state.items.length === 0 ? (
                <motion.div
                  initial={{ opacity: 0, y: 20 }}
                  animate={{ opacity: 1, y: 0 }}
                  className="flex flex-col items-center justify-center h-full gap-4 text-center"
                >
                  <div className="w-16 h-16 border border-white/10 flex items-center justify-center">
                    <ShoppingBag className="w-8 h-8 text-white/20" />
                  </div>
                  <div>
                    <p className="text-white/40 text-sm">Your cart is empty</p>
                    <p className="text-white/20 text-xs mt-1">Add some premium gear to get started</p>
                  </div>
                  <button
                    onClick={closeCart}
                    className="mt-2 px-6 py-3 bg-[#b5f72e] text-black text-xs font-bold uppercase tracking-widest hover:bg-[#c8ff47] transition-colors"
                  >
                    Start Shopping
                  </button>
                </motion.div>
              ) : (
                <div className="space-y-4">
                  <AnimatePresence mode="popLayout">
                    {state.items.map((item) => (
                      <motion.div
                        key={item.product.id}
                        layout
                        initial={{ opacity: 0, x: 20 }}
                        animate={{ opacity: 1, x: 0 }}
                        exit={{ opacity: 0, x: -20, height: 0, marginBottom: 0 }}
                        transition={{ duration: 0.2 }}
                        className="flex gap-4 py-4 border-b border-white/5"
                      >
                        {/* Product Image */}
                        <div className="w-20 h-20 flex-shrink-0 overflow-hidden">
                          <ProductImagePlaceholder product={item.product} className="w-full h-full" />
                        </div>

                        {/* Product Info */}
                        <div className="flex-1 min-w-0">
                          <p className="text-white/40 text-xs uppercase tracking-wider mb-0.5">{item.product.brand}</p>
                          <p className="text-white text-sm font-medium leading-tight line-clamp-1">{item.product.name}</p>
                          <p className="text-[#b5f72e] text-sm font-bold mt-1">
                            €{((item.product.salePrice ?? item.product.price) * item.quantity).toFixed(2)}
                          </p>

                          {/* Quantity Controls */}
                          <div className="flex items-center gap-2 mt-2">
                            <button
                              onClick={() => updateQuantity(item.product.id, item.quantity - 1)}
                              className="w-7 h-7 border border-white/10 flex items-center justify-center text-white/60 hover:text-white hover:border-white/30 transition-all"
                            >
                              <Minus className="w-3 h-3" />
                            </button>
                            <span className="w-6 text-center text-sm text-white">{item.quantity}</span>
                            <button
                              onClick={() => updateQuantity(item.product.id, item.quantity + 1)}
                              className="w-7 h-7 border border-white/10 flex items-center justify-center text-white/60 hover:text-white hover:border-white/30 transition-all"
                            >
                              <Plus className="w-3 h-3" />
                            </button>
                            <button
                              onClick={() => removeItem(item.product.id)}
                              className="ml-auto p-1.5 text-white/20 hover:text-red-400 transition-colors"
                            >
                              <Trash2 className="w-4 h-4" />
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
              <div className="border-t border-white/5 px-6 py-5 space-y-4">
                {/* Free shipping banner */}
                <div className="flex items-center justify-between px-3 py-2 bg-[#b5f72e]/5 border border-[#b5f72e]/20">
                  <span className="text-white/50 text-xs">Free shipping on orders over €75</span>
                  {totalPrice >= 75 ? (
                    <span className="text-[#b5f72e] text-xs font-bold">✓ Unlocked</span>
                  ) : (
                    <span className="text-white/40 text-xs">€{(75 - totalPrice).toFixed(2)} away</span>
                  )}
                </div>

                {/* Subtotal */}
                <div className="flex items-center justify-between">
                  <span className="text-white/60 text-sm uppercase tracking-wider">Subtotal</span>
                  <span className="text-white text-xl font-bold">€{totalPrice.toFixed(2)}</span>
                </div>
                <p className="text-white/30 text-xs">Shipping and taxes calculated at checkout</p>

                {/* Checkout Button */}
                <Link
                  href="/cart"
                  onClick={closeCart}
                  className="flex items-center justify-between w-full px-6 py-4 bg-[#b5f72e] text-black font-bold text-sm uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-md transition-all duration-300 group"
                >
                  <span>Checkout</span>
                  <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
                </Link>

                <button
                  onClick={closeCart}
                  className="w-full text-center text-white/30 text-xs uppercase tracking-widest hover:text-white/60 transition-colors py-1"
                >
                  Continue Shopping
                </button>
              </div>
            )}
          </motion.div>
        </>
      )}
    </AnimatePresence>
  )
}
