import type { Metadata } from 'next'
import { Inter, Bebas_Neue } from 'next/font/google'
import './globals.css'
import { CartProvider } from '@/context/CartContext'
import { WishlistProvider } from '@/context/WishlistContext'
import Header from '@/components/layout/Header'
import Footer from '@/components/layout/Footer'
import CartDrawer from '@/components/layout/CartDrawer'

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-inter',
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
    default: 'NEXOR Padel — Premium Padel Gear',
    template: '%s | NEXOR Padel',
  },
  description: 'Premium padel rackets, shoes, bags and accessories. Built for players who want more. Play Next. Play NEXOR.',
  keywords: ['padel', 'padel rackets', 'padel shop', 'padel gear', 'premium padel', 'nexor padel'],
  openGraph: {
    title: 'NEXOR Padel — Premium Padel Gear',
    description: 'Curated premium padel gear for serious players. Play Next. Play NEXOR.',
    type: 'website',
    siteName: 'NEXOR Padel',
  },
  twitter: {
    card: 'summary_large_image',
    title: 'NEXOR Padel',
    description: 'Premium padel gear for players who want more.',
  },
  robots: {
    index: true,
    follow: true,
  },
}

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${inter.variable} ${bebas.variable}`}>
      <body className="bg-brand-bg text-white antialiased">
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
