import type { Metadata } from 'next'
import { Heebo, Bebas_Neue } from 'next/font/google'
import './globals.css'
import { CartProvider } from '@/context/CartContext'
import { WishlistProvider } from '@/context/WishlistContext'
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
    default: 'NEXOR Padel — ציוד פאדל פרימיום בישראל',
    template: '%s | NEXOR Padel',
  },
  description: 'מחבטי פאדל, נעליים, תיקים ואביזרים פרימיום. ציוד פאדל לשחקנים שרוצים יותר. הדור הבא של הפאדל בישראל.',
  keywords: ['פאדל', 'מחבט פאדל', 'ציוד פאדל', 'חנות פאדל', 'nexor padel', 'פאדל ישראל'],
  openGraph: {
    title: 'NEXOR Padel — ציוד פאדל פרימיום בישראל',
    description: 'ציוד פאדל פרימיום נבחר לשחקנים שרוצים יותר. הדור הבא של הפאדל.',
    type: 'website',
    siteName: 'NEXOR Padel',
    locale: 'he_IL',
  },
  robots: { index: true, follow: true },
}

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="he" dir="rtl" className={`${heebo.variable} ${bebas.variable}`}>
      <body className="bg-brand-bg text-white antialiased font-heebo">
        <div className="noise-overlay" aria-hidden="true" />
        <CartProvider>
          <WishlistProvider>
            <Header />
            <main>{children}</main>
            <Footer />
            <CartDrawer />
          </WishlistProvider>
        </CartProvider>
      </body>
    </html>
  )
}
