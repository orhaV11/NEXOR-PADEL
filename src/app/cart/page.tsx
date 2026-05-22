'use client'

import { motion, AnimatePresence } from 'framer-motion'
import Link from 'next/link'
import { ShoppingBag, Minus, Plus, Trash2, ArrowLeft, ShieldCheck, Truck, RotateCcw, MessageCircle } from 'lucide-react'
import { useCart } from '@/context/CartContext'
import ProductImagePlaceholder from '@/components/shop/ProductImagePlaceholder'
import { products } from '@/lib/data'
import ProductCard from '@/components/shop/ProductCard'
import { playerLevelLabels } from '@/lib/data'

const recommendedProducts = products.filter(p => p.isBestSeller).slice(0, 4)

export default function CartPage() {
  const { state, removeItem, updateQuantity, totalPrice, totalItems } = useCart()

  return (
    <div className="min-h-screen pt-20">
      {/* Header */}
      <div className="bg-[#0d0d10] border-b border-[rgba(201,165,90,0.08)] py-8 px-5">
        <div className="max-w-7xl mx-auto">
          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6, ease: [0.16, 1, 0.3, 1] }}
          >
            <div className="flex items-center gap-3 mb-2">
              <ShoppingBag className="w-5 h-5 text-[#c9a55a]" />
              <h1 className="font-display text-4xl md:text-5xl text-[#f2eddf] tracking-wide uppercase">
                עגלת הקניות שלך
              </h1>
              {totalItems > 0 && (
                <span className="badge-gold ltr">
                  {totalItems} פריטים
                </span>
              )}
            </div>
          </motion.div>
        </div>
      </div>

      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 py-12">
        {state.items.length === 0 ? (
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            animate={{ opacity: 1, y: 0 }}
            className="flex flex-col items-center justify-center py-24 text-center"
          >
            <div className="w-24 h-24 border border-[rgba(201,165,90,0.1)] flex items-center justify-center mb-6">
              <ShoppingBag className="w-10 h-10 text-[rgba(242,237,223,0.15)]" />
            </div>
            <h2 className="font-display text-4xl text-[#f2eddf] tracking-wide uppercase mb-3">העגלה ריקה</h2>
            <p className="text-[rgba(242,237,223,0.4)] text-sm max-w-xs mb-8">
              נראה שעדיין לא הוספת ציוד. בוא נתקן את זה.
            </p>
            <Link href="/shop" className="btn-gold">
              <ArrowLeft className="w-4 h-4" />
              עבור לחנות
            </Link>
          </motion.div>
        ) : (
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-10">
            {/* Cart Items */}
            <div className="lg:col-span-2">
              {/* Back to Shop */}
              <Link
                href="/shop"
                className="inline-flex items-center gap-2 text-[rgba(242,237,223,0.3)] text-xs uppercase tracking-widest hover:text-[#f2eddf] transition-colors mb-6"
              >
                <ArrowLeft className="w-3 h-3" /> המשך קנייה
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
                      className="flex gap-5 py-6 border-b border-[rgba(201,165,90,0.08)]"
                    >
                      {/* Image */}
                      <Link href={`/product/${item.product.slug}`} className="w-24 h-24 md:w-28 md:h-28 flex-shrink-0 border border-[rgba(201,165,90,0.08)] overflow-hidden hover:border-[rgba(201,165,90,0.2)] transition-colors">
                        <ProductImagePlaceholder product={item.product} className="w-full h-full" />
                      </Link>

                      {/* Details */}
                      <div className="flex-1 min-w-0">
                        <div className="flex items-start justify-between gap-4">
                          <div>
                            <p className="text-[rgba(242,237,223,0.3)] text-xs uppercase tracking-widest mb-0.5">{item.product.brand}</p>
                            <Link href={`/product/${item.product.slug}`}>
                              <h3 className="text-[#f2eddf] font-medium text-base hover:text-[#c9a55a] transition-colors leading-snug">
                                {item.product.name}
                              </h3>
                            </Link>
                            {item.product.playerLevel && (
                              <p className="text-[rgba(242,237,223,0.2)] text-xs mt-1">
                                {playerLevelLabels[item.product.playerLevel as keyof typeof playerLevelLabels] || item.product.playerLevel}
                              </p>
                            )}
                          </div>
                          <button
                            onClick={() => removeItem(item.product.id)}
                            className="p-2 text-[rgba(242,237,223,0.2)] hover:text-red-400 transition-colors flex-shrink-0"
                          >
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </div>

                        <div className="flex items-center justify-between mt-4">
                          {/* Quantity */}
                          <div className="flex items-center border border-[rgba(201,165,90,0.1)]">
                            <button
                              onClick={() => updateQuantity(item.product.id, item.quantity - 1)}
                              className="w-9 h-9 flex items-center justify-center text-[rgba(242,237,223,0.5)] hover:text-[#f2eddf] hover:bg-[rgba(255,255,255,0.05)] transition-all"
                            >
                              <Minus className="w-3.5 h-3.5" />
                            </button>
                            <span className="w-8 text-center text-sm font-medium text-[#f2eddf]">{item.quantity}</span>
                            <button
                              onClick={() => updateQuantity(item.product.id, item.quantity + 1)}
                              className="w-9 h-9 flex items-center justify-center text-[rgba(242,237,223,0.5)] hover:text-[#f2eddf] hover:bg-[rgba(255,255,255,0.05)] transition-all"
                            >
                              <Plus className="w-3.5 h-3.5" />
                            </button>
                          </div>

                          {/* Price */}
                          <div className="text-left">
                            <p className="text-[#c9a55a] font-bold text-lg ltr">₪{(price * item.quantity).toLocaleString()}</p>
                            {item.quantity > 1 && (
                              <p className="text-[rgba(242,237,223,0.25)] text-xs ltr">₪{price.toLocaleString()} ליחידה</p>
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
                <div className="luxury-card p-6 mb-4">
                  <h2 className="text-[#f2eddf] font-bold text-sm uppercase tracking-widest mb-6">סיכום הזמנה</h2>

                  <div className="space-y-3 mb-5">
                    <div className="flex justify-between text-sm">
                      <span className="text-[rgba(242,237,223,0.4)]">סה״כ ביניים ({totalItems} פריטים)</span>
                      <span className="text-[#f2eddf] ltr">₪{totalPrice.toLocaleString()}</span>
                    </div>
                    <div className="flex justify-between text-sm">
                      <span className="text-[rgba(242,237,223,0.4)]">משלוח</span>
                      <span className={totalPrice >= 280 ? 'text-[#c9a55a]' : 'text-[#f2eddf]'}>
                        {totalPrice >= 280 ? 'חינם' : '₪29'}
                      </span>
                    </div>
                    {totalPrice < 280 && (
                      <div className="px-3 py-2 bg-[rgba(201,165,90,0.05)] border border-[rgba(201,165,90,0.15)] text-xs text-[rgba(242,237,223,0.5)]">
                        הוסף <span className="text-[#c9a55a] font-bold ltr">₪{(280 - totalPrice).toLocaleString()}</span> לקבלת משלוח חינם
                      </div>
                    )}
                  </div>

                  <div className="flex justify-between items-center py-4 border-t border-[rgba(201,165,90,0.08)] mb-5">
                    <span className="text-[#f2eddf] font-bold uppercase tracking-wider text-sm">סה״כ</span>
                    <span className="text-[#f2eddf] font-bold text-2xl ltr">₪{(totalPrice + (totalPrice >= 280 ? 0 : 29)).toLocaleString()}</span>
                  </div>

                  <button className="btn-gold w-full mb-3 justify-center gap-2">
                    <span>המשך לקופה</span>
                    <ArrowLeft className="w-4 h-4" />
                  </button>

                  <Link href="/shop" className="btn-outline w-full mb-4 justify-center text-xs">
                    המשך קנייה
                  </Link>

                  <p className="text-[rgba(242,237,223,0.2)] text-xs text-center mb-4">מיסים ומשלוח יחושבו בקופה</p>
                </div>

                {/* Trust Badges */}
                <div className="luxury-card p-4 space-y-3">
                  <p className="text-[rgba(242,237,223,0.25)] text-[10px] uppercase tracking-widest mb-2">למה לקנות ב-NEXOR</p>
                  {[
                    { icon: ShieldCheck, text: 'תשלום מאובטח ומוצפן' },
                    { icon: Truck, text: 'משלוח מהיר בישראל — חינם מעל ₪280' },
                    { icon: RotateCcw, text: 'החזרה חינם עד 30 יום' },
                    { icon: MessageCircle, text: 'שירות לקוחות זמין בוואטסאפ' },
                  ].map(({ icon: Icon, text }) => (
                    <div key={text} className="flex items-center gap-3 text-[rgba(242,237,223,0.4)] text-xs">
                      <Icon className="w-4 h-4 text-[rgba(201,165,90,0.6)] flex-shrink-0" />
                      {text}
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </div>
        )}

        {/* Recommended Products */}
        <section className="mt-20 pt-16 border-t border-[rgba(201,165,90,0.08)]">
          <h2 className="font-display text-3xl text-[#f2eddf] tracking-wide uppercase mb-8">
            אולי תאהב גם <span className="text-gold-gradient">את אלה</span>
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
