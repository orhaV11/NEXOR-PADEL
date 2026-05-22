'use client'

import { useMotionValue, useTransform, motion, useSpring, AnimatePresence } from 'framer-motion'
import { useState } from 'react'
import Link from 'next/link'
import { Heart, ShoppingCart, Eye, Star } from 'lucide-react'
import { useCart } from '@/context/CartContext'
import { useWishlist } from '@/context/WishlistContext'
import ProductImagePlaceholder from '@/components/shop/ProductImagePlaceholder'
import { playerLevelLabels } from '@/lib/data'
import type { Product } from '@/lib/types'

const LUXURY_EASE = [0.16, 1, 0.3, 1] as const

const playerLevelOrder = ['beginner', 'intermediate', 'advanced', 'professional']

function PlayerLevelBar({ level }: { level: string }) {
  const idx = playerLevelOrder.indexOf(level)
  return (
    <div className="mt-2.5">
      <div className="flex gap-[3px] mb-1">
        {playerLevelOrder.map((_, i) => (
          <div
            key={i}
            className="h-[3px] flex-1 rounded-full"
            style={{
              background:
                i <= idx
                  ? 'linear-gradient(90deg, #b08840, #e2c890)'
                  : 'rgba(242,237,223,0.08)',
              transition: 'background 0.3s ease',
            }}
          />
        ))}
      </div>
      <span className="text-[10px] tracking-wider" style={{ color: 'rgba(201,165,90,0.5)' }}>
        {playerLevelLabels[level] ?? level}
      </span>
    </div>
  )
}

