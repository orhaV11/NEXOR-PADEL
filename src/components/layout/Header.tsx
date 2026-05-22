'use client'

import { useState, useEffect, useRef } from 'react'
import Link from 'next/link'
import { usePathname } from 'next/navigation'
import { motion, AnimatePresence, useScroll, useSpring } from 'framer-motion'
import { ShoppingCart, Heart, Menu, X, Zap } from 'lucide-react'
import { useCart } from '@/context/CartContext'
import { useWishlist } from '@/context/WishlistContext'

/* ── data ─────────────────────────────────────────────── */

const navLinks = [
  { label: 'חנות',          href: '/shop' },
  { label: 'מחבטים',        href: '/shop?category=rackets' },
  { label: 'נעליים וביגוד', href: '/shop?category=shoes' },
  { label: 'מותגים',        href: '/brands' },
  { label: 'מדריך',         href: '/racket-guide' },
]

const LUXURY_EASE = [0.16, 1, 0.3, 1] as const

/* ── Logo ─────────────────────────────────────────────── */

function NexorLogo({ size = 'md' }: { size?: 'sm' | 'md' }) {
  const isSm = size === 'sm'
  return (
    <div className="flex items-center gap-2.5 group">
      {/* Diamond gemstone mark */}
      <div className={`relative flex items-center justify-center shrink-0 ${isSm ? 'w-7 h-7' : 'w-8 h-8'}`}>
        {/* Outer square rotated — spins further on logo hover */}
        <div
          className={[
            'absolute inset-0 rotate-45 border transition-all duration-500',
            'border-[rgba(201,165,90,0.3)] bg-[rgba(201,165,90,0.07)]',
            'group-hover:border-[rgba(201,165,90,0.6)] group-hover:bg-[rgba(201,165,90,0.14)]',
            'group-hover:rotate-[60deg]',
            'group-hover:shadow-gold-sm',
          ].join(' ')}
        />
        {/* Inner diamond */}
        <div
          className={[
            'absolute rotate-45 border transition-all duration-700',
            'border-[rgba(201,165,90,0.15)]',
            isSm ? 'w-2.5 h-2.5' : 'w-3 h-3',
          ].join(' ')}
        />
        <span
          className={[
            'relative font-black leading-none select-none transition-colors duration-300',
            'text-[#c9a55a] group-hover:text-[#e2c890]',
            isSm ? 'text-[9px]' : 'text-[10px]',
          ].join(' ')}
        >
          ◆
        </span>
      </div>

      {/* Wordmark */}
      <div className="flex flex-col leading-none">
        <span
          className={[
            'font-display tracking-[0.12em] text-[#f2eddf] transition-colors duration-300',
            'group-hover:text-[#e2c890]',
            isSm ? 'text-xl' : 'text-2xl lg:text-[1.65rem]',
          ].join(' ')}
        >
          NEXOR
        </span>
        <span className="text-[7px] text-[#c9a55a] tracking-[0.5em] uppercase font-semibold -mt-0.5 opacity-75 group-hover:opacity-100 transition-opacity duration-300">
          PADEL
        </span>
      </div>
    </div>
  )
}

/* ── Count badge ──────────────────────────────────────── */

