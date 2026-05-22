'use client'

import { useState, useEffect } from 'react'
import Link from 'next/link'
import { usePathname } from 'next/navigation'
import { motion, AnimatePresence } from 'framer-motion'
import { ShoppingCart, Heart, Menu, X, ChevronRight, Zap } from 'lucide-react'
import { useCart } from '@/context/CartContext'
import { useWishlist } from '@/context/WishlistContext'

const navLinks = [
  { label: 'Shop', href: '/shop' },
  { label: 'Rackets', href: '/shop?category=rackets' },
  { label: 'Shoes', href: '/shop?category=shoes' },
  { label: 'Bags', href: '/shop?category=bags' },
  { label: 'About', href: '/about' },
]

export default function Header() {
  const [scrolled, setScrolled] = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)
  const pathname = usePathname()
  const { totalItems, openCart } = useCart()
  const { count: wishlistCount } = useWishlist()

  useEffect(() => {
    const handleScroll = () => setScrolled(window.scrollY > 20)
    window.addEventListener('scroll', handleScroll, { passive: true })
    return () => window.removeEventListener('scroll', handleScroll)
  }, [])

  useEffect(() => {
    setMobileOpen(false)
  }, [pathname])

  useEffect(() => {
    document.body.style.overflow = mobileOpen ? 'hidden' : ''
    return () => { document.body.style.overflow = '' }
  }, [mobileOpen])

  return (
    <>
      <motion.header
        initial={{ y: -100 }}
        animate={{ y: 0 }}
        transition={{ duration: 0.6, ease: [0.22, 1, 0.36, 1] }}
        className={`fixed top-0 left-0 right-0 z-50 transition-all duration-500 ${
          scrolled
            ? 'bg-brand-bg/95 backdrop-blur-xl border-b border-white/5 shadow-[0_4px_30px_rgba(0,0,0,0.5)]'
            : 'bg-transparent'
        }`}
      >
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between h-16 lg:h-20">
            {/* Logo */}
            <Link href="/" className="flex items-center gap-2 group">
              <div className="relative">
                <div className="w-8 h-8 bg-[#b5f72e] flex items-center justify-center">
                  <Zap className="w-5 h-5 text-black fill-black" />
                </div>
                <div className="absolute inset-0 bg-[#b5f72e] blur-md opacity-0 group-hover:opacity-50 transition-opacity duration-300" />
              </div>
              <span className="font-display text-2xl lg:text-3xl tracking-[0.12em] text-white group-hover:text-[#b5f72e] transition-colors duration-300">
                NEXOR
              </span>
              <span className="hidden sm:block text-white/30 text-xs font-medium tracking-[0.3em] uppercase mt-1">
                PADEL
              </span>
            </Link>

            {/* Desktop Nav */}
            <nav className="hidden lg:flex items-center gap-1">
              {navLinks.map(link => (
                <Link
                  key={link.href}
                  href={link.href}
                  className={`relative px-4 py-2 text-sm font-medium uppercase tracking-widest transition-all duration-300 group ${
                    pathname === link.href.split('?')[0]
                      ? 'text-[#b5f72e]'
                      : 'text-white/60 hover:text-white'
                  }`}
                >
                  {link.label}
                  <span className={`absolute bottom-0 left-4 right-4 h-px bg-[#b5f72e] transition-all duration-300 ${
                    pathname === link.href.split('?')[0] ? 'opacity-100' : 'opacity-0 group-hover:opacity-60'
                  }`} />
                </Link>
              ))}
            </nav>

            {/* Actions */}
            <div className="flex items-center gap-1">
              {/* Wishlist */}
              <Link href="/shop" className="relative p-2.5 text-white/60 hover:text-white transition-colors duration-300 hidden sm:flex">
                <Heart className="w-5 h-5" />
                {wishlistCount > 0 && (
                  <span className="absolute top-1 right-1 w-4 h-4 bg-[#b5f72e] text-black text-[10px] font-bold flex items-center justify-center rounded-full">
                    {wishlistCount}
                  </span>
                )}
              </Link>

              {/* Cart */}
              <button
                onClick={openCart}
                className="relative p-2.5 text-white/60 hover:text-white transition-colors duration-300 flex items-center gap-2"
              >
                <div className="relative">
                  <ShoppingCart className="w-5 h-5" />
                  {totalItems > 0 && (
                    <motion.span
                      key={totalItems}
                      initial={{ scale: 0 }}
                      animate={{ scale: 1 }}
                      className="absolute -top-2 -right-2 w-4 h-4 bg-[#b5f72e] text-black text-[10px] font-bold flex items-center justify-center rounded-full"
                    >
                      {totalItems}
                    </motion.span>
                  )}
                </div>
                <span className="hidden sm:block text-sm font-medium text-white/60 hover:text-white transition-colors">
                  Cart
                </span>
              </button>

              {/* Shop CTA */}
              <Link
                href="/shop"
                className="hidden lg:flex items-center gap-2 ml-3 px-5 py-2.5 bg-[#b5f72e] text-black text-xs font-bold uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-sm transition-all duration-300"
              >
                Shop Now
                <ChevronRight className="w-3 h-3" />
              </Link>

              {/* Mobile Menu Toggle */}
              <button
                onClick={() => setMobileOpen(!mobileOpen)}
                className="lg:hidden p-2.5 text-white/60 hover:text-white transition-colors ml-1"
              >
                {mobileOpen ? <X className="w-5 h-5" /> : <Menu className="w-5 h-5" />}
              </button>
            </div>
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
            transition={{ duration: 0.3, ease: [0.22, 1, 0.36, 1] }}
            className="fixed inset-0 z-40 bg-brand-bg/98 backdrop-blur-xl lg:hidden pt-20"
          >
            <div className="flex flex-col h-full px-6 py-8">
              <nav className="flex flex-col gap-1">
                {navLinks.map((link, i) => (
                  <motion.div
                    key={link.href}
                    initial={{ opacity: 0, x: 30 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{ delay: i * 0.07 }}
                  >
                    <Link
                      href={link.href}
                      className="flex items-center justify-between py-4 border-b border-white/5 text-2xl font-display tracking-wider text-white/80 hover:text-[#b5f72e] transition-colors"
                    >
                      {link.label}
                      <ChevronRight className="w-5 h-5 text-white/20" />
                    </Link>
                  </motion.div>
                ))}
              </nav>

              <div className="mt-auto">
                <Link
                  href="/shop"
                  className="flex items-center justify-center w-full py-4 bg-[#b5f72e] text-black font-bold uppercase tracking-widest text-sm hover:bg-[#c8ff47] transition-colors"
                >
                  Shop Now
                </Link>
                <Link
                  href="/contact"
                  className="flex items-center justify-center w-full py-4 text-white/50 text-sm uppercase tracking-widest mt-3 hover:text-white transition-colors"
                >
                  Contact Us
                </Link>
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </>
  )
}
