'use client'

import { motion } from 'framer-motion'
import Link from 'next/link'
import { ArrowRight, Sparkles } from 'lucide-react'
import { newArrivals } from '@/lib/data'
import ProductCard from '@/components/shop/ProductCard'

export default function NewArrivals() {
  return (
    <section className="py-24">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-100px' }}
          transition={{ duration: 0.7 }}
          className="flex flex-col md:flex-row md:items-end justify-between gap-6 mb-12"
        >
          <div>
            <div className="flex items-center gap-2 mb-3">
              <Sparkles className="w-4 h-4 text-[#b5f72e]" />
              <div className="section-tag mb-0">Just Dropped</div>
            </div>
            <h2 className="section-heading">
              New<br />
              <span className="text-[#b5f72e]">Arrivals</span>
            </h2>
            <p className="text-white/40 mt-3 max-w-md">
              The freshest gear from the top padel brands. First on court, first in your bag.
            </p>
          </div>
          <Link
            href="/shop?filter=new"
            className="flex items-center gap-2 text-white/40 text-sm uppercase tracking-widest hover:text-[#b5f72e] transition-colors group whitespace-nowrap"
          >
            View All New <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
          </Link>
        </motion.div>

        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4">
          {newArrivals.map((product, i) => (
            <ProductCard key={product.id} product={product} index={i} />
          ))}
        </div>
      </div>
    </section>
  )
}
