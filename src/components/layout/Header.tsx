'use client'

import { useState, useEffect } from 'react'
import Link from 'next/link'
import { usePathname } from 'next/navigation'
import { motion, AnimatePresence } from 'framer-motion'
import { ShoppingCart, Heart, Menu, X, Zap } from 'lucide-react'
import { useCart } from '@/context/CartContext'
import { useWishlist } from '@/context/WishlistContext'

const navLinks = [
  { label: 'חנות', href: '/shop' },
  { label: 'מחבטים', href: '/shop?category=rackets' },
  { label: 'נעליים', href: '/shop?category=shoes' },
  { label: 'תיקים', href: '/shop?category=bags' },
  { label: 'אודות', href: '/about' },
]

export default function Header() {
  const [scrolled, setScrolled] = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)
  const pathname = usePathname()
  const { totalItems, openCart } = useCart()
  const { count: wishlistCount } = useWishlist()

  useEffect(() => {
    const handleScroll = () => setScrolled(window.scrollY > 30)
    window.addEventListener('scroll', handleScroll, { passive: true })
    return () => window.removeEventListener('scroll', handleScroll)
  }, [])

  useEffect(() => { setMobileOpen(false) }, [pathname])

  useEffect(() => {
    document.body.style.overflow = mobileOpen ? 'hidden' : ''
    return () => { document.body.style.overflow = '' }
  }, [mobileOpen])

  return (
    <>
      <motion.header
        initial={{ y: -80, opacity: 0 }}
        animate={{ y: 0, opacity: 1 }}
        transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
        className={`fixed top-0 left-0 right-0 z-50 transition-all duration-500 ${
          scrolled
            ? 'bg-[#020202]/95 backdrop-blur-xl border-b border-white/[0.05]'
            : 'bg-transparent'
        }`}
      >
        {/* Top accent line */}
        <div className="h-px bg-gradient-to-l from-transparent via-[#b5f72e]/30 to-transparent" />

        <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
          <div className="flex items-center justify-between h-16 lg:h-20">

            {/* Actions (left side in RTL) */}
            <div className="flex items-center gap-1">
              {/* Cart */}
              <button
                onClick={openCart}
                className="relative p-2.5 text-white/50 hover:text-white transition-colors duration-300"
              >
                <ShoppingCart className="w-5 h-5" />
                <AnimatePresence>
                  {totalItems > 0 && (
                    <motion.span
                      key={totalItems}
                      initial={{ scale: 0 }}
                      animate={{ scale: 1 }}
                      exit={{ scale: 0 }}
                      className="absolute top-0.5 left-0.5 w-4 h-4 bg-[#b5f72e] text-black text-[9px] font-bold flex items-center justify-center rounded-full"
                    >
                      {totalItems}
                    </motion.span>
                  )}
                </AnimatePresence>
              </button>

              {/* Wishlist */}
              <Link href="/shop" className="relative p-2.5 text-white/50 hover:text-white transition-colors duration-300 hidden sm:flex">
                <Heart className="w-5 h-5" />
                {wishlistCount > 0 && (
                  <span className="absolute top-0.5 left-0.5 w-4 h-4 bg-[#b5f72e] text-black text-[9px] font-bold flex items-center justify-center rounded-full">
                    {wishlistCount}
                  </span>
                )}
              </Link>

              {/* CTA button */}
              <Link
                href="/shop"
                className="hidden lg:flex items-center gap-2 mr-2 px-5 py-2.5 bg-[#b5f72e] text-black text-xs font-bold uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-xs transition-all duration-300"
              >
                קנה עכשיו
              </Link>

              {/* Mobile toggle */}
              <button
                onClick={() => setMobileOpen(!mobileOpen)}
                className="lg:hidden p-2.5 text-white/50 hover:text-white transition-colors mr-1"
              >
                {mobileOpen ? <X className="w-5 h-5" /> : <Menu className="w-5 h-5" />}
              </button>
            </div>

            {/* Desktop Nav (center) */}
            <nav className="hidden lg:flex items-center gap-1">
              {navLinks.map(link => (
                <Link
                  key={link.href}
                  href={link.href}
                  className={`relative px-4 py-2 text-sm font-medium tracking-wide transition-all duration-300 group ${
                    pathname === link.href.split('?')[0]
                      ? 'text-[#b5f72e]'
                      : 'text-white/50 hover:text-white'
                  }`}
                >
                  {link.label}
                  <span className={`absolute bottom-0 right-4 left-4 h-px bg-[#b5f72e] transition-all duration-300 ${
                    pathname === link.href.split('?')[0] ? 'opacity-100' : 'opacity-0 group-hover:opacity-40'
                  }`} />
                </Link>
              ))}
            </nav>

            {/* Logo (right side in RTL) */}
            <Link href="/" className="flex items-center gap-2.5 group">
              <div className="relative">
                <div className="w-8 h-8 bg-[#b5f72e] flex items-center justify-center transition-all duration-300 group-hover:shadow-neon-xs">
                  <Zap className="w-5 h-5 text-black fill-black" />
                </div>
                <div className="absolute inset-0 bg-[#b5f72e] blur-md opacity-0 group-hover:opacity-40 transition-opacity duration-300" />
              </div>
              <div className="flex flex-col leading-none">
                <span className="font-display text-2xl lg:text-3xl tracking-[0.15em] text-white group-hover:text-[#b5f72e] transition-colors duration-300">
                  NEXOR
                </span>
                <span className="text-[9px] text-white/25 tracking-[0.35em] uppercase font-medium -mt-0.5">
                  PADEL
                </span>
              </div>
            </Link>

          </div>
        </div>
      </motion.header>

      {/* Mobile Menu */}
      <AnimatePresence>
        {mobileOpen && (
          <motion.div
            initial={{ opacity: 0, x: '100%' }}
            animate={{ opacity: 1, x: 0 }}
            exit={{ opacity: 0, x: '100%' }}
            transition={{ duration: 0.35, ease: [0.22, 1, 0.36, 1] }}
            className="fixed inset-0 z-40 bg-[#020202]/98 backdrop-blur-2xl lg:hidden flex flex-col"
          >
            {/* Decoration */}
            <div className="absolute inset-0 bg-grid-fine bg-grid-fine opacity-50 pointer-events-none" />
            <div className="absolute top-0 left-0 right-0 h-px bg-gradient-to-l from-transparent via-[#b5f72e]/25 to-transparent" />

            <div className="relative flex flex-col h-full px-8 py-8 pt-24">
              <nav className="flex flex-col">
                {navLinks.map((link, i) => (
                  <motion.div
                    key={link.href}
                    initial={{ opacity: 0, x: 30 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{ delay: i * 0.06, duration: 0.5, ease: [0.16, 1, 0.3, 1] }}
                  >
                    <Link
                      href={link.href}
                      className="flex items-center justify-between py-5 border-b border-white/[0.05] group"
                    >
                      <span className="text-3xl font-bold text-white/70 group-hover:text-white transition-colors">
                        {link.label}
                      </span>
                      <span className="w-6 h-6 border border-white/10 group-hover:border-[#b5f72e]/40 transition-colors flex items-center justify-center">
                        <span className="text-white/20 group-hover:text-[#b5f72e] text-xs">←</span>
                      </span>
                    </Link>
                  </motion.div>
                ))}
              </nav>

              <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.35 }}
                className="mt-auto space-y-3"
              >
                <Link
                  href="/shop"
                  className="flex items-center justify-center w-full py-4 bg-[#b5f72e] text-black font-bold uppercase tracking-widest text-sm hover:bg-[#c8ff47] transition-colors"
                >
                  קנה עכשיו
                </Link>
                <Link
                  href="/contact"
                  className="flex items-center justify-center w-full py-4 border border-white/[0.08] text-white/40 text-sm uppercase tracking-widest hover:text-white hover:border-white/20 transition-all"
                >
                  צור קשר
                </Link>
              </motion.div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </>
  )
}
