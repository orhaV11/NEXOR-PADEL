import Link from 'next/link'
import { Instagram, Youtube, ArrowLeft } from 'lucide-react'

/* ── data ─────────────────────────────────────────────── */

const categories = [
  { label: 'מחבטים',  href: '/shop?category=rackets' },
  { label: 'כדורים',  href: '/shop?category=balls' },
  { label: 'נעליים',  href: '/shop?category=shoes' },
  { label: 'תיקים',   href: '/shop?category=bags' },
  { label: 'גריפים',  href: '/shop?category=grips' },
  { label: 'ביגוד',   href: '/shop?category=apparel' },
  { label: 'אביזרים', href: '/shop?category=accessories' },
]

const service = [
  { label: 'צור קשר',         href: '/contact' },
  { label: 'משלוחים והחזרות', href: '/shipping' },
  { label: 'שאלות נפוצות',    href: '/faq' },
  { label: 'מדריך מחבטים',    href: '/racket-guide' },
  { label: 'אודות NEXOR',     href: '/about' },
  { label: 'מותגים',          href: '/brands' },
]

const socials = [
  { Icon: Instagram, label: 'אינסטגרם', href: 'https://instagram.com' },
  { Icon: Youtube,   label: 'יוטיוב',    href: 'https://youtube.com' },
]

/* ── sub-components ───────────────────────────────────── */

