'use client'

import { motion, AnimatePresence } from 'framer-motion'
import Link from 'next/link'
import { ShoppingBag, Minus, Plus, Trash2, ArrowRight, ShieldCheck, Truck, RotateCcw, ArrowLeft } from 'lucide-react'
import { useCart } from '@/context/CartContext'
import ProductImagePlaceholder from '@/components/shop/ProductImagePlaceholder'
import { products } from '@/lib/data'
import ProductCard from '@/components/shop/ProductCard'

const recommendedProducts = products.filter(p => p.isBestSeller).slice(0, 4)

export default function CartPage() {
  const { state, removeItem, updateQuantity, totalPrice, totalItems } = useCart()

  return (
    <div className="min-h-screen pt-20">
      {/* Header */}
      <div className="bg-[#070707] border-b border-white/5 py-8 px-4">
        <div className="max-w-7xl mx-auto">
          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6 }}
          >
            <div className="flex items-center gap-3 mb-2">
              <ShoppingBag className="w-5 h-5 text-[#b5f72e]" />
              <h1 className="font-display text-4xl md:text-5xl text-white tracking-wide uppercase">
                Your Cart
              </h1>
              {totalItems > 0 && (
                <span className="px-2 py-1 bg-[#b5f72e] text-black text-sm font-bold">
                  {totalItems} {totalItems === 1 ? 'item' : 'items'}
                </span>
              )}
            </div>
          </motion.div>
        </div>
      </div>

      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-12">
        {state.items.length === 0 ? (
          /* Empty State */
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            animate={{ opacity: 1, y: 0 }}
            className="flex flex-col items-center justify-center py-24 text-center"
          >
            <div className="w-24 h-24 border border-white/5 flex items-center justify-center mb-6">
              <ShoppingBag className="w-10 h-10 text-white/15" />
            </div>
            <h2 className="font-display text-4xl text-white tracking-wide uppercase mb-3">Cart is Empty</h2>
            <p className="text-white/40 text-sm max-w-xs mb-8">
              Looks like you haven&apos;t added any gear yet. Let&apos;s fix that.
            </p>
            <Link
              href="/shop"
              className="flex items-center gap-2 px-8 py-4 bg-[#b5f72e] text-black font-bold text-sm uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-sm transition-all group"
            >
              Start Shopping
              <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
            </Link>
          </motion.div>
        ) : (
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-10">
            {/* Cart Items */}
            <div className="lg:col-span-2">
              {/* Back to Shop */}
              <Link
                href="/shop"
                className="inline-flex items-center gap-2 text-white/30 text-xs uppercase tracking-widest hover:text-white transition-colors mb-6"
              >
                <ArrowLeft className="w-3 h-3" /> Continue Shopping
              </Link>

              <AnimatePresence mode="popLayout">
                {state.items.map((item) => {
                  const price = item.product.salePrice ?? item.product.price
                  return (
                    <motion.div
                      key={item.product.id}
                      layout
                      initial={{ opacity: 0, y: 20 }}
                      animate={{ opacity: 1, y: 0 }}
                      exit={{ opacity: 0, height: 0, marginBottom: 0 }}
                      transition={{ duration: 0.3 }}
                      className="flex gap-5 py-6 border-b border-white/5"
                    >
                      {/* Image */}
                      <Link href={`/product/${item.product.slug}`} className="w-24 h-24 md:w-28 md:h-28 flex-shrink-0 border border-white/5 overflow-hidden hover:border-white/15 transition-colors">
                        <ProductImagePlaceholder product={item.product} className="w-full h-full" />
                      </Link>

                      {/* Details */}
                      <div className="flex-1 min-w-0">
                        <div className="flex items-start justify-between gap-4">
                          <div>
                            <p className="text-white/30 text-xs uppercase tracking-widest mb-0.5">{item.product.brand}</p>
                            <Link href={`/product/${item.product.slug}`}>
                              <h3 className="text-white font-medium text-base hover:text-[#b5f72e] transition-colors leading-snug">
                                {item.product.name}
                              </h3>
                            </Link>
                            {item.product.playerLevel && (
                              <p className="text-white/20 text-xs mt-1 capitalize">{item.product.playerLevel} level</p>
                            )}
                          </div>
                          <button
                            onClick={() => removeItem(item.product.id)}
                            className="p-2 text-white/20 hover:text-red-400 transition-colors flex-shrink-0"
                          >
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </div>

                        <div className="flex items-center justify-between mt-4">
                          {/* Quantity */}
                          <div className="flex items-center border border-white/10">
                            <button
                              onClick={() => updateQuantity(item.product.id, item.quantity - 1)}
                              className="w-9 h-9 flex items-center justify-center text-white/50 hover:text-white hover:bg-white/5 transition-all"
                            >
                              <Minus className="w-3.5 h-3.5" />
                            </button>
                            <span className="w-8 text-center text-sm font-medium text-white">{item.quantity}</span>
                            <button
                              onClick={() => updateQuantity(item.product.id, item.quantity + 1)}
                              className="w-9 h-9 flex items-center justify-center text-white/50 hover:text-white hover:bg-white/5 transition-all"
                            >
                              <Plus className="w-3.5 h-3.5" />
                            </button>
                          </div>

                          {/* Price */}
                          <div className="text-right">
                            <p className="text-[#b5f72e] font-bold text-lg">€{(price * item.quantity).toFixed(2)}</p>
                            {item.quantity > 1 && (
                              <p className="text-white/25 text-xs">€{price.toFixed(2)} each</p>
                            )}
                          </div>
                        </div>
                      </div>
                    </motion.div>
                  )
                })}
              </AnimatePresence>
            </div>

            {/* Order Summary */}
            <div className="lg:col-span-1">
              <div className="sticky top-28">
                <div className="bg-[#0f0f0f] border border-white/5 p-6 mb-4">
                  <h2 className="text-white font-bold text-sm uppercase tracking-widest mb-6">Order Summary</h2>

                  <div className="space-y-3 mb-5">
                    <div className="flex justify-between text-sm">
                      <span className="text-white/40">Subtotal ({totalItems} items)</span>
                      <span className="text-white">€{totalPrice.toFixed(2)}</span>
                    </div>
                    <div className="flex justify-between text-sm">
                      <span className="text-white/40">Shipping</span>
                      <span className={totalPrice >= 75 ? 'text-[#b5f72e]' : 'text-white'}>
                        {totalPrice >= 75 ? 'FREE' : '€4.99'}
                      </span>
                    </div>
                    {totalPrice < 75 && (
                      <div className="px-3 py-2 bg-[#b5f72e]/5 border border-[#b5f72e]/15 text-xs text-white/50">
                        Add <span className="text-[#b5f72e] font-bold">€{(75 - totalPrice).toFixed(2)}</span> more for free shipping
                      </div>
                    )}
                  </div>

                  <div className="flex justify-between items-center py-4 border-t border-white/5 mb-5">
                    <span className="text-white font-bold uppercase tracking-wider text-sm">Total</span>
                    <span className="text-white font-bold text-2xl">€{(totalPrice + (totalPrice >= 75 ? 0 : 4.99)).toFixed(2)}</span>
                  </div>

                  <button className="w-full flex items-center justify-between py-4 px-5 bg-[#b5f72e] text-black font-bold text-sm uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-sm transition-all duration-300 group mb-3">
                    <span>Proceed to Checkout</span>
                    <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
                  </button>

                  <p className="text-white/20 text-xs text-center">Taxes calculated at checkout</p>
                </div>

                {/* Trust Badges */}
                <div className="space-y-2">
                  {[
                    { icon: ShieldCheck, text: 'Secure & encrypted payment' },
                    { icon: Truck, text: 'Free shipping over €75' },
                    { icon: RotateCcw, text: '30-day free returns' },
                  ].map(({ icon: Icon, text }) => (
                    <div key={text} className="flex items-center gap-3 text-white/30 text-xs">
                      <Icon className="w-4 h-4 text-[#b5f72e]/50 flex-shrink-0" />
                      {text}
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </div>
        )}

        {/* Recommended Products */}
        <section className="mt-20 pt-16 border-t border-white/5">
          <h2 className="font-display text-3xl text-white tracking-wide uppercase mb-8">
            You May Also <span className="text-[#b5f72e]">Like</span>
          </h2>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            {recommendedProducts.map((product, i) => (
              <ProductCard key={product.id} product={product} index={i} />
            ))}
          </div>
        </section>
      </div>
    </div>
  )
}
