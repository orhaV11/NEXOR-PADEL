'use client'

import { useEffect, useRef } from 'react'
import { motion } from 'framer-motion'
import Link from 'next/link'
import { ArrowRight, Play, ChevronDown } from 'lucide-react'

export default function Hero() {
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    let animationId: number
    let particles: Array<{
      x: number; y: number; vx: number; vy: number
      size: number; opacity: number; life: number; maxLife: number
    }> = []

    const resize = () => {
      canvas.width = canvas.offsetWidth
      canvas.height = canvas.offsetHeight
    }
    resize()
    window.addEventListener('resize', resize)

    const spawnParticle = () => {
      const side = Math.random()
      let x, y, vx, vy
      if (side < 0.5) {
        x = Math.random() * canvas.width
        y = canvas.height + 10
        vx = (Math.random() - 0.5) * 0.5
        vy = -Math.random() * 1.5 - 0.5
      } else {
        x = -10
        y = Math.random() * canvas.height
        vx = Math.random() * 1.5 + 0.5
        vy = (Math.random() - 0.5) * 0.5
      }
      const maxLife = 200 + Math.random() * 200
      particles.push({ x, y, vx, vy, size: Math.random() * 1.5 + 0.5, opacity: 0, life: 0, maxLife })
    }

    const draw = () => {
      ctx.clearRect(0, 0, canvas.width, canvas.height)
      if (Math.random() < 0.12) spawnParticle()

      particles = particles.filter(p => p.life < p.maxLife)
      particles.forEach(p => {
        p.x += p.vx
        p.y += p.vy
        p.life++
        const progress = p.life / p.maxLife
        p.opacity = progress < 0.2 ? progress / 0.2 : progress > 0.8 ? (1 - progress) / 0.2 : 1
        ctx.beginPath()
        ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2)
        ctx.fillStyle = `rgba(181, 247, 46, ${p.opacity * 0.4})`
        ctx.fill()
      })

      // Draw speed lines
      ctx.strokeStyle = 'rgba(181, 247, 46, 0.04)'
      ctx.lineWidth = 1
      for (let i = 0; i < 8; i++) {
        const x = (canvas.width / 8) * i + ((Date.now() * 0.02 * (i % 2 === 0 ? 1 : -1)) % canvas.width)
        ctx.beginPath()
        ctx.moveTo(x % canvas.width, 0)
        ctx.lineTo((x + 200) % canvas.width, canvas.height)
        ctx.stroke()
      }

      animationId = requestAnimationFrame(draw)
    }

    draw()
    return () => {
      cancelAnimationFrame(animationId)
      window.removeEventListener('resize', resize)
    }
  }, [])

  const container = {
    hidden: {},
    show: { transition: { staggerChildren: 0.12, delayChildren: 0.3 } },
  }

  const item = {
    hidden: { opacity: 0, y: 40 },
    show: { opacity: 1, y: 0, transition: { duration: 0.8, ease: [0.22, 1, 0.36, 1] } },
  }

  return (
    <section className="relative min-h-screen flex flex-col items-center justify-center overflow-hidden bg-brand-bg">
      {/* Canvas background */}
      <canvas ref={canvasRef} className="absolute inset-0 w-full h-full" aria-hidden="true" />

      {/* Background layers */}
      <div className="absolute inset-0 bg-hero-radial" aria-hidden="true" />
      <div
        className="absolute inset-0 bg-grid-pattern bg-grid opacity-100"
        aria-hidden="true"
      />

      {/* Central glow orb */}
      <div
        className="absolute top-1/3 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[400px] opacity-10 animate-orb-drift pointer-events-none"
        style={{
          background: 'radial-gradient(ellipse, #b5f72e 0%, transparent 70%)',
          filter: 'blur(60px)',
        }}
        aria-hidden="true"
      />

      {/* Content */}
      <div className="relative z-10 max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 pt-24 pb-16 text-center">
        <motion.div variants={container} initial="hidden" animate="show">
          {/* Season badge */}
          <motion.div variants={item} className="flex justify-center mb-8">
            <div className="inline-flex items-center gap-2 px-4 py-1.5 border border-[#b5f72e]/30 bg-[#b5f72e]/5 backdrop-blur-sm">
              <div className="w-1.5 h-1.5 bg-[#b5f72e] rounded-full animate-pulse" />
              <span className="text-[#b5f72e] text-xs font-bold uppercase tracking-[0.25em]">
                New Season 2025 Collection
              </span>
              <ArrowRight className="w-3 h-3 text-[#b5f72e]" />
            </div>
          </motion.div>

          {/* Main headline */}
          <motion.div variants={item}>
            <h1 className="font-display text-[13vw] sm:text-[11vw] md:text-[9vw] lg:text-[8rem] xl:text-[9rem] leading-[0.9] tracking-wide uppercase text-white mb-2">
              PLAY NEXT.
            </h1>
            <h1
              className="font-display text-[13vw] sm:text-[11vw] md:text-[9vw] lg:text-[8rem] xl:text-[9rem] leading-[0.9] tracking-wide uppercase text-glow-lime mb-10"
              style={{ color: '#b5f72e' }}
            >
              PLAY NEXOR.
            </h1>
          </motion.div>

          {/* Subheadline */}
          <motion.p
            variants={item}
            className="text-white/50 text-lg md:text-xl lg:text-2xl font-light tracking-wide max-w-2xl mx-auto mb-12"
          >
            Premium padel gear for players who want more.
          </motion.p>

          {/* CTA Buttons */}
          <motion.div variants={item} className="flex flex-col sm:flex-row items-center justify-center gap-4">
            <Link
              href="/shop"
              className="group inline-flex items-center gap-3 px-8 py-4 bg-[#b5f72e] text-black font-bold text-sm uppercase tracking-widest hover:bg-[#c8ff47] transition-all duration-300 hover:shadow-neon-md"
            >
              Shop Now
              <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
            </Link>
            <Link
              href="/shop?category=rackets"
              className="inline-flex items-center gap-3 px-8 py-4 border border-white/20 text-white font-medium text-sm uppercase tracking-widest hover:border-[#b5f72e]/50 hover:text-[#b5f72e] transition-all duration-300"
            >
              <Play className="w-4 h-4 fill-current" />
              Find Your Racket
            </Link>
          </motion.div>

          {/* Stats Row */}
          <motion.div
            variants={item}
            className="flex flex-wrap items-center justify-center gap-12 mt-16 pt-12 border-t border-white/5"
          >
            {[
              { value: '500+', label: 'Premium Products' },
              { value: '10k+', label: 'Happy Players' },
              { value: '24h', label: 'Fast Delivery' },
              { value: '30d', label: 'Free Returns' },
            ].map(stat => (
              <div key={stat.label} className="text-center">
                <p className="text-2xl md:text-3xl font-display text-[#b5f72e] tracking-wide">{stat.value}</p>
                <p className="text-white/30 text-xs uppercase tracking-widest mt-1">{stat.label}</p>
              </div>
            ))}
          </motion.div>
        </motion.div>
      </div>

      {/* Scroll indicator */}
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 1.5 }}
        className="absolute bottom-8 left-1/2 -translate-x-1/2 flex flex-col items-center gap-2 text-white/20"
      >
        <span className="text-xs uppercase tracking-[0.3em]">Scroll</span>
        <motion.div
          animate={{ y: [0, 8, 0] }}
          transition={{ duration: 1.5, repeat: Infinity, ease: 'easeInOut' }}
        >
          <ChevronDown className="w-4 h-4" />
        </motion.div>
      </motion.div>
    </section>
  )
}
