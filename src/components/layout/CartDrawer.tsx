'use client'

import { useRef } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { X, Minus, Plus, Trash2, ShoppingBag, ArrowLeft, ShieldCheck, Truck, MessageCircle } from 'lucide-react'
import Link from 'next/link'
import { useCart } from '@/context/CartContext'
import ProductImagePlaceholder from '@/components/shop/ProductImagePlaceholder'

/* ── constants ────────────────────────────────────────── */

const FREE_SHIPPING_THRESHOLD = 280

/* ── sub-components ───────────────────────────────────── */

function DrawerHeader({ onClose, itemCount }: { onClose: () => void; itemCount: number }) {
  return (
    <div className="flex items-center justify-between px-6 py-5 border-b border-[rgba(201,165,90,0.08)]">
      {/* Close — start side (right in RTL) */}
      <button
        onClick={onClose}
        aria-label="סגור עגלה"
        className="p-2 text-[rgba(242,237,223,0.28)] hover:text-[#f2eddf]
                   border border-[rgba(201,165,90,0.1)] hover:border-[rgba(201,165,90,0.35)]
                   hover:bg-[rgba(201,165,90,0.04)]
                   transition-all duration-300"
      >
        <X className="w-[17px] h-[17px]" />
      </button>

      {/* Title + icon — end side */}
      <div className="flex items-center gap-3">
        {itemCount > 0 && (
          <motion.span
            key={itemCount}
            initial={{ scale: 0.6, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ type: 'spring', stiffness: 480, damping: 22 }}
            className="min-w-[22px] h-[22px] px-1.5
                       bg-[#c9a55a] text-[#08080a] text-[9px] font-black
                       flex items-center justify-center ltr"
          >
            {itemCount > 99 ? '99+' : itemCount}
          </motion.span>
        )}
        <h2 className="font-bold text-[15px] text-[#f2eddf] tracking-wide">
          עגלת קניות
        </h2>
        <ShoppingBag className="w-[17px] h-[17px] text-[#c9a55a]" aria-hidden="true" />
      </div>
    </div>
  )
}

/* ── empty state ──────────────────────────────────────── */

function EmptyCart({ onClose }: { onClose: () => void }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 20 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.45, ease: [0.16, 1, 0.3, 1] }}
      className="flex flex-col items-center justify-center h-full gap-5 text-center px-8"
    >
      {/* Icon frame */}
      <div className="relative">
        <div
          className="w-20 h-20 border border-[rgba(201,165,90,0.12)] flex items-center justify-center"
          style={{
            background: 'radial-gradient(ellipse at center, rgba(201,165,90,0.05) 0%, transparent 70%)',
          }}
        >
          {/* Floating animation on the bag icon */}
          <motion.div
            animate={{ y: [0, -6, 0] }}
            transition={{ duration: 3, repeat: Infinity, ease: 'easeInOut' }}
          >
            <ShoppingBag className="w-8 h-8 text-[rgba(201,165,90,0.3)]" aria-hidden="true" />
          </motion.div>
        </div>
        {/* Decorative corner marks */}
        <div className="absolute -top-px -right-px w-2 h-2 border-t border-r border-[rgba(201,165,90,0.35)]" aria-hidden="true" />
        <div className="absolute -bottom-px -left-px w-2 h-2 border-b border-l border-[rgba(201,165,90,0.35)]" aria-hidden="true" />
      </div>

      <div className="space-y-1.5">
        <p className="text-[#f2eddf] font-semibold text-base tracking-wide">
          העגלה ריקה
        </p>
        <p className="text-[rgba(242,237,223,0.35)] text-sm leading-relaxed max-w-[22ch]">
          הוסף ציוד פרימיום וחזור לכאן
        </p>
      </div>

      <Link
        href="/shop"
        onClick={onClose}
        className="btn-gold mt-2 px-8 py-3 text-[10px] rounded-none"
      >
        עבור לחנות
        <ArrowLeft className="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
      </Link>
    </motion.div>
  )
}

/* ── free-shipping progress bar ──────────────────────── */

