'use client'

import { motion } from 'framer-motion'
import Link from 'next/link'
import { ArrowRight, Target, Heart, Zap, Award, Users, Globe } from 'lucide-react'

const values = [
  {
    icon: Target,
    title: 'Curated with Purpose',
    description: 'We don\'t carry everything. We carry the best. Every product in our shop has been tested, vetted and chosen for performance, durability and quality.',
  },
  {
    icon: Heart,
    title: 'Built for Players',
    description: 'We\'re padel players ourselves. We understand what it means to invest in the right gear — and we\'re here to help you make that choice with confidence.',
  },
  {
    icon: Award,
    title: 'Only Authentic Gear',
    description: 'NEXOR is an official authorised retailer for every brand we stock. You\'ll never find fakes here. Every product comes with a manufacturer warranty.',
  },
  {
    icon: Zap,
    title: 'Premium Service',
    description: 'Fast delivery, easy returns, real customer support. Shopping with NEXOR should feel as premium as the gear you\'re buying.',
  },
]

const milestones = [
  { year: '2022', title: 'NEXOR Founded', description: 'Born from a frustration with low-quality padel retail, NEXOR launched with a mission to do it better.' },
  { year: '2023', title: 'Official Partnerships', description: 'Secured official retailer partnerships with Head, Bullpadel, Nox and Adidas Padel.' },
  { year: '2024', title: '10,000 Players', description: 'Reached our first 10,000 customer milestone. The community was growing fast.' },
  { year: '2025', title: 'Full Platform Launch', description: 'Launched our full premium platform with the complete gear lineup for every level of player.' },
]

