'use client'

import { motion } from 'framer-motion'
import { Shield, Truck, RotateCcw, Award, Headphones, Zap } from 'lucide-react'

const features = [
  {
    icon: Award,
    title: 'Official Authorised Retailer',
    description: '100% authentic gear sourced directly from top padel brands. Every product comes with original manufacturer warranty.',
    accent: '#b5f72e',
  },
  {
    icon: Truck,
    title: 'Fast Free Shipping',
    description: 'Free delivery on all orders over €75. Express next-day shipping available for those who need their gear now.',
    accent: '#b5f72e',
  },
  {
    icon: RotateCcw,
    title: '30-Day Free Returns',
    description: 'Not the right fit? Return it hassle-free within 30 days. No questions asked, no hidden fees.',
    accent: '#b5f72e',
  },
  {
    icon: Headphones,
    title: 'Expert Padel Support',
    description: 'Our team of padel enthusiasts are here to help you find the perfect gear for your game and level.',
    accent: '#b5f72e',
  },
  {
    icon: Shield,
    title: 'Secure Payments',
    description: 'All transactions are encrypted and secured. Accept Visa, Mastercard, PayPal, Apple Pay & more.',
    accent: '#b5f72e',
  },
  {
    icon: Zap,
    title: 'Premium Selection',
    description: 'Curated range of the best padel gear available. We test and approve every product before it reaches you.',
    accent: '#b5f72e',
  },
]

export default function WhyNexor() {
  return (
    <section className="py-24 bg-[#060606] relative overflow-hidden">
      {/* Background accent */}
      <div
        className="absolute top-0 left-1/2 -translate-x-1/2 w-px h-full opacity-30"
        style={{ background: 'linear-gradient(to bottom, transparent, #b5f72e, transparent)' }}
      />
      <div
        className="absolute -left-40 top-1/2 -translate-y-1/2 w-80 h-80 rounded-full opacity-5 pointer-events-none"
        style={{ background: 'radial-gradient(circle, #b5f72e 0%, transparent 70%)', filter: 'blur(40px)' }}
      />

      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, margin: '-100px' }}
          transition={{ duration: 0.7 }}
          className="text-center mb-16"
        >
          <div className="section-tag mx-auto inline-flex">Why NEXOR</div>
          <h2 className="section-heading mt-2">
            The NEXOR<br />
            <span className="text-[#b5f72e]">Difference</span>
          </h2>
          <p className="text-white/40 mt-4 max-w-xl mx-auto">
            We're not just a store. We're a padel destination built for players who demand the best.
          </p>
        </motion.div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {features.map((feature, i) => {
            const Icon = feature.icon
            return (
              <motion.div
                key={feature.title}
                initial={{ opacity: 0, y: 30 }}
                whileInView={{ opacity: 1, y: 0 }}
                viewport={{ once: true, margin: '-50px' }}
                transition={{ duration: 0.6, delay: i * 0.1, ease: [0.22, 1, 0.36, 1] }}
                className="group relative p-6 bg-[#0f0f0f] border border-white/5 hover:border-[#b5f72e]/20 transition-all duration-500 hover:shadow-card-hover"
              >
                {/* Hover background glow */}
                <div className="absolute inset-0 opacity-0 group-hover:opacity-100 transition-opacity duration-500 pointer-events-none"
                  style={{ background: 'radial-gradient(ellipse at top left, rgba(181,247,46,0.03) 0%, transparent 60%)' }}
                />

                <div className="relative">
                  {/* Icon */}
                  <div className="w-12 h-12 border border-[#b5f72e]/20 bg-[#b5f72e]/5 flex items-center justify-center mb-5 group-hover:bg-[#b5f72e]/10 group-hover:border-[#b5f72e]/40 transition-all duration-300">
                    <Icon className="w-5 h-5 text-[#b5f72e]" />
                  </div>

                  {/* Text */}
                  <h3 className="text-white font-bold text-base mb-2 group-hover:text-[#b5f72e] transition-colors duration-300">
                    {feature.title}
                  </h3>
                  <p className="text-white/40 text-sm leading-relaxed">
                    {feature.description}
                  </p>
                </div>
              </motion.div>
            )
          })}
        </div>

        {/* Bottom CTA */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true }}
          transition={{ duration: 0.6, delay: 0.4 }}
          className="text-center mt-16 pt-16 border-t border-white/5"
        >
          <p className="text-3xl md:text-4xl font-display text-white tracking-wide mb-6">
            YOUR NEXT MATCH STARTS HERE.
          </p>
          <a
            href="/shop"
            className="inline-flex items-center gap-3 px-8 py-4 bg-[#b5f72e] text-black font-bold text-sm uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-md transition-all duration-300"
          >
            Shop Premium Gear
          </a>
        </motion.div>
      </div>
    </section>
  )
}
