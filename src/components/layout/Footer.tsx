import Link from 'next/link'
import { Zap, Instagram, Youtube, ArrowLeft } from 'lucide-react'

const footerLinks = {
  shop: [
    { label: 'כל המוצרים', href: '/shop' },
    { label: 'מחבטים', href: '/shop?category=rackets' },
    { label: 'נעליים', href: '/shop?category=shoes' },
    { label: 'תיקים', href: '/shop?category=bags' },
    { label: 'אביזרים', href: '/shop?category=accessories' },
    { label: 'חדש', href: '/shop?filter=new' },
    { label: 'הנמכרים ביותר', href: '/shop?filter=bestseller' },
  ],
  info: [
    { label: 'אודות NEXOR', href: '/about' },
    { label: 'צור קשר', href: '/contact' },
    { label: 'מדיניות משלוח', href: '/contact' },
    { label: 'החזרות והחלפות', href: '/contact' },
    { label: 'מדריך מידות', href: '/contact' },
    { label: 'שאלות נפוצות', href: '/#faq' },
  ],
}

export default function Footer() {
  return (
    <footer className="bg-[#050505] border-t border-white/[0.04] mt-24">
      {/* Main Footer */}
      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 py-16 lg:py-20">
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-12 lg:gap-8">

          {/* Brand Column */}
          <div className="lg:col-span-1">
            <Link href="/" className="flex items-center gap-2.5 group mb-6 w-fit">
              <div className="w-8 h-8 bg-[#b5f72e] flex items-center justify-center group-hover:shadow-neon-xs transition-all">
                <Zap className="w-5 h-5 text-black fill-black" />
              </div>
              <span className="font-display text-2xl tracking-[0.15em] text-white group-hover:text-[#b5f72e] transition-colors">
                NEXOR
              </span>
            </Link>
            <p className="text-white/35 text-sm leading-relaxed mb-6">
              ציוד פאדל פרימיום נבחר לשחקנים שרוצים יותר. ציוד מדויק. ביצועים רציניים.
            </p>
            {/* Social Links */}
            <div className="flex items-center gap-2.5">
              {[
                { Icon: Instagram, label: 'אינסטגרם', href: '#' },
                { Icon: Youtube, label: 'יוטיוב', href: '#' },
              ].map(({ Icon, label, href }) => (
                <a
                  key={label}
                  href={href}
                  aria-label={label}
                  className="w-9 h-9 border border-white/[0.08] flex items-center justify-center text-white/30 hover:text-[#b5f72e] hover:border-[#b5f72e]/30 transition-all duration-300"
                >
                  <Icon className="w-4 h-4" />
                </a>
              ))}
            </div>
          </div>

          {/* Shop Links */}
          <div>
            <h3 className="text-white text-xs font-bold uppercase tracking-[0.25em] mb-5">חנות</h3>
            <ul className="space-y-3">
              {footerLinks.shop.map(link => (
                <li key={link.href}>
                  <Link href={link.href} className="text-white/35 text-sm hover:text-white/70 transition-colors duration-200">
                    {link.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          {/* Info Links */}
          <div>
            <h3 className="text-white text-xs font-bold uppercase tracking-[0.25em] mb-5">מידע</h3>
            <ul className="space-y-3">
              {footerLinks.info.map(link => (
                <li key={link.label}>
                  <Link href={link.href} className="text-white/35 text-sm hover:text-white/70 transition-colors duration-200">
                    {link.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          {/* Newsletter */}
          <div>
            <h3 className="text-white text-xs font-bold uppercase tracking-[0.25em] mb-5">הישאר מעודכן</h3>
            <p className="text-white/35 text-sm mb-5 leading-relaxed">
              קבל עסקאות בלעדיות, מוצרים חדשים וטיפים לפאדל ישירות לתיבת הדואר שלך.
            </p>
            <div className="flex flex-col gap-2">
              <input
                type="email"
                placeholder="האימייל שלך"
                dir="ltr"
                className="w-full bg-white/[0.04] border border-white/[0.07] px-4 py-3 text-white placeholder-white/20 text-sm focus:outline-none focus:border-[#b5f72e]/30 transition-colors"
              />
              <button className="flex items-center justify-center gap-2 py-3 bg-[#b5f72e] text-black text-xs font-bold uppercase tracking-widest hover:bg-[#c8ff47] transition-colors">
                הרשם
                <ArrowLeft className="w-3.5 h-3.5" />
              </button>
            </div>
            {/* Contact Info */}
            <div className="mt-6 space-y-1.5 text-white/25 text-xs">
              <p>✉ hello@nexorpadel.co.il</p>
              <p>📱 054-123-4567</p>
              <p>🕐 ראשון–חמישי: 09:00–18:00</p>
            </div>
          </div>
        </div>
      </div>

      {/* Luxury divider */}
      <div className="divider-gold" />

      {/* Bottom Bar */}
      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 py-5 flex flex-col sm:flex-row items-center justify-between gap-4">
        <p className="text-white/15 text-xs">
          © {new Date().getFullYear()} NEXOR Padel. כל הזכויות שמורות.
        </p>
        <div className="flex items-center gap-5">
          {['מדיניות פרטיות', 'תנאי שימוש', 'עוגיות'].map(label => (
            <Link key={label} href="/" className="text-white/15 text-xs hover:text-white/40 transition-colors">
              {label}
            </Link>
          ))}
        </div>
        <p className="text-white/15 text-xs">
          <span className="text-[#b5f72e]/50">NEXOR Padel</span> · ישראל
        </p>
      </div>
    </footer>
  )
}