function CountBadge({ count }: { count: number }) {
  return (
    <AnimatePresence>
      {count > 0 && (
        <motion.span
          key={count}
          initial={{ scale: 0, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          exit={{ scale: 0, opacity: 0 }}
          transition={{ type: 'spring', stiffness: 520, damping: 22 }}
          className="absolute -top-0.5 -start-0.5 min-w-[17px] h-[17px] px-0.5
                     bg-[#c9a55a] text-[#08080a] text-[9px] font-black
                     flex items-center justify-center rounded-full ltr pointer-events-none"
        >
          {count > 99 ? '99+' : count}
        </motion.span>
      )}
    </AnimatePresence>
  )
}

/* ── Main component ───────────────────────────────────── */

export default function Header() {
  const [scrolled, setScrolled]     = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)
  const [cartPulse, setCartPulse]   = useState(false)
  const pathname                    = usePathname()
  const { totalItems, openCart }    = useCart()
  const { count: wishlistCount }    = useWishlist()

  /* Scroll progress bar */
  const { scrollYProgress } = useScroll()
  const scaleX = useSpring(scrollYProgress, { stiffness: 200, damping: 30 })

  /* Cart pulse when items are added */
  const prevItems = useRef(totalItems)
  useEffect(() => {
    if (totalItems > prevItems.current) {
      setCartPulse(true)
      setTimeout(() => setCartPulse(false), 600)
    }
    prevItems.current = totalItems
  }, [totalItems])

  /* scroll detection */
  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 20)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  /* close mobile on route change */
  useEffect(() => { setMobileOpen(false) }, [pathname])

  /* body-scroll lock while mobile menu is open */
  useEffect(() => {
    document.body.style.overflow = mobileOpen ? 'hidden' : ''
    return () => { document.body.style.overflow = '' }
  }, [mobileOpen])

  const isActive = (href: string) =>
    href.includes('?')
      ? pathname === href.split('?')[0]
      : pathname === href

  return (
    <>
      {/* ═══════════════════════ STICKY HEADER ═══════════════════════ */}
      <motion.header
        initial={{ y: -80, opacity: 0 }}
        animate={{ y: 0,   opacity: 1 }}
        transition={{ duration: 0.9, ease: LUXURY_EASE }}
        className={[
          'fixed top-0 inset-x-0 z-50 transition-all duration-500',
          scrolled
            ? 'glass border-b border-[rgba(201,165,90,0.1)]'
            : 'bg-transparent',
        ].join(' ')}
      >
        {/* Scroll progress bar — very top of header */}
        <motion.div
          className="absolute top-0 left-0 right-0 h-[2px] bg-gradient-to-r from-[#b08840] via-[#e2c890] to-[#b08840] origin-left z-10"
          style={{ scaleX, transformOrigin: 'left' }}
        />

        {/* Top accent shimmer — always visible, slightly brighter on scroll */}
        <div
          className={[
            'h-px transition-opacity duration-500',
            'bg-gradient-to-l from-transparent via-[rgba(201,165,90,0.3)] to-transparent',
            scrolled ? 'opacity-100' : 'opacity-60',
          ].join(' ')}
        />

        <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
          <div className="flex items-center justify-between h-14 lg:h-20">

            {/* ── ACTIONS — left side in RTL ── */}
            <div className="flex items-center gap-0.5">

              {/* Cart — pulses when an item is added */}
              <motion.button
                onClick={openCart}
                aria-label="פתח עגלת קניות"
                animate={cartPulse ? { scale: [1, 1.3, 1] } : {}}
                transition={{ duration: 0.4, type: 'spring' }}
                className="relative p-2.5 text-[rgba(242,237,223,0.4)] hover:text-[#f2eddf]
                           transition-colors duration-300 rounded-sm
                           hover:bg-[rgba(201,165,90,0.05)]"
              >
                <ShoppingCart className="w-[18px] h-[18px]" />
                <CountBadge count={totalItems} />
              </motion.button>

              {/* Wishlist (hidden on xs) */}
              <Link
                href="/wishlist"
                aria-label="רשימת משאלות"
                className="relative p-2.5 text-[rgba(242,237,223,0.4)] hover:text-[#f2eddf]
                           transition-colors duration-300 rounded-sm
                           hover:bg-[rgba(201,165,90,0.05)] hidden sm:flex"
              >
                <Heart className="w-[18px] h-[18px]" />
                <CountBadge count={wishlistCount} />
              </Link>

              {/* Thin vertical divider */}
              <div className="hidden lg:block w-px h-5 bg-[rgba(201,165,90,0.15)] mx-2" />

              {/* Desktop CTA */}
              <Link
                href="/shop"
                className="btn-gold hidden lg:inline-flex ms-1 px-5 py-2 text-[10px] rounded-none"
              >
                <Zap className="w-3.5 h-3.5 shrink-0" />
                קנה עכשיו
              </Link>

              {/* Mobile hamburger */}
              <button
                onClick={() => setMobileOpen(v => !v)}
                aria-label={mobileOpen ? 'סגור תפריט' : 'פתח תפריט'}
                aria-expanded={mobileOpen}
                className="lg:hidden p-2.5 text-[rgba(242,237,223,0.4)] hover:text-[#f2eddf]
                           transition-colors duration-300 ms-1"
              >
                <AnimatePresence mode="wait" initial={false}>
                  {mobileOpen ? (
                    <motion.span
                      key="close"
                      initial={{ rotate: -90, opacity: 0 }}
                      animate={{ rotate: 0,  opacity: 1 }}
                      exit={{ rotate: 90,  opacity: 0 }}
                      transition={{ duration: 0.2 }}
                      className="flex"
                    >
                      <X className="w-5 h-5" />
                    </motion.span>
                  ) : (
                    <motion.span
                      key="open"
                      initial={{ rotate: 90,  opacity: 0 }}
                      animate={{ rotate: 0,   opacity: 1 }}
                      exit={{ rotate: -90, opacity: 0 }}
                      transition={{ duration: 0.2 }}
                      className="flex"
                    >
                      <Menu className="w-5 h-5" />
                    </motion.span>
                  )}
                </AnimatePresence>
              </button>
            </div>

            {/* ── DESKTOP NAV — center ── */}
            <nav
              className="hidden lg:flex items-center gap-7"
              aria-label="ניווט ראשי"
            >
              {navLinks.map(link => (
                <Link
                  key={link.href}
                  href={link.href}
                  className={`nav-link${isActive(link.href) ? ' active' : ''}`}
                >
                  {link.label}
                </Link>
              ))}
            </nav>

            {/* ── LOGO — right side in RTL ── */}
            <Link href="/" aria-label="NEXOR Padel — דף הבית">
              <NexorLogo />
            </Link>

          </div>
        </div>
      </motion.header>

      {/* ═══════════════════════ MOBILE MENU ═══════════════════════ */}
      <AnimatePresence>
        {mobileOpen && (
          <>
            {/* Backdrop */}
            <motion.div
              key="mobile-backdrop"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              transition={{ duration: 0.35 }}
              className="fixed inset-0 z-40 bg-[#08080a]/70 backdrop-blur-md lg:hidden"
              onClick={() => setMobileOpen(false)}
              aria-hidden="true"
            />

            {/* Full-screen panel — slides down from top */}
            <motion.div
              key="mobile-panel"
              initial={{ opacity: 0, y: '-100%' }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: '-100%' }}
              transition={{ duration: 0.5, ease: LUXURY_EASE }}
              className="fixed inset-0 z-50 bg-[#08080a] lg:hidden flex flex-col overflow-hidden"
              role="dialog"
              aria-modal="true"
              aria-label="תפריט ניווט"
            >
              {/* Top shimmer */}
              <div className="h-px bg-gradient-to-l from-transparent via-[rgba(201,165,90,0.28)] to-transparent" />

              {/* Ambient glow — top-right corner */}
              <div
                className="absolute top-0 right-0 w-80 h-80 pointer-events-none"
                style={{
                  background: 'radial-gradient(ellipse at top right, rgba(201,165,90,0.08) 0%, transparent 65%)',
                }}
                aria-hidden="true"
              />
              {/* Ambient glow — bottom-left corner */}
              <div
                className="absolute bottom-0 left-0 w-60 h-60 pointer-events-none"
                style={{
                  background: 'radial-gradient(ellipse at bottom left, rgba(201,165,90,0.05) 0%, transparent 60%)',
                }}
                aria-hidden="true"
              />

              {/* Header row */}
              <div className="relative flex items-center justify-between px-6 pt-5 pb-5">
                {/* Close button — start side (left in RTL) */}
                <button
                  onClick={() => setMobileOpen(false)}
                  aria-label="סגור תפריט"
                  className="p-2.5 text-[rgba(242,237,223,0.35)] hover:text-[#f2eddf]
                             border border-[rgba(201,165,90,0.12)] hover:border-[rgba(201,165,90,0.35)]
                             transition-all duration-300"
                >
                  <X className="w-[18px] h-[18px]" />
                </button>

                {/* Logo echo — end side (right in RTL) */}
                <Link href="/" onClick={() => setMobileOpen(false)}>
                  <NexorLogo size="sm" />
                </Link>
              </div>

              {/* Divider */}
              <div className="divider mx-6" />

              {/* Nav links */}
              <nav
                className="relative flex-1 flex flex-col px-6 pt-4 overflow-y-auto"
                aria-label="ניווט נייד"
              >
                {navLinks.map((link, i) => (
                  <motion.div
                    key={link.href}
                    initial={{ opacity: 0, x: 40 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{
                      delay: 0.05 + i * 0.065,
                      duration: 0.45,
                      ease: LUXURY_EASE,
                    }}
                  >
                    <Link
                      href={link.href}
                      className={[
                        'flex items-center justify-between py-5 group',
                        'border-b border-[rgba(242,237,223,0.05)]',
                        'transition-colors duration-300',
                        isActive(link.href)
                          ? 'text-[#c9a55a]'
                          : 'text-[rgba(242,237,223,0.45)] hover:text-[#f2eddf]',
                      ].join(' ')}
                    >
                      <div className="flex flex-col items-start gap-1">
                        <span className="text-[1.75rem] font-bold tracking-tight leading-none">
                          {link.label}
                        </span>
                        {/* Gold underline for active item */}
                        {isActive(link.href) && (
                          <motion.div
                            layoutId="mobile-active-indicator"
                            className="w-6 h-[2px] bg-[#c9a55a]"
                          />
                        )}
                      </div>

                      {/* Arrow indicator */}
                      <span
                        className={[
                          'w-8 h-8 border flex items-center justify-center shrink-0',
                          'transition-all duration-300',
                          isActive(link.href)
                            ? 'border-[rgba(201,165,90,0.5)] text-[#c9a55a] bg-[rgba(201,165,90,0.06)]'
                            : 'border-[rgba(242,237,223,0.07)] text-[rgba(242,237,223,0.18)]',
                          'group-hover:border-[rgba(201,165,90,0.4)] group-hover:text-[#c9a55a]',
                        ].join(' ')}
                        aria-hidden="true"
                      >
                        {/* ChevronLeft = "forward" arrow in RTL */}
                        <svg
                          width="12"
                          height="12"
                          viewBox="0 0 12 12"
                          fill="none"
                          aria-hidden="true"
                        >
                          <path d="M7.5 2.5L4.5 6L7.5 9.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
                        </svg>
                      </span>
                    </Link>
                  </motion.div>
                ))}
              </nav>

              {/* Bottom CTAs */}
              <motion.div
                initial={{ opacity: 0, y: 28 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.38, duration: 0.5, ease: LUXURY_EASE }}
                className="relative px-6 pt-4 pb-10 space-y-3"
              >
                <div className="divider mb-5" />

                <Link
                  href="/shop"
                  className="btn-gold w-full py-[1.1rem] text-[11px] rounded-none"
                  onClick={() => setMobileOpen(false)}
                >
                  <Zap className="w-4 h-4 shrink-0" />
                  קנה עכשיו
                </Link>
                <Link
                  href="/contact"
                  className="btn-outline w-full py-[1.1rem] text-[11px] rounded-none"
                  onClick={() => setMobileOpen(false)}
                >
                  צור קשר
                </Link>
              </motion.div>
            </motion.div>
          </>
        )}
      </AnimatePresence>
    </>
  )
}
