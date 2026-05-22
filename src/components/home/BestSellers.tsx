'use client'

import { motion } from 'framer-motion'
import Link from 'next/link'
import { ArrowRight, TrendingUp } from 'lucide-react'
import { bestSellers } from '@/lib/data'
import ProductCard from '@/components/shop/ProductCard'

export default function BestSellers() {
  return (
    <section className="py-24 bg-[#070707]">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-100px' }}
          transition={{ duration: 0.7, ease: [0.22, 1, 0.36, 1] }}
          className="flex flex-col md:flex-row md:items-end justify-between gap-6 mb-12"
        >
          <div>
            <div className="flex items-center gap-2 mb-3">
              <TrendingUp className="w-4 h-4 text-[#b5f72e]" />
              <div className="section-tag mb-0">Best Sellers</div>
            </div>
            <h2 className="section-heading">
              Player&apos;s<br />
              <span className="text-[#b5f72e]">Favourites</span>
            </h2>
            <p className="text-white/40 mt-3 max-w-md">
              The most trusted gear by serious padel players. Proven on court. Loved by thousands.
            </p>
          </div>
          <Link
            href="/shop?filter=bestseller"
            className="flex items-center gap-2 text-white/40 text-sm uppercase tracking-widest hover:text-[#b5f72e] transition-colors group whitespace-nowrap"
          >
            View All <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
          </Link>
        </motion.div>

        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4">
          {bestSellers.map((product, i) => (
            <ProductCard key={product.id} product={product} index={i} />
          ))}
        </div>
      </div>
    </section>
  )
}
