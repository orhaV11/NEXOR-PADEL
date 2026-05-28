'use client'

import { useMotionValue, useTransform, motion, useSpring, AnimatePresence } from 'framer-motion'
import { useState, useEffect } from 'react'
import Link from 'next/link'
import { Heart, ShoppingCart, Eye, Star, Check } from 'lucide-react'
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
    <div className="mt-3">
      <div className="flex gap-[4px] mb-1.5">
        {playerLevelOrder.map((_, i) => (
          <motion.div
            key={i}
            className="h-[4px] flex-1 rounded-full"
            initial={{ scaleX: 0, opacity: 0 }}
            animate={{ scaleX: 1, opacity: 1 }}
            transition={{ duration: 0.4, ease: LUXURY_EASE, delay: i * 0.07 }}
            style={{
              background:
                i <= idx
                  ? 'linear-gradient(90deg, #b08840, #e2c890)'
                  : 'rgba(242,237,223,0.08)',
              transformOrigin: 'left',
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
  const [wishlistClicked, setWishlistClicked] = useState(false)
  const [hovered, setHovered] = useState(false)
  const [isMobile, setIsMobile] = useState(false)

  useEffect(() => {
    setIsMobile(window.innerWidth < 768)
    const handleResize = () => setIsMobile(window.innerWidth < 768)
    window.addEventListener('resize', handleResize)
    return () => window.removeEventListener('resize', handleResize)
  }, [])

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
    setWishlistClicked(true)
    setTimeout(() => setWishlistClicked(false), 400)
    toggle(product)
  }

  const wishlisted = isWishlisted(product.id)
  const discount = product.salePrice
    ? Math.round(((product.price - product.salePrice) / product.price) * 100)
    : 0

  const lowStock = product.stockCount != null && product.stockCount <= 5 && product.stockCount > 0

  return (
    <motion.div
      initial={{ opacity: 0, y: 40 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, margin: '-80px' }}
      transition={{ duration: 0.65, ease: LUXURY_EASE, delay: index * 0.07 }}
      style={
        isMobile
          ? {}
          : { rotateX, rotateY, transformPerspective: 1200 }
      }
      className="product-card card-gold-top group relative cursor-pointer"
      onMouseMove={isMobile ? undefined : handleMouseMove}
      onMouseEnter={isMobile ? undefined : () => setHovered(true)}
      onMouseLeave={isMobile ? undefined : handleMouseLeave}
    >
      <Link href={`/product/${product.slug}`} className="block">
        {/* Image area — taller 4:5 aspect */}
        <div className="relative aspect-[4/5] overflow-hidden">
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
          <div className="absolute top-3 right-3 flex flex-col gap-1.5 items-end">
            {discount > 0 && <span className="badge-sale">–{discount}%</span>}
            {product.isNew && <span className="badge-new">חדש</span>}
            {product.isBestSeller && <span className="badge-gold">בסט-סלר</span>}
          </div>

          {/* Wishlist button — top-left in RTL */}
          <motion.button
            onClick={handleWishlist}
            aria-label={wishlisted ? 'הסר מרשימת משאלות' : 'הוסף לרשימת משאלות'}
            animate={
              wishlistClicked
                ? { scale: [1, 1.35, 1] }
                : { scale: 1 }
            }
            transition={{ duration: 0.35, ease: LUXURY_EASE }}
            className="absolute top-3 left-3 w-8 h-8 flex items-center justify-center rounded-full transition-all duration-300"
            style={{
              opacity: hovered || wishlisted ? 1 : 0,
              background: wishlisted ? 'rgba(201,165,90,0.2)' : 'rgba(8,8,10,0.72)',
              color: wishlisted ? '#c9a55a' : 'rgba(242,237,223,0.45)',
              backdropFilter: 'blur(4px)',
            }}
          >
            <Heart size={13} fill={wishlisted ? 'currentColor' : 'none'} />
          </motion.button>

          {/* Low stock badge */}
          {lowStock && (
            <div className="absolute bottom-14 inset-x-0 flex justify-center pointer-events-none">
              <span
                className="text-[10px] font-semibold tracking-wider px-2.5 py-1 rounded-full"
                style={{
                  background: 'rgba(8,8,10,0.82)',
                  color: product.stockCount === 1 ? '#ef4444' : '#f59e0b',
                  backdropFilter: 'blur(6px)',
                  border: `1px solid ${product.stockCount === 1 ? 'rgba(239,68,68,0.3)' : 'rgba(245,158,11,0.3)'}`,
                }}
              >
                נותרו {product.stockCount} בלבד
              </span>
            </div>
          )}

          {/* Quick-add overlay — slides up on hover with glass effect */}
          <AnimatePresence>
            {hovered && (
              <motion.div
                initial={{ y: '100%', opacity: 0 }}
                animate={{ y: 0, opacity: 1 }}
                exit={{ y: '100%', opacity: 0 }}
                transition={{ duration: 0.28, ease: LUXURY_EASE }}
                className="absolute bottom-0 inset-x-0 backdrop-blur-sm flex items-stretch"
              >
                <motion.button
                  onClick={handleAddToCart}
                  animate={added ? { backgroundColor: '#c9a55a' } : { backgroundColor: '#ffffff' }}
                  transition={{ type: 'spring', stiffness: 400, damping: 25 }}
                  className="flex-1 flex items-center justify-center gap-2 py-3.5 text-[11px] font-bold uppercase tracking-widest transition-colors duration-300"
                  style={{ color: '#08080a' }}
                >
                  <AnimatePresence mode="wait">
                    {added ? (
                      <motion.span
                        key="check"
                        initial={{ scale: 0, rotate: -90 }}
                        animate={{ scale: 1, rotate: 0 }}
                        exit={{ scale: 0 }}
                        transition={{ type: 'spring', stiffness: 500, damping: 20 }}
                        className="flex items-center gap-2"
                      >
                        <Check size={13} />
                        נוסף!
                      </motion.span>
                    ) : (
                      <motion.span
                        key="cart"
                        initial={{ scale: 0.8, opacity: 0 }}
                        animate={{ scale: 1, opacity: 1 }}
                        exit={{ scale: 0.8, opacity: 0 }}
                        transition={{ duration: 0.15 }}
                        className="flex items-center gap-2"
                      >
                        <ShoppingCart size={13} />
                        הוסף לעגלה
                      </motion.span>
                    )}
                  </AnimatePresence>
                </motion.button>
                <Link
                  href={`/product/${product.slug}`}
                  onClick={e => e.stopPropagation()}
                  className="w-11 flex items-center justify-center transition-colors duration-200"
                  style={{
                    background: 'rgba(8,8,10,0.85)',
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

        {/* Info section */}
        <div className="p-4 pt-3.5">
          {/* Brand + separator */}
          <div className="flex items-center gap-2 mb-2">
            <p
              className="text-[10px] uppercase tracking-widest shrink-0"
              style={{ color: 'rgba(201,165,90,0.65)' }}
            >
              {product.brand}
            </p>
            <div className="flex-1 h-px" style={{ background: 'rgba(242,237,223,0.06)' }} />
          </div>

          {/* Product name */}
          <h3
            className="text-sm font-semibold line-clamp-3 leading-snug mb-3 transition-colors duration-200"
            style={{ color: hovered ? '#c9a55a' : '#f2eddf' }}
          >
            {product.name}
          </h3>

          {/* Rating — stars stagger in on hover */}
          <div className="flex items-center gap-1.5 mb-3">
            <div className="flex items-center gap-0.5">
              {[1, 2, 3, 4, 5].map((star, i) => (
                <motion.div
                  key={star}
                  animate={
                    hovered
                      ? { scale: [1, 1.25, 1], opacity: 1 }
                      : { scale: 1, opacity: 0.85 }
                  }
                  transition={
                    hovered
                      ? { duration: 0.3, delay: i * 0.05, ease: LUXURY_EASE }
                      : { duration: 0.2 }
                  }
                >
                  <Star
                    size={10}
                    style={{
                      color: star <= Math.round(product.rating) ? '#c9a55a' : 'rgba(201,165,90,0.18)',
                    }}
                    fill={star <= Math.round(product.rating) ? '#c9a55a' : 'none'}
                  />
                </motion.div>
              ))}
            </div>
            <span className="text-[10px]" style={{ color: 'rgba(242,237,223,0.35)' }}>
              ({product.reviewCount})
            </span>
          </div>

          {/* Price */}
          <motion.div
            className="flex items-center gap-2.5"
            animate={hovered ? { scale: 1.03 } : { scale: 1 }}
            transition={{ duration: 0.25, ease: LUXURY_EASE }}
            style={{ transformOrigin: 'right' }}
          >
            {product.salePrice ? (
              <>
                <span className="stat-number text-xl ltr">
                  ₪{product.salePrice.toLocaleString()}
                </span>
                <span className="ltr text-xs line-through" style={{ color: 'rgba(242,237,223,0.25)' }}>
                  ₪{product.price.toLocaleString()}
                </span>
              </>
            ) : (
              <span className="stat-number text-xl ltr">
                ₪{product.price.toLocaleString()}
              </span>
            )}
          </motion.div>

          {/* Player level bar */}
          {product.playerLevel && <PlayerLevelBar level={product.playerLevel} />}
        </div>
      </Link>
    </motion.div>
  )
}
