import type { Metadata } from 'next'
import Hero from '@/components/home/Hero'
import FeaturedCategories from '@/components/home/FeaturedCategories'
import BestSellers from '@/components/home/BestSellers'
import NewArrivals from '@/components/home/NewArrivals'
import WhyNexor from '@/components/home/WhyNexor'
import CustomerReviews from '@/components/home/CustomerReviews'
import FAQ from '@/components/home/FAQ'
import Newsletter from '@/components/home/Newsletter'

export const metadata: Metadata = {
  title: 'NEXOR Padel — Premium Padel Gear | Play Next. Play NEXOR.',
  description: 'Premium padel rackets, shoes, bags and accessories. Curated gear for players who want more. Shop the best padel brands — Head, Bullpadel, Nox, Adidas.',
}

export default function HomePage() {
  return (
    <>
      <Hero />
      <FeaturedCategories />
      <BestSellers />
      <NewArrivals />
      <WhyNexor />
      <CustomerReviews />
      <FAQ />
      <Newsletter />
    </>
  )
}
