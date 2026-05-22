'use client'

import { useState } from 'react'
import Link from 'next/link'
import { motion } from 'framer-motion'
import { Heart, ShoppingCart, Star, Eye } from 'lucide-react'
import type { Product } from '@/lib/types'
import { useCart } from '@/context/CartContext'
import { useWishlist } from '@/context/WishlistContext'
import ProductImagePlaceholder from './ProductImagePlaceholder'
import { playerLevelLabels } from '@/lib/data'

type Props = {
  product: Product
  index?: number
}

export default function ProductCard({ product, index = 0 }: Props) {
  const { addItem } = useCart()
  const { isWishlisted, toggle } = useWishlist()
  const [addedToCart, setAddedToCart] = useState(false)
  const wishlisted = isWishlisted(product.id)

  const handleAddToCart = (e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    addItem(product)
    setAddedToCart(true)
    setTimeout(() => setAddedToCart(false), 1500)
  }

  const handleWishlist = (e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    toggle(product)
  }

  const discount = product.salePrice
    ? Math.round(((product.price - product.salePrice) / product.price) * 100)
    : null

  const levels = ['beginner', 'intermediate', 'advanced', 'professional'] as const

  return (
    <motion.div
      initial={{ opacity: 0, y: 30 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, margin: '-50px' }}
      transition={{ duration: 0.6, delay: index * 0.07, ease: [0.16, 1, 0.3, 1] }}
    >
      <Link href={`/product/${product.slug}`} className="product-card block">
        {/* Image Container */}
        <div className="relative aspect-square overflow-hidden">
          <ProductImagePlaceholder
            product={product}
            className="w-full h-full transition-transform duration-700 group-hover:scale-105"
          />

          {/* Badges */}
          <div className="absolute top-3 right-3 flex flex-col gap-1.5">
            {product.salePrice && (
              <span className="px-2 py-0.5 bg-[#b5f72e] text-black text-[10px] font-bold uppercase tracking-widest ltr-text">
                -{discount}%
              </span>
            )}
            {product.isNew && (
              <span className="px-2 py-0.5 bg-white text-black text-[10px] font-bold uppercase tracking-widest">
                חדש
              </span>
            )}
            {product.isBestSeller && !product.isNew && (
              <span className="px-2 py-0.5 bg-white/10 border border-white/20 text-white text-[10px] font-bold uppercase tracking-widest backdrop-blur-sm">
                פופולרי
              </span>
            )}
          </div>

          {/* Wishlist Button */}
          <motion.button
            onClick={handleWishlist}
            className={`absolute top-3 left-3 w-8 h-8 flex items-center justify-center backdrop-blur-sm border transition-all duration-300 ${
              wishlisted
                ? 'bg-[#b5f72e]/10 border-[#b5f72e]/40 text-[#b5f72e]'
                : 'bg-black/40 border-white/10 text-white/50 opacity-0 group-hover:opacity-100'
            }`}
            whileTap={{ scale: 0.85 }}
          >
            <Heart className={`w-4 h-4 ${wishlisted ? 'fill-[#b5f72e]' : ''}`} />
          </motion.button>

          {/* Quick Action Bar */}
          <div className="absolute bottom-0 left-0 right-0 translate-y-full group-hover:translate-y-0 transition-transform duration-300 ease-out">
            <div className="flex">
              <button
                onClick={handleAddToCart}
                className={`flex-1 py-3 text-xs font-bold uppercase tracking-widest transition-all duration-300 flex items-center justify-center gap-2 ${
                  addedToCart
                    ? 'bg-[#b5f72e] text-black'
                    : 'bg-white/95 text-black hover:bg-[#b5f72e]'
                }`}
              >
                <ShoppingCart className="w-3.5 h-3.5" />
                {addedToCart ? 'נוסף!' : 'הוסף לעגלה'}
              </button>
              <Link
                href={`/product/${product.slug}`}
                className="w-12 bg-black/80 border-r border-white/10 flex items-center justify-center text-white/60 hover:text-white hover:bg-black transition-colors"
              >
                <Eye className="w-4 h-4" />
              </Link>
            </div>
          </div>
        </div>

        {/* Product Info */}
        <div className="p-4">
          <p className="text-white/30 text-xs uppercase tracking-widest mb-1">{product.brand}</p>

          <h3 className="text-white text-sm font-medium leading-tight line-clamp-2 group-hover:text-[#b5f72e] transition-colors duration-300 mb-2">
            {product.name}
          </h3>

          <div className="flex items-center gap-1.5 mb-3">
            <div className="flex items-center gap-0.5">
              {[...Array(5)].map((_, i) => (
                <Star
                  key={i}
                  className={`w-3 h-3 ${i < Math.floor(product.rating) ? 'text-[#b5f72e] fill-[#b5f72e]' : 'text-white/15'}`}
                />
              ))}
            </div>
            <span className="text-white/30 text-xs ltr-text">({product.reviewCount})</span>
          </div>

          {/* Price */}
          <div className="flex items-center gap-2">
            {product.salePrice ? (
              <>
                <span className="text-[#b5f72e] font-bold text-lg ltr-text">₪{product.salePrice.toLocaleString()}</span>
                <span className="text-white/25 text-sm line-through ltr-text">₪{product.price.toLocaleString()}</span>
              </>
            ) : (
              <span className="text-white font-bold text-lg ltr-text">₪{product.price.toLocaleString()}</span>
            )}
          </div>

          {/* Player level indicator */}
          {product.playerLevel && (
            <div className="mt-2 flex items-center gap-1">
              {levels.map((level, i) => {
                const currentIdx = levels.indexOf(product.playerLevel as typeof levels[number])
                return (
                  <div
                    key={level}
                    className={`h-0.5 flex-1 transition-colors ${i <= currentIdx ? 'bg-[#b5f72e]' : 'bg-white/10'}`}
                  />
                )
              })}
              <span className="text-white/30 text-[9px] me-1 uppercase tracking-wider">
                {playerLevelLabels[product.playerLevel as keyof typeof playerLevelLabels] || product.playerLevel}
              </span>
            </div>
          )}
        </div>
      </Link>
    </motion.div>
  )
}
