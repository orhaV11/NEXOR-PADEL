import type { Metadata } from 'next'
import { Heebo, Bebas_Neue } from 'next/font/google'
import './globals.css'
import { CartProvider } from '@/context/CartContext'
import { WishlistProvider } from '@/context/WishlistContext'
import { RecentlyViewedProvider } from '@/context/RecentlyViewedContext'
import Header from '@/components/layout/Header'
import Footer from '@/components/layout/Footer'
import CartDrawer from '@/components/layout/CartDrawer'

const heebo = Heebo({
  subsets: ['hebrew', 'latin'],
  weight: ['300', '400', '500', '600', '700', '800', '900'],
  variable: '--font-heebo',
  display: 'swap',
})

const bebas = Bebas_Neue({
  weight: '400',
  subsets: ['latin'],
  variable: '--font-bebas',
  display: 'swap',
})

export const metadata: Metadata = {
  title: {
    default: 'NEXOR Padel — פאדל ברמה אחרת',
    template: '%s | NEXOR Padel',
  },
  description: 'ציוד פאדל פרימיום לשחקנים שלא מתפשרים. מחבטים, נעליים, תיקים ואביזרים מהמותגים המובילים בעולם. משלוח מהיר לכל הארץ.',
  keywords: ['פאדל', 'מחבט פאדל', 'ציוד פאדל', 'nexor padel', 'פאדל ישראל', 'head padel', 'bullpadel', 'nox'],
  openGraph: {
    title: 'NEXOR Padel — פאדל ברמה אחרת',
    description: 'ציוד פאדל פרימיום לשחקנים שלא מתפשרים.',
    type: 'website',
    siteName: 'NEXOR Padel',
    locale: 'he_IL',
  },
  robots: { index: true, follow: true },
}

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="he" dir="rtl" className={`${heebo.variable} ${bebas.variable}`}>
      <body className="bg-brand-bg text-ivory antialiased" style={{ fontFamily: 'var(--font-heebo), Heebo, Arial, sans-serif' }}>
        <div className="noise-overlay" aria-hidden="true" />
        <CartProvider>
          <WishlistProvider>
            <RecentlyViewedProvider>
              <Header />
              <main>{children}</main>
              <Footer />
              <CartDrawer />
            </RecentlyViewedProvider>
          </WishlistProvider>
        </CartProvider>
      </body>
    </html>
  )
}
