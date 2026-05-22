'use client'

import { motion } from 'framer-motion'
import Link from 'next/link'
import { ArrowLeft, TrendingUp } from 'lucide-react'
import { bestSellers } from '@/lib/data'
import ProductCard from '@/components/shop/ProductCard'

export default function BestSellers() {
  return (
    <section className="py-24 bg-[#070707]">
      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-100px' }}
          transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
          className="flex flex-col md:flex-row md:items-end justify-between gap-6 mb-12"
        >
          <div>
            <div className="flex items-center gap-2 mb-3">
              <TrendingUp className="w-4 h-4 text-[#b5f72e]" />
              <div className="section-tag mb-0">הנמכרים ביותר</div>
            </div>
            <h2 className="section-heading">
              המועדפים של<br />
              <span className="text-[#b5f72e]">השחקנים</span>
            </h2>
            <p className="text-white/40 mt-3 max-w-md text-sm leading-relaxed">
              הציוד המהימן ביותר על ידי שחקני פאדל רציניים. נבדק במגרש. אהוב על ידי אלפים.
            </p>
          </div>
          <Link
            href="/shop?filter=bestseller"
            className="flex items-center gap-2 text-white/35 text-xs uppercase tracking-widest hover:text-[#b5f72e] transition-colors group whitespace-nowrap"
          >
            <ArrowLeft className="w-4 h-4 group-hover:-translate-x-1 transition-transform" />
            כל הנמכרים
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