export default function AboutPage() {
  return (
    <div className="min-h-screen pt-20">
      {/* Hero */}
      <section className="relative py-24 md:py-36 overflow-hidden">
        <div className="absolute inset-0 bg-[#050505]" />
        <div className="absolute inset-0 bg-hero-radial opacity-60" />
        <div className="absolute inset-0 bg-grid-pattern bg-grid opacity-100" />

        <div className="relative max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 text-center">
          <motion.div
            initial={{ opacity: 0, y: 40 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.8, ease: [0.22, 1, 0.36, 1] }}
          >
            <div className="section-tag mx-auto inline-flex mb-6">Our Story</div>
            <h1 className="font-display text-6xl md:text-8xl lg:text-9xl text-white tracking-wide uppercase leading-none mb-6">
              ABOUT<br />
              <span style={{ color: '#b5f72e' }}>NEXOR</span>
            </h1>
            <p className="text-white/50 text-lg md:text-xl max-w-2xl mx-auto leading-relaxed">
              We built NEXOR because we were tired of compromising. As padel players, we wanted a store that matched the level of the sport we love.
            </p>
          </motion.div>
        </div>
      </section>

      {/* Mission Statement */}
      <section className="py-24 bg-[#070707]">
        <div className="max-w-5xl mx-auto px-4 sm:px-6 lg:px-8">
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true }}
            transition={{ duration: 0.7 }}
            className="grid grid-cols-1 lg:grid-cols-2 gap-16 items-center"
          >
            <div>
              <div className="section-tag mb-4">Who We Are</div>
              <h2 className="font-display text-4xl md:text-5xl text-white tracking-wide uppercase mb-6 leading-tight">
                Premium Gear.<br />
                <span className="text-[#b5f72e]">Serious Purpose.</span>
              </h2>
              <p className="text-white/50 text-base leading-relaxed mb-4">
                NEXOR Padel was founded by players who were frustrated with the state of padel retail — overpriced basics, poor advice, and gear that didn&apos;t match the quality of the sport.
              </p>
              <p className="text-white/50 text-base leading-relaxed mb-6">
                We set out to build something different. A premium padel destination where every product is hand-picked, every brand is official, and every customer gets the kind of service they deserve.
              </p>
              <p className="text-white/50 text-base leading-relaxed">
                Whether you&apos;re picking up a padel racket for the first time or you&apos;re a tournament-level competitor looking for your next weapon — NEXOR is built for you.
              </p>
            </div>

            {/* Stats */}
            <div className="grid grid-cols-2 gap-4">
              {[
                { icon: Users, value: '10,000+', label: 'Happy Players' },
                { icon: Award, value: '6+', label: 'Official Brand Partners' },
                { icon: Globe, value: '20+', label: 'Countries Served' },
                { icon: Heart, value: '4.9★', label: 'Average Rating' },
              ].map(({ icon: Icon, value, label }, i) => (
                <motion.div
                  key={label}
                  initial={{ opacity: 0, scale: 0.9 }}
                  whileInView={{ opacity: 1, scale: 1 }}
                  viewport={{ once: true }}
                  transition={{ duration: 0.5, delay: i * 0.1 }}
                  className="p-6 bg-[#0f0f0f] border border-white/5 text-center"
                >
                  <div className="w-10 h-10 mx-auto mb-3 bg-[#b5f72e]/10 border border-[#b5f72e]/20 flex items-center justify-center">
                    <Icon className="w-5 h-5 text-[#b5f72e]" />
                  </div>
                  <p className="font-display text-3xl text-[#b5f72e] tracking-wide">{value}</p>
                  <p className="text-white/30 text-xs uppercase tracking-widest mt-1">{label}</p>
                </motion.div>
              ))}
            </div>
          </motion.div>
        </div>
      </section>

      {/* Values */}
      <section className="py-24">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true }}
            transition={{ duration: 0.7 }}
            className="text-center mb-16"
          >
            <div className="section-tag mx-auto inline-flex mb-4">What We Stand For</div>
            <h2 className="section-heading">
              Our<br />
              <span className="text-[#b5f72e]">Values</span>
            </h2>
          </motion.div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {values.map((value, i) => {
              const Icon = value.icon
              return (
                <motion.div
                  key={value.title}
                  initial={{ opacity: 0, y: 30 }}
                  whileInView={{ opacity: 1, y: 0 }}
                  viewport={{ once: true }}
                  transition={{ duration: 0.6, delay: i * 0.1 }}
                  className="p-8 bg-[#0f0f0f] border border-white/5 hover:border-[#b5f72e]/20 transition-all duration-500 group"
                >
                  <div className="w-12 h-12 border border-[#b5f72e]/20 bg-[#b5f72e]/5 flex items-center justify-center mb-5 group-hover:bg-[#b5f72e]/10 transition-all duration-300">
                    <Icon className="w-5 h-5 text-[#b5f72e]" />
                  </div>
                  <h3 className="text-white font-bold text-lg mb-3">{value.title}</h3>
                  <p className="text-white/40 text-sm leading-relaxed">{value.description}</p>
                </motion.div>
              )
            })}
          </div>
        </div>
      </section>

      {/* Timeline */}
      <section className="py-24 bg-[#070707]">
        <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8">
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true }}
            transition={{ duration: 0.7 }}
            className="text-center mb-16"
          >
            <div className="section-tag mx-auto inline-flex mb-4">Our Journey</div>
            <h2 className="section-heading">
              How We<br />
              <span className="text-[#b5f72e]">Got Here</span>
            </h2>
          </motion.div>

          <div className="relative">
            <div className="absolute left-1/2 -translate-x-px top-0 bottom-0 w-px bg-white/5" />
            {milestones.map((m, i) => (
              <motion.div
                key={m.year}
                initial={{ opacity: 0, x: i % 2 === 0 ? -40 : 40 }}
                whileInView={{ opacity: 1, x: 0 }}
                viewport={{ once: true }}
                transition={{ duration: 0.6, delay: i * 0.15 }}
                className={`relative flex items-center gap-8 mb-12 ${i % 2 === 0 ? 'flex-row' : 'flex-row-reverse'}`}
              >
                <div className={`flex-1 ${i % 2 === 0 ? 'text-right' : 'text-left'}`}>
                  <div className="inline-block p-5 bg-[#0f0f0f] border border-white/5 hover:border-[#b5f72e]/20 transition-all duration-300">
                    <p className="text-[#b5f72e] text-xs font-bold uppercase tracking-widest mb-1">{m.year}</p>
                    <h3 className="text-white font-bold text-base mb-1">{m.title}</h3>
                    <p className="text-white/40 text-sm">{m.description}</p>
                  </div>
                </div>
                <div className="relative z-10 w-3 h-3 bg-[#b5f72e] flex-shrink-0 shadow-neon-sm" />
                <div className="flex-1" />
              </motion.div>
            ))}
          </div>
        </div>
      </section>

      {/* CTA Section */}
      <section className="py-24">
        <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 text-center">
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true }}
            transition={{ duration: 0.7 }}
          >
            <h2 className="font-display text-5xl md:text-6xl text-white tracking-wide uppercase mb-6">
              READY TO PLAY<br />
              <span className="text-[#b5f72e]">YOUR BEST?</span>
            </h2>
            <p className="text-white/40 mb-10 max-w-md mx-auto">
              From your first match to your next level. Curated gear. Serious performance.
            </p>
            <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
              <Link
                href="/shop"
                className="flex items-center gap-2 px-8 py-4 bg-[#b5f72e] text-black font-bold text-sm uppercase tracking-widest hover:bg-[#c8ff47] hover:shadow-neon-md transition-all group"
              >
                Shop Now <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
              </Link>
              <Link
                href="/contact"
                className="flex items-center gap-2 px-8 py-4 border border-white/20 text-white text-sm uppercase tracking-widest hover:border-[#b5f72e]/40 hover:text-[#b5f72e] transition-all"
              >
                Get in Touch
              </Link>
            </div>
          </motion.div>
        </div>
      </section>
    </div>
  )
}