function ShippingBanner({ totalPrice }: { totalPrice: number }) {
  const reached   = totalPrice >= FREE_SHIPPING_THRESHOLD
  const remaining = Math.max(0, FREE_SHIPPING_THRESHOLD - totalPrice)
  const progress  = Math.min(100, (totalPrice / FREE_SHIPPING_THRESHOLD) * 100)

  return (
    <div
      className="px-4 py-3 border border-[rgba(201,165,90,0.1)]
                 bg-[rgba(201,165,90,0.03)]"
      role="status"
      aria-live="polite"
    >
      <div className="flex items-center justify-between mb-2">
        <span className="text-[rgba(242,237,223,0.45)] text-[11px] tracking-wide">
          משלוח חינם מעל{' '}
          <span className="ltr">₪{FREE_SHIPPING_THRESHOLD.toLocaleString()}</span>
        </span>

        {reached ? (
          <motion.span
            initial={{ scale: 0.8, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            className="text-[#c9a55a] text-[11px] font-bold flex items-center gap-1"
          >
            <span aria-hidden="true">✓</span> מגיע לך!
          </motion.span>
        ) : (
          <span className="text-[rgba(242,237,223,0.3)] text-[11px] ltr">
            עוד{' '}
            <span className="ltr">₪{remaining.toLocaleString()}</span>
          </span>
        )}
      </div>

      {/* Progress track — taller with glowing dot */}
      <div className="relative h-[3px] bg-[rgba(242,237,223,0.06)] overflow-visible">
        <motion.div
          className="absolute top-0 left-0 h-full bg-gradient-to-r from-[#b08840] to-[#e2c890]"
          animate={{ width: `${progress}%` }}
          transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
        />
        {!reached && (
          <motion.div
            className="absolute top-1/2 -translate-y-1/2 w-2 h-2 rounded-full bg-[#c9a55a] shadow-[0_0_6px_#c9a55a]"
            animate={{ left: `${progress}%` }}
            transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
          />
        )}
      </div>
    </div>
  )
}

/* ── cart item row ────────────────────────────────────── */

function CartItemRow({
  item,
  onUpdateQuantity,
  onRemove,
}: {
  item: { product: import('@/lib/types').Product; quantity: number }
  onUpdateQuantity: (id: string, qty: number) => void
  onRemove: (id: string) => void
}) {
  const { product, quantity } = item
  const unitPrice   = product.salePrice ?? product.price
  const lineTotal   = unitPrice * quantity

  return (
    <motion.div
      layout
      initial={{ opacity: 0, x: -18 }}
      animate={{ opacity: 1, x: 0 }}
      exit={{ opacity: 0, x: 30, height: 0, marginBottom: 0, paddingTop: 0, paddingBottom: 0 }}
      transition={{ duration: 0.28, ease: [0.16, 1, 0.3, 1] }}
      className="relative flex gap-3.5 py-5 border-b border-[rgba(201,165,90,0.07)] group
                 before:absolute before:end-0 before:top-2 before:bottom-2 before:w-[2px]
                 before:bg-[#c9a55a] before:scale-y-0 hover:before:scale-y-100
                 before:transition-transform before:duration-300"
    >
      {/* Thumbnail — slightly bigger on mobile via Tailwind responsive */}
      <div className="w-20 h-20 sm:w-[72px] sm:h-[72px] shrink-0
                      border border-[rgba(201,165,90,0.08)]
                      group-hover:border-[rgba(201,165,90,0.22)] transition-colors duration-300 overflow-hidden">
        <motion.div
          layoutId={`cart-thumb-${product.id}`}
          className="w-full h-full"
        >
          <ProductImagePlaceholder product={product} className="w-full h-full" />
        </motion.div>
      </div>

      {/* Details */}
      <div className="flex-1 min-w-0 flex flex-col gap-0.5">
        {/* Brand */}
        <p className="text-[#c9a55a] text-[10px] uppercase tracking-wider font-semibold opacity-80">
          {product.brand}
        </p>
        {/* Name */}
        <p className="text-[#f2eddf] text-sm font-medium leading-snug line-clamp-2">
          {product.name}
        </p>
        {/* Line price */}
        <p className="text-[#e2c890] text-sm font-bold mt-0.5 ltr self-start">
          ₪{lineTotal.toLocaleString('he-IL')}
        </p>

        {/* Quantity controls + trash */}
        <div className="flex items-center gap-2 mt-2">
          {/* Decrease */}
          <button
            onClick={() => onUpdateQuantity(product.id, quantity - 1)}
            aria-label={`הפחת כמות של ${product.name}`}
            className="w-7 h-7 border border-[rgba(201,165,90,0.12)]
                       flex items-center justify-center
                       text-[rgba(242,237,223,0.35)]
                       hover:text-[#f2eddf] hover:border-[rgba(201,165,90,0.38)]
                       hover:bg-[rgba(201,165,90,0.05)]
                       transition-all duration-200 shrink-0"
          >
            <Minus className="w-3 h-3" />
          </button>

          {/* Count — animated when it changes */}
          <div className="w-7 overflow-hidden relative" style={{ height: '1.5rem' }}>
            <AnimatePresence mode="popLayout" initial={false}>
              <motion.span
                key={quantity}
                initial={{ opacity: 0, y: -10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: 10 }}
                transition={{ duration: 0.18, ease: 'easeOut' }}
                className="absolute inset-0 flex items-center justify-center
                           text-sm text-[#f2eddf] font-semibold tabular-nums ltr"
                aria-label={`כמות: ${quantity}`}
              >
                {quantity}
              </motion.span>
            </AnimatePresence>
          </div>

          {/* Increase */}
          <button
            onClick={() => onUpdateQuantity(product.id, quantity + 1)}
            aria-label={`הוסף כמות של ${product.name}`}
            className="w-7 h-7 border border-[rgba(201,165,90,0.12)]
                       flex items-center justify-center
                       text-[rgba(242,237,223,0.35)]
                       hover:text-[#f2eddf] hover:border-[rgba(201,165,90,0.38)]
                       hover:bg-[rgba(201,165,90,0.05)]
                       transition-all duration-200 shrink-0"
          >
            <Plus className="w-3 h-3" />
          </button>

          {/* Remove — pushed to far side */}
          <button
            onClick={() => onRemove(product.id)}
            aria-label={`הסר ${product.name} מהעגלה`}
            className="ms-auto p-1.5 text-[rgba(242,237,223,0.18)]
                       hover:text-red-400 transition-colors duration-200
                       hover:bg-red-500/[0.07]"
          >
            <Trash2 className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </motion.div>
  )
}

/* ── main component ───────────────────────────────────── */

export default function CartDrawer() {
  const {
    state,
    closeCart,
    removeItem,
    updateQuantity,
    totalPrice,
    totalItems,
  } = useCart()

  return (
    <AnimatePresence>
      {state.isOpen && (
        <>
          {/* ── Backdrop ── */}
          <motion.div
            key="cart-backdrop"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.4, ease: 'easeOut' }}
            onClick={closeCart}
            className="fixed inset-0 bg-black/75 backdrop-blur-sm z-50"
            aria-hidden="true"
          />

          {/* ── Drawer panel — slides from LEFT (RTL convention) ── */}
          <motion.div
            key="cart-panel"
            initial={{ x: '-100%' }}
            animate={{ x: 0 }}
            exit={{ x: '-100%' }}
            transition={{ type: 'spring', damping: 32, stiffness: 260, mass: 0.9 }}
            className="fixed left-0 top-0 bottom-0 w-full max-w-md xs:max-w-full
                       bg-[#0d0d10] border-e border-[rgba(201,165,90,0.1)]
                       z-50 flex flex-col shadow-luxury pb-6 sm:pb-0"
            role="dialog"
            aria-modal="true"
            aria-label="עגלת קניות"
          >
            {/* Top accent */}
            <div className="h-px bg-gradient-to-l from-transparent via-[rgba(201,165,90,0.3)] to-transparent shrink-0" />

            {/* Ambient glow in top-right area */}
            <div
              className="absolute top-0 right-0 w-56 h-56 pointer-events-none"
              style={{
                background: 'radial-gradient(ellipse at top right, rgba(201,165,90,0.06) 0%, transparent 65%)',
              }}
              aria-hidden="true"
            />

            {/* Header */}
            <DrawerHeader onClose={closeCart} itemCount={totalItems} />

            {/* ── Items / Empty state ── */}
            <div className="flex-1 overflow-y-auto px-6 py-2 relative">
              {state.items.length === 0 ? (
                <EmptyCart onClose={closeCart} />
              ) : (
                <AnimatePresence mode="popLayout" initial={false}>
                  {state.items.map(item => (
                    <CartItemRow
                      key={item.product.id}
                      item={item}
                      onUpdateQuantity={updateQuantity}
                      onRemove={removeItem}
                    />
                  ))}
                </AnimatePresence>
              )}
            </div>

            {/* ── Footer (only when cart has items) ── */}
            <AnimatePresence>
              {state.items.length > 0 && (
                <motion.div
                  initial={{ opacity: 0, y: 16 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: 16 }}
                  transition={{ duration: 0.3, ease: [0.16, 1, 0.3, 1] }}
                  className="border-t border-[rgba(201,165,90,0.08)] px-6 pt-4 pb-7 space-y-3 shrink-0"
                >
                  {/* Free shipping progress */}
                  <ShippingBanner totalPrice={totalPrice} />

                  {/* Subtotal row */}
                  <div className="flex items-center justify-between py-2">
                    <span className="text-[rgba(242,237,223,0.45)] text-sm uppercase tracking-wider font-medium">
                      סה״כ ביניים
                    </span>
                    <AnimatePresence mode="wait">
                      <motion.span
                        key={totalPrice}
                        initial={{ opacity: 0, y: -8 }}
                        animate={{ opacity: 1, y: 0 }}
                        exit={{ opacity: 0, y: 8 }}
                        transition={{ duration: 0.2 }}
                        className="text-[#f2eddf] font-bold text-xl ltr tabular-nums"
                      >
                        ₪{totalPrice.toLocaleString('he-IL')}
                      </motion.span>
                    </AnimatePresence>
                  </div>

                  {/* Fine print */}
                  <p className="text-[rgba(242,237,223,0.18)] text-[11px]">
                    מיסים ומשלוח יחושבו בקופה
                  </p>

                  {/* Checkout CTA — animated arrow */}
                  <Link
                    href="/cart"
                    onClick={closeCart}
                    className="btn-gold w-full py-[1.1rem] text-[11px] rounded-none group gap-3"
                  >
                    <span className="flex-1 text-center">לתשלום</span>
                    <motion.div
                      animate={{ x: [0, -4, 0] }}
                      transition={{ duration: 1.5, repeat: Infinity, ease: 'easeInOut' }}
                    >
                      <ArrowLeft className="w-4 h-4" aria-hidden="true" />
                    </motion.div>
                  </Link>

                  {/* Continue shopping ghost */}
                  <button
                    onClick={closeCart}
                    className="btn-ghost w-full py-2 text-[rgba(242,237,223,0.28)] hover:text-[rgba(242,237,223,0.6)]"
                  >
                    המשך קנייה
                  </button>

                  {/* Trust badges */}
                  <div className="flex items-center justify-center gap-4 pt-1 border-t border-[rgba(201,165,90,0.06)]">
                    {[
                      { icon: ShieldCheck, label: 'תשלום מאובטח' },
                      { icon: Truck, label: 'משלוח מהיר בישראל' },
                      { icon: MessageCircle, label: 'תמיכה בוואטסאפ' },
                    ].map(({ icon: Icon, label }) => (
                      <div key={label} className="flex flex-col items-center gap-1 text-center">
                        <Icon className="w-3.5 h-3.5 text-[rgba(201,165,90,0.45)]" />
                        <span className="text-[rgba(242,237,223,0.2)] text-[9px] leading-tight max-w-[56px]">{label}</span>
                      </div>
                    ))}
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  )
}