export default function ProductCard({ product, index = 0 }: { product: Product; index?: number }) {
  const { addItem } = useCart()
  const { toggle, isWishlisted } = useWishlist()
  const [added, setAdded] = useState(false)
  const [hovered, setHovered] = useState(false)

  const mouseX = useMotionValue(0)
  const mouseY = useMotionValue(0)

  const rawRotateX = useTransform(mouseY, [-0.5, 0.5], [6, -6])
  const rawRotateY = useTransform(mouseX, [-0.5, 0.5], [-6, 6])
  const rotateX = useSpring(rawRotateX, { stiffness: 300, damping: 30 })
  const rotateY = useSpring(rawRotateY, { stiffness: 300, damping: 30 })

  function handleMouseMove(e: React.MouseEvent<HTMLDivElement>) {
    const rect = e.currentTarget.getBoundingClientRect()
    mouseX.set((e.clientX - rect.left) / rect.width - 0.5)
    mouseY.set((e.clientY - rect.top) / rect.height - 0.5)
  }

  function handleMouseLeave() {
    mouseX.set(0)
    mouseY.set(0)
    setHovered(false)
  }

  function handleAddToCart(e: React.MouseEvent) {
    e.preventDefault()
    e.stopPropagation()
    addItem(product)
    setAdded(true)
    setTimeout(() => setAdded(false), 1800)
  }

  function handleWishlist(e: React.MouseEvent) {
    e.preventDefault()
    e.stopPropagation()
    toggle(product)
  }

  const wishlisted = isWishlisted(product.id)
  const discount = product.salePrice
    ? Math.round(((product.price - product.salePrice) / product.price) * 100)
    : 0

  return (
    <motion.div
      initial={{ opacity: 0, y: 40 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, margin: '-80px' }}
      transition={{ duration: 0.65, ease: LUXURY_EASE, delay: index * 0.07 }}
      style={{ rotateX, rotateY, transformPerspective: 1200 }}
      className="product-card group relative"
      onMouseMove={handleMouseMove}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={handleMouseLeave}
    >
      <Link href={`/product/${product.slug}`} className="block">
        {/* Image area */}
        <div className="relative aspect-square overflow-hidden">
          <div
            className="w-full h-full"
            style={{
              transition: 'transform 0.6s cubic-bezier(0.16,1,0.3,1)',
              transform: hovered ? 'scale(1.04)' : 'scale(1)',
            }}
          >
            <ProductImagePlaceholder product={product} className="w-full h-full" />
          </div>

          {/* Badges — top-right in RTL */}
          <div className="absolute top-2.5 right-2.5 flex flex-col gap-1.5 items-end">
            {discount > 0 && <span className="badge-sale">–{discount}%</span>}
            {product.isNew && <span className="badge-new">חדש</span>}
            {product.isBestSeller && <span className="badge-gold">בסט-סלר</span>}
          </div>

          {/* Wishlist — top-left in RTL */}
          <button
            onClick={handleWishlist}
            aria-label={wishlisted ? 'הסר מרשימת משאלות' : 'הוסף לרשימת משאלות'}
            className="absolute top-2.5 left-2.5 w-8 h-8 flex items-center justify-center rounded-full transition-all duration-300"
            style={{
              opacity: hovered || wishlisted ? 1 : 0,
              background: wishlisted ? 'rgba(201,165,90,0.18)' : 'rgba(8,8,10,0.72)',
              color: wishlisted ? '#c9a55a' : 'rgba(242,237,223,0.45)',
            }}
          >
            <Heart size={13} fill={wishlisted ? 'currentColor' : 'none'} />
          </button>

          {/* Quick-add overlay — slides up on hover */}
          <AnimatePresence>
            {hovered && (
              <motion.div
                initial={{ y: '100%', opacity: 0 }}
                animate={{ y: 0, opacity: 1 }}
                exit={{ y: '100%', opacity: 0 }}
                transition={{ duration: 0.28, ease: LUXURY_EASE }}
                className="absolute bottom-0 inset-x-0 flex items-stretch"
              >
                <button
                  onClick={handleAddToCart}
                  className="flex-1 flex items-center justify-center gap-2 py-3 text-[11px] font-bold uppercase tracking-widest transition-all duration-300"
                  style={{
                    background: added ? '#c9a55a' : 'white',
                    color: '#08080a',
                  }}
                >
                  <ShoppingCart size={13} />
                  {added ? 'נוסף!' : 'הוסף לעגלה'}
                </button>
                <Link
                  href={`/product/${product.slug}`}
                  onClick={e => e.stopPropagation()}
                  className="w-11 flex items-center justify-center transition-colors duration-200"
                  style={{
                    background: '#08080a',
                    borderRight: '1px solid rgba(242,237,223,0.08)',
                    color: 'rgba(242,237,223,0.4)',
                  }}
                  onMouseEnter={e => (e.currentTarget.style.color = '#c9a55a')}
                  onMouseLeave={e => (e.currentTarget.style.color = 'rgba(242,237,223,0.4)')}
                >
                  <Eye size={14} />
                </Link>
              </motion.div>
            )}
          </AnimatePresence>
        </div>

        {/* Info */}
        <div className="p-3.5">
          <p
            className="text-[10px] uppercase tracking-widest mb-1"
            style={{ color: 'rgba(201,165,90,0.65)' }}
          >
            {product.brand}
          </p>

          <h3
            className="text-sm font-semibold line-clamp-2 leading-snug mb-2 transition-colors duration-200"
            style={{ color: hovered ? '#c9a55a' : '#f2eddf' }}
          >
            {product.name}
          </h3>

          {/* Rating */}
          <div className="flex items-center gap-1.5 mb-2.5">
            <div className="flex items-center gap-0.5">
              {[1, 2, 3, 4, 5].map(star => (
                <Star
                  key={star}
                  size={10}
                  style={{ color: star <= Math.round(product.rating) ? '#c9a55a' : 'rgba(201,165,90,0.18)' }}
                  fill={star <= Math.round(product.rating) ? '#c9a55a' : 'none'}
                />
              ))}
            </div>
            <span className="text-[10px]" style={{ color: 'rgba(242,237,223,0.35)' }}>
              ({product.reviewCount})
            </span>
          </div>

          {/* Price */}
          <div className="flex items-center gap-2">
            {product.salePrice ? (
              <>
                <span className="ltr font-bold text-base" style={{ color: '#c9a55a' }}>
                  ₪{product.salePrice.toLocaleString()}
                </span>
                <span className="ltr text-xs line-through" style={{ color: 'rgba(242,237,223,0.25)' }}>
                  ₪{product.price.toLocaleString()}
                </span>
              </>
            ) : (
              <span className="ltr font-bold text-base" style={{ color: '#f2eddf' }}>
                ₪{product.price.toLocaleString()}
              </span>
            )}
          </div>

          {/* Player level bar */}
          {product.playerLevel && <PlayerLevelBar level={product.playerLevel} />}
        </div>
      </Link>
    </motion.div>
  )
}
