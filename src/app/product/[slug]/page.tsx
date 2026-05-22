'use client'

import { notFound } from 'next/navigation'
import { useState } from 'react'
import { motion } from 'framer-motion'
import { ShoppingCart, Heart, Star, Shield, Truck, RotateCcw, ChevronLeft, Minus, Plus, Check } from 'lucide-react'
import Link from 'next/link'
import { products, playerLevelLabels } from '@/lib/data'
import { useCart } from '@/context/CartContext'
import { useWishlist } from '@/context/WishlistContext'
import ProductCard from '@/components/shop/ProductCard'
import ProductImagePlaceholder from '@/components/shop/ProductImagePlaceholder'

const categoryLabels: Record<string, string> = {
  rackets: 'מחבטים',
  balls: 'כדורים',
  bags: 'תיקים',
  shoes: 'נעליים',
  grips: 'גריפים',
  apparel: 'ביגוד',
  accessories: 'אביזרים',
}

export default function ProductPage({ params }: { params: { slug: string } }) {
  const product = products.find(p => p.slug === params.slug)
  if (!product) notFound()

  const { addItem } = useCart()
  const { isWishlisted, toggle } = useWishlist()
  const [quantity, setQuantity] = useState(1)
  const [addedToCart, setAddedToCart] = useState(false)
  const wishlisted = isWishlisted(product.id)

  const handleAddToCart = () => {
    addItem(product, quantity)
    setAddedToCart(true)
    setTimeout(() => setAddedToCart(false), 2000)
  }

  const recommended = products.filter(p => p.id !== product.id && (p.category === product.category || p.brand === product.brand)).slice(0, 4)

  const discount = product.salePrice
    ? Math.round(((product.price - product.salePrice) / product.price) * 100)
    : null

  const levels = ['beginner', 'intermediate', 'advanced', 'professional'] as const

  return (
    <div className="min-h-screen pt-20">
      {/* Breadcrumb */}
      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 py-4">
        <nav className="flex items-center gap-2 text-[rgba(242,237,223,0.3)] text-xs uppercase tracking-widest">
          <Link href="/" className="hover:text-[#f2eddf] transition-colors">בית</Link>
          <ChevronLeft className="w-3 h-3" />
          <Link href="/shop" className="hover:text-[#f2eddf] transition-colors">חנות</Link>
          <ChevronLeft className="w-3 h-3" />
          <Link href={`/shop?category=${product.category}`} className="hover:text-[#f2eddf] transition-colors">
            {categoryLabels[product.category] || product.category}
          </Link>
          <ChevronLeft className="w-3 h-3" />
          <span className="text-[rgba(242,237,223,0.6)] truncate max-w-xs">{product.name}</span>
        </nav>
      </div>

      {/* Product Main */}
      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 py-8">
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-12 xl:gap-20">
          {/* Right: Image Gallery (first in RTL) */}
          <motion.div
            initial={{ opacity: 0, x: 40 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
          >
            <div className="relative aspect-square border border-[rgba(201,165,90,0.08)] overflow-hidden group">
              <ProductImagePlaceholder product={product} className="w-full h-full" />

              {/* Badges */}
              <div className="absolute top-4 right-4 flex gap-2">
                {product.salePrice && (
                  <span className="badge-sale ltr">
                    -{discount}%
                  </span>
                )}
                {product.isNew && (
                  <span className="badge-new">
                    חדש
                  </span>
                )}
              </div>

              {/* Wishlist */}
              <button
                onClick={() => toggle(product)}
                className={`absolute top-4 left-4 w-10 h-10 border flex items-center justify-center transition-all duration-300 ${
                  wishlisted
                    ? 'border-[rgba(201,165,90,0.5)] bg-[rgba(201,165,90,0.1)] text-[#c9a55a]'
                    : 'border-[rgba(255,255,255,0.1)] bg-black/40 text-[rgba(242,237,223,0.5)] hover:text-[#f2eddf] hover:border-[rgba(255,255,255,0.3)]'
                }`}
              >
                <Heart className={`w-5 h-5 ${wishlisted ? 'fill-[#c9a55a]' : ''}`} />
              </button>
            </div>

            {/* Thumbnails */}
            <div className="grid grid-cols-4 gap-2 mt-2">
              {[...Array(4)].map((_, i) => (
                <div
                  key={i}
                  className={`aspect-square border cursor-pointer transition-all duration-200 overflow-hidden ${
                    i === 0 ? 'border-[rgba(201,165,90,0.5)]' : 'border-[rgba(201,165,90,0.08)] hover:border-[rgba(201,165,90,0.2)]'
                  }`}
                >
                  <ProductImagePlaceholder product={product} className="w-full h-full opacity-60 hover:opacity-100 transition-opacity" />
                </div>
              ))}
            </div>
          </motion.div>

          {/* Left: Product Details */}
          <motion.div
            initial={{ opacity: 0, x: -40 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1], delay: 0.1 }}
            className="flex flex-col"
          >
            {/* Brand */}
            <p className="text-[#c9a55a] text-xs font-bold uppercase tracking-[0.3em] mb-2">{product.brand}</p>

            {/* Name */}
            <h1 className="font-display text-4xl md:text-5xl text-[#f2eddf] tracking-wide uppercase leading-none mb-4">
              {product.name}
            </h1>

            {/* Rating */}
            <div className="flex items-center gap-3 mb-6">
              <div className="flex items-center gap-1">
                {[...Array(5)].map((_, i) => (
                  <Star key={i} className={`w-4 h-4 ${i < Math.floor(product.rating) ? 'text-[#c9a55a] fill-[#c9a55a]' : 'text-[rgba(242,237,223,0.15)]'}`} />
                ))}
              </div>
              <span className="text-[#f2eddf] font-bold ltr">{product.rating}</span>
              <span className="text-[rgba(242,237,223,0.3)] text-sm ltr">({product.reviewCount} ביקורות)</span>
            </div>

            {/* Price */}
            <div className="flex items-baseline gap-3 mb-6 pb-6 border-b border-[rgba(201,165,90,0.08)]">
              {product.salePrice ? (
                <>
                  <span className="text-4xl font-bold text-[#c9a55a] ltr">₪{product.salePrice.toLocaleString()}</span>
                  <span className="text-2xl text-[rgba(242,237,223,0.25)] line-through ltr">₪{product.price.toLocaleString()}</span>
                  <span className="px-2 py-1 bg-[rgba(201,165,90,0.1)] border border-[rgba(201,165,90,0.3)] text-[#c9a55a] text-xs font-bold">
                    חסוך {discount}%
                  </span>
                </>
              ) : (
                <span className="text-4xl font-bold text-[#f2eddf] ltr">₪{product.price.toLocaleString()}</span>
              )}
            </div>

            {/* Description */}
            <p className="text-[rgba(242,237,223,0.5)] text-sm leading-relaxed mb-6">{product.description}</p>

            {/* Specs Grid */}
            {(product.weight || product.balance || product.shape || product.playerLevel || product.material) && (
              <div className="grid grid-cols-2 gap-2 mb-6 p-4 bg-[rgba(255,255,255,0.02)] border border-[rgba(201,165,90,0.08)]">
                {product.playerLevel && (
                  <div>
                    <p className="text-[rgba(242,237,223,0.3)] text-xs uppercase tracking-widest mb-0.5">רמה</p>
                    <p className="text-[#f2eddf] text-sm font-medium">
                      {playerLevelLabels[product.playerLevel as keyof typeof playerLevelLabels] || product.playerLevel}
                    </p>
                  </div>
                )}
                {product.weight && (
                  <div>
                    <p className="text-[rgba(242,237,223,0.3)] text-xs uppercase tracking-widest mb-0.5">משקל</p>
                    <p className="text-[#f2eddf] text-sm font-medium ltr">{product.weight}</p>
                  </div>
                )}
                {product.balance && (
                  <div>
                    <p className="text-[rgba(242,237,223,0.3)] text-xs uppercase tracking-widest mb-0.5">איזון</p>
                    <p className="text-[#f2eddf] text-sm font-medium">{product.balance}</p>
                  </div>
                )}
                {product.shape && (
                  <div>
                    <p className="text-[rgba(242,237,223,0.3)] text-xs uppercase tracking-widest mb-0.5">צורה</p>
                    <p className="text-[#f2eddf] text-sm font-medium">{product.shape}</p>
                  </div>
                )}
                {product.material && (
                  <div className="col-span-2">
                    <p className="text-[rgba(242,237,223,0.3)] text-xs uppercase tracking-widest mb-0.5">חומר</p>
                    <p className="text-[#f2eddf] text-sm font-medium">{product.material}</p>
                  </div>
                )}
              </div>
            )}

            {/* Player Level Bar */}
            {product.playerLevel && (
              <div className="mb-6">
                <p className="text-[rgba(242,237,223,0.3)] text-xs uppercase tracking-widest mb-2">רמת שחקן</p>
                <div className="flex items-center gap-1">
                  {levels.map((level, i) => {
                    const currentIdx = levels.indexOf(product.playerLevel as typeof levels[number])
                    return (
                      <div key={level} className="flex-1">
                        <div className={`h-1 ${i <= currentIdx ? 'bg-[#c9a55a]' : 'bg-[rgba(255,255,255,0.1)]'}`} />
                        <p className={`text-[9px] mt-1 text-center ${i === currentIdx ? 'text-[#c9a55a]' : 'text-[rgba(242,237,223,0.2)]'}`}>
                          {i === currentIdx && (playerLevelLabels[level] || level)}
                        </p>
                      </div>
                    )
                  })}
                </div>
              </div>
            )}

            {/* Quantity & Add to Cart */}
            <div className="flex items-center gap-3 mb-4">
              <div className="flex items-center border border-[rgba(201,165,90,0.1)]">
                <button
                  onClick={() => setQuantity(q => Math.max(1, q - 1))}
                  className="w-10 h-12 flex items-center justify-center text-[rgba(242,237,223,0.5)] hover:text-[#f2eddf] hover:bg-[rgba(255,255,255,0.05)] transition-all"
                >
                  <Minus className="w-4 h-4" />
                </button>
                <span className="w-10 h-12 flex items-center justify-center text-[#f2eddf] font-bold">
                  {quantity}
                </span>
                <button
                  onClick={() => setQuantity(q => q + 1)}
                  className="w-10 h-12 flex items-center justify-center text-[rgba(242,237,223,0.5)] hover:text-[#f2eddf] hover:bg-[rgba(255,255,255,0.05)] transition-all"
                >
                  <Plus className="w-4 h-4" />
                </button>
              </div>

              <button
                onClick={handleAddToCart}
                className={`flex-1 flex items-center justify-center gap-2 py-3.5 font-bold text-sm uppercase tracking-widest transition-all duration-300 ${
                  addedToCart
                    ? 'bg-white text-black'
                    : 'btn-gold'
                }`}
              >
                {addedToCart ? (
                  <>
                    <Check className="w-4 h-4" />
                    נוסף לעגלה!
                  </>
                ) : (
                  <>
                    <ShoppingCart className="w-4 h-4" />
                    הוסף לעגלה
                  </>
                )}
              </button>
            </div>

            {/* Wishlist Button */}
            <button
              onClick={() => toggle(product)}
              className={`btn-outline w-full mb-8 ${
                wishlisted
                  ? 'border-[rgba(201,165,90,0.4)] text-[#c9a55a]'
                  : ''
              }`}
            >
              <Heart className={`w-4 h-4 ${wishlisted ? 'fill-[#c9a55a]' : ''}`} />
              {wishlisted ? 'ברשימת המשאלות' : 'הוסף לרשימת משאלות'}
            </button>

            {/* Features */}
            <div className="mb-8">
              <p className="text-[rgba(242,237,223,0.3)] text-xs uppercase tracking-widest mb-3">תכונות</p>
              <ul className="space-y-2">
                {product.features.map((f, i) => (
                  <li key={i} className="flex items-start gap-2 text-sm text-[rgba(242,237,223,0.5)]">
                    <span className="w-4 h-4 mt-0.5 bg-[rgba(201,165,90,0.1)] border border-[rgba(201,165,90,0.3)] flex items-center justify-center flex-shrink-0">
                      <Check className="w-2.5 h-2.5 text-[#c9a55a]" />
                    </span>
                    {f}
                  </li>
                ))}
              </ul>
            </div>

            {/* Shipping, Returns, Security */}
            <div className="space-y-3 border-t border-[rgba(201,165,90,0.08)] pt-6">
              {[
                { icon: Truck, text: 'משלוח חינם על הזמנות מעל ₪280. משלוח מהיר זמין.' },
                { icon: RotateCcw, text: 'החזרה ללא טרחה עד 30 יום. ללא שאלות.' },
                { icon: Shield, text: 'מקורי 100%. ספק מורשה רשמי.' },
              ].map(({ icon: Icon, text }) => (
                <div key={text} className="flex items-start gap-3 text-[rgba(242,237,223,0.4)] text-xs">
                  <Icon className="w-4 h-4 text-[#c9a55a] flex-shrink-0 mt-0.5" />
                  {text}
                </div>
              ))}
            </div>
          </motion.div>
        </div>

        {/* Long Description */}
        {product.longDescription && (
          <div className="mt-16 pt-16 border-t border-[rgba(201,165,90,0.08)]">
            <h2 className="font-display text-3xl text-[#f2eddf] tracking-wide uppercase mb-6">אודות המוצר</h2>
            <p className="text-[rgba(242,237,223,0.5)] text-base leading-relaxed max-w-3xl">{product.longDescription}</p>
          </div>
        )}
      </div>

      {/* Recommended Products */}
      {recommended.length > 0 && (
        <section className="py-16 bg-[#0d0d10] mt-12">
          <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
            <h2 className="font-display text-3xl text-[#f2eddf] tracking-wide uppercase mb-8">
              אולי תאהב גם <span className="text-gold-gradient">את אלה</span>
            </h2>
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              {recommended.map((p, i) => (
                <ProductCard key={p.id} product={p} index={i} />
              ))}
            </div>
          </div>
        </section>
      )}
    </div>
  )
}