function FooterLinkList({ links }: { links: typeof categories }) {
  return (
    <ul className="space-y-3.5" role="list">
      {links.map(link => (
        <li key={link.href}>
          <Link
            href={link.href}
            className="group flex items-center gap-2.5
                       text-[rgba(242,237,223,0.35)] text-sm
                       hover:text-[#c9a55a] transition-colors duration-250"
          >
            {/* Dot bullet */}
            <span
              className="w-1 h-1 rounded-full shrink-0 transition-all duration-300
                         bg-[rgba(201,165,90,0.25)] group-hover:bg-[#c9a55a]
                         group-hover:shadow-[0_0_6px_rgba(201,165,90,0.5)]"
              aria-hidden="true"
            />
            {link.label}
          </Link>
        </li>
      ))}
    </ul>
  )
}

/* ── newsletter form (needs 'use client' for interactivity) ─ */
/* Since Footer is a server component we render a plain form  */

function NewsletterForm() {
  return (
    /* The form submits via standard HTML; a server action can be wired later */
    <form
      action="#"
      method="post"
      className="flex flex-col gap-2"
      aria-label="הרשמה לניוזלטר"
    >
      <input
        type="email"
        name="email"
        placeholder="האימייל שלך"
        dir="ltr"
        autoComplete="email"
        required
        className="luxury-input text-sm"
      />
      <button
        type="submit"
        className="btn-gold w-full py-[0.85rem] text-[10px] rounded-none"
      >
        הרשמה
        <ArrowLeft className="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
      </button>
    </form>
  )
}

/* ── main component ───────────────────────────────────── */

export default function Footer() {
  const year = new Date().getFullYear()

  return (
    <footer className="bg-[#08080a] mt-24" aria-label="כותרת תחתונה">

      {/* ─── Gold top border ──────────────────────────────── */}
      <div className="divider" />

      {/* ─── Ambient glow strip ───────────────────────────── */}
      <div
        className="h-48 pointer-events-none"
        style={{
          background:
            'radial-gradient(ellipse 70% 100% at 50% 0%, rgba(201,165,90,0.06) 0%, transparent 100%)',
        }}
        aria-hidden="true"
      />

      {/* ─── Main grid ────────────────────────────────────── */}
      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 pb-16 lg:pb-20 -mt-32">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-12 lg:gap-8">

          {/* ── Col 1: Brand ── */}
          <div className="sm:col-span-2 lg:col-span-1">

            {/* Logo mark */}
            <Link href="/" className="group flex items-center gap-2.5 w-fit mb-7" aria-label="NEXOR Padel — דף הבית">
              <div className="relative flex items-center justify-center w-9 h-9 shrink-0">
                <div
                  className="absolute inset-0 rotate-45 border transition-all duration-500
                             border-[rgba(201,165,90,0.3)] bg-[rgba(201,165,90,0.07)]
                             group-hover:border-[rgba(201,165,90,0.6)] group-hover:bg-[rgba(201,165,90,0.13)]"
                />
                <div
                  className="absolute rotate-45 w-3.5 h-3.5 border transition-all duration-700
                             border-[rgba(201,165,90,0.14)]"
                />
                <span className="relative text-[#c9a55a] text-[11px] font-black leading-none
                                  group-hover:text-[#e2c890] transition-colors duration-300 select-none">
                  ◆
                </span>
              </div>
              <div className="flex flex-col leading-none">
                <span className="font-display text-2xl tracking-[0.12em] text-[#f2eddf]
                                  group-hover:text-[#e2c890] transition-colors duration-300">
                  NEXOR
                </span>
                <span className="text-[7px] text-[#c9a55a] tracking-[0.5em] uppercase font-semibold -mt-0.5 opacity-75">
                  PADEL
                </span>
              </div>
            </Link>

            {/* Tagline */}
            <p className="text-[rgba(242,237,223,0.35)] text-sm leading-[1.75] mb-7 max-w-[24ch]">
              פאדל ברמה אחרת.{' '}
              <span className="text-[rgba(242,237,223,0.55)]">
                ציוד מדויק לשחקנים שרוצים יותר.
              </span>
            </p>

            {/* Social icons */}
            <div className="flex items-center gap-2.5" aria-label="רשתות חברתיות">
              {socials.map(({ Icon, label, href }) => (
                <a
                  key={label}
                  href={href}
                  target="_blank"
                  rel="noopener noreferrer"
                  aria-label={label}
                  className="w-9 h-9 border border-[rgba(201,165,90,0.14)]
                             flex items-center justify-center
                             text-[rgba(242,237,223,0.25)]
                             hover:text-[#c9a55a] hover:border-[rgba(201,165,90,0.45)]
                             hover:bg-[rgba(201,165,90,0.04)]
                             hover:shadow-gold-sm
                             transition-all duration-300"
                >
                  <Icon className="w-[15px] h-[15px]" />
                </a>
              ))}
            </div>
          </div>

          {/* ── Col 2: קטגוריות ── */}
          <div>
            <h3 className="section-label mb-6">קטגוריות</h3>
            <FooterLinkList links={categories} />
          </div>

          {/* ── Col 3: שירות ── */}
          <div>
            <h3 className="section-label mb-6">שירות</h3>
            <FooterLinkList links={service} />
          </div>

          {/* ── Col 4: ניוזלטר ── */}
          <div>
            <h3 className="section-label mb-3">הישאר מעודכן</h3>
            <p className="text-[rgba(242,237,223,0.35)] text-sm leading-relaxed mb-5">
              מבצעים בלעדיים, מוצרים חדשים וטיפים — ישירות אליך.
            </p>

            <NewsletterForm />

            {/* Contact info */}
            <address className="not-italic mt-7 space-y-2.5">
              <p>
                <a
                  href="mailto:hello@nexorpadel.co.il"
                  className="flex items-center gap-2 text-[rgba(242,237,223,0.25)] text-xs
                             hover:text-[#c9a55a] transition-colors duration-200"
                >
                  <span aria-hidden="true" className="text-[rgba(201,165,90,0.5)]">✉</span>
                  hello@nexorpadel.co.il
                </a>
              </p>
              <p>
                <a
                  href="tel:054-123-4567"
                  dir="ltr"
                  className="flex items-center gap-2 text-[rgba(242,237,223,0.25)] text-xs
                             hover:text-[#c9a55a] transition-colors duration-200 w-fit"
                >
                  <span aria-hidden="true" className="text-[rgba(201,165,90,0.5)]" dir="ltr">📱</span>
                  <span className="ltr">054-123-4567</span>
                </a>
              </p>
              <p className="flex items-center gap-2 text-[rgba(242,237,223,0.25)] text-xs">
                <span aria-hidden="true" className="text-[rgba(201,165,90,0.5)]">📦</span>
                <span>משלוח לכל הארץ</span>
              </p>
            </address>
          </div>

        </div>
      </div>

      {/* ─── Divider before bottom bar ────────────────────── */}
      <div className="divider" />

      {/* ─── Bottom bar ───────────────────────────────────── */}
      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 py-5">
        <div className="flex flex-col sm:flex-row items-center justify-between gap-4
                        text-xs text-[rgba(242,237,223,0.18)]">

          {/* Copyright — start (right in RTL) */}
          <p className="order-3 sm:order-1">
            &copy; {year} NEXOR Padel. כל הזכויות שמורות.
          </p>

          {/* Legal links — center */}
          <div className="flex items-center gap-5 order-1 sm:order-2">
            <Link
              href="/privacy"
              className="hover:text-[rgba(242,237,223,0.5)] transition-colors duration-200"
            >
              מדיניות פרטיות
            </Link>
            <span className="text-[rgba(201,165,90,0.18)]" aria-hidden="true">·</span>
            <Link
              href="/terms"
              className="hover:text-[rgba(242,237,223,0.5)] transition-colors duration-200"
            >
              תנאי שימוש
            </Link>
          </div>

          {/* Brand stamp — end (left in RTL) */}
          <p className="order-2 sm:order-3">
            <span className="text-[rgba(201,165,90,0.45)] font-semibold">NEXOR Padel</span>
            {' '}·{' '}ישראל
          </p>
        </div>
      </div>

    </footer>
  )
}
