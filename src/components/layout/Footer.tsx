import Link from 'next/link'
import { Zap, Instagram, Youtube, Facebook, ArrowRight } from 'lucide-react'

const footerLinks = {
  shop: [
    { label: 'All Products', href: '/shop' },
    { label: 'Rackets', href: '/shop?category=rackets' },
    { label: 'Shoes', href: '/shop?category=shoes' },
    { label: 'Bags', href: '/shop?category=bags' },
    { label: 'Accessories', href: '/shop?category=accessories' },
    { label: 'New Arrivals', href: '/shop?filter=new' },
    { label: 'Best Sellers', href: '/shop?filter=bestseller' },
  ],
  info: [
    { label: 'About NEXOR', href: '/about' },
    { label: 'Contact', href: '/contact' },
    { label: 'Shipping Policy', href: '/contact' },
    { label: 'Returns & Exchanges', href: '/contact' },
    { label: 'Size Guide', href: '/contact' },
    { label: 'FAQ', href: '/#faq' },
  ],
  legal: [
    { label: 'Privacy Policy', href: '/' },
    { label: 'Terms of Service', href: '/' },
    { label: 'Cookie Policy', href: '/' },
  ],
}

export default function Footer() {
  return (
    <footer className="bg-[#060606] border-t border-white/5 mt-24">
      {/* Main Footer */}
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-16 lg:py-20">
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-12 lg:gap-8">
          {/* Brand Column */}
          <div className="lg:col-span-1">
            <Link href="/" className="flex items-center gap-2 group mb-6">
              <div className="w-8 h-8 bg-[#b5f72e] flex items-center justify-center">
                <Zap className="w-5 h-5 text-black fill-black" />
              </div>
              <span className="font-display text-2xl tracking-[0.12em] text-white group-hover:text-[#b5f72e] transition-colors">
                NEXOR
              </span>
            </Link>
            <p className="text-white/40 text-sm leading-relaxed mb-6">
              Premium padel gear for players who want more. Curated equipment. Serious performance.
            </p>
            {/* Social Links */}
            <div className="flex items-center gap-3">
              {[
                { Icon: Instagram, label: 'Instagram', href: '#' },
                { Icon: Youtube, label: 'YouTube', href: '#' },
                { Icon: Facebook, label: 'Facebook', href: '#' },
              ].map(({ Icon, label, href }) => (
                <a
                  key={label}
                  href={href}
                  aria-label={label}
                  className="w-9 h-9 border border-white/10 flex items-center justify-center text-white/40 hover:text-[#b5f72e] hover:border-[#b5f72e]/40 transition-all duration-300"
                >
                  <Icon className="w-4 h-4" />
                </a>
              ))}
            </div>
          </div>

          {/* Shop Links */}
          <div>
            <h3 className="text-white text-xs font-bold uppercase tracking-[0.2em] mb-5">Shop</h3>
            <ul className="space-y-3">
              {footerLinks.shop.map(link => (
                <li key={link.href}>
                  <Link
                    href={link.href}
                    className="text-white/40 text-sm hover:text-white transition-colors duration-200"
                  >
                    {link.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          {/* Info Links */}
          <div>
            <h3 className="text-white text-xs font-bold uppercase tracking-[0.2em] mb-5">Info</h3>
            <ul className="space-y-3">
              {footerLinks.info.map(link => (
                <li key={link.label}>
                  <Link
                    href={link.href}
                    className="text-white/40 text-sm hover:text-white transition-colors duration-200"
                  >
                    {link.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          {/* Newsletter */}
          <div>
            <h3 className="text-white text-xs font-bold uppercase tracking-[0.2em] mb-5">Stay Updated</h3>
            <p className="text-white/40 text-sm mb-4">
              Get exclusive deals, new arrivals and padel tips delivered to your inbox.
            </p>
            <div className="flex">
              <input
                type="email"
                placeholder="Your email"
                className="flex-1 bg-white/5 border border-white/10 border-r-0 px-3 py-2.5 text-sm text-white placeholder-white/20 focus:outline-none focus:border-[#b5f72e]/40"
              />
              <button className="px-4 py-2.5 bg-[#b5f72e] text-black hover:bg-[#c8ff47] transition-colors flex-shrink-0">
                <ArrowRight className="w-4 h-4" />
              </button>
            </div>
            {/* Contact Info */}
            <div className="mt-6 space-y-2 text-white/30 text-xs">
              <p>📧 hello@nexorpadel.com</p>
              <p>📱 +34 600 123 456</p>
              <p>🕐 Mon–Fri 9:00–18:00</p>
            </div>
          </div>
        </div>
      </div>

      {/* Bottom Bar */}
      <div className="border-t border-white/5">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-6 flex flex-col sm:flex-row items-center justify-between gap-4">
          <p className="text-white/20 text-xs">
            © {new Date().getFullYear()} NEXOR Padel. All rights reserved.
          </p>
          <div className="flex items-center gap-6">
            {footerLinks.legal.map(link => (
              <Link key={link.label} href={link.href} className="text-white/20 text-xs hover:text-white/50 transition-colors">
                {link.label}
              </Link>
            ))}
          </div>
          <div className="flex items-center gap-2 text-white/20 text-xs">
            <span>Built for champions.</span>
            <span className="text-[#b5f72e]">Play NEXOR.</span>
          </div>
        </div>
      </div>
    </footer>
  )
}
