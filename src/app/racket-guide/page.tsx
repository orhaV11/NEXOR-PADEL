'use client'

import { useState } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import Link from 'next/link'
import { ChevronLeft, RotateCcw, ShoppingBag } from 'lucide-react'
import { products } from '@/lib/data'
import ProductCard from '@/components/shop/ProductCard'
import type { PlayerLevel } from '@/lib/types'

const EASE = [0.16, 1, 0.3, 1] as const

/* ── Step definitions ── */
type StepId = 'level' | 'style' | 'budget'

interface QuizStep {
  id: StepId
  title: string
  subtitle: string
  options: { value: string; label: string; description: string }[]
}

const steps: QuizStep[] = [
  {
    id: 'level',
    title: 'מה רמת המשחק שלך?',
    subtitle: 'שלב 1 מתוך 3',
    options: [
      { value: 'beginner',      label: 'מתחיל',    description: 'משחק פחות משנה, לומד את הבסיס' },
      { value: 'intermediate',  label: 'בינוני',   description: 'שנה–שלוש, בקיא במגרש' },
      { value: 'advanced',      label: 'מתקדם',    description: 'שחקן עם ניסיון רב ומשחק יציב' },
      { value: 'professional',  label: 'מקצועי',   description: 'תחרויות ואימונים ברמה גבוהה' },
    ],
  },
  {
    id: 'style',
    title: 'מה סגנון המשחק שלך?',
    subtitle: 'שלב 2 מתוך 3',
    options: [
      { value: 'defensive',  label: 'הגנתי',       description: 'שולט, ניהול ארוך של הנקודה' },
      { value: 'allround',   label: 'כולל',        description: 'שילוב של התקפה והגנה' },
      { value: 'offensive',  label: 'התקפי',       description: 'עוצמה, בנדג\'ות ומחיצות' },
    ],
  },
  {
    id: 'budget',
    title: 'מה התקציב שלך?',
    subtitle: 'שלב 3 מתוך 3',
    options: [
      { value: 'low',    label: 'עד ₪600',       description: 'כניסה לעולם הפאדל' },
      { value: 'mid',    label: '₪600–₪1,200',   description: 'יחס מחיר-איכות מעולה' },
      { value: 'high',   label: '₪1,200–₪2,000', description: 'ביצועים מקצועיים' },
      { value: 'pro',    label: 'מעל ₪2,000',    description: 'הציוד הטוב ביותר' },
    ],
  },
]

/* ── Filter logic ── */
function getRecommendations(
  level: string,
  style: string,
  budget: string
) {
  const rackets = products.filter(p => p.category === 'rackets')

  /* Level map */
  const levelOrder: Record<string, PlayerLevel[]> = {
    beginner:     ['beginner', 'intermediate'],
    intermediate: ['intermediate', 'advanced'],
    advanced:     ['advanced', 'professional'],
    professional: ['professional', 'advanced'],
  }
  const allowedLevels = levelOrder[level] ?? []

  /* Price ranges */
  const priceMax: Record<string, number> = { low: 600, mid: 1200, high: 2000, pro: Infinity }
  const priceMin: Record<string, number> = { low: 0, mid: 600, high: 1200, pro: 2000 }
  const max = priceMax[budget] ?? Infinity
  const min = priceMin[budget] ?? 0

  const price = (p: typeof rackets[number]) => p.salePrice ?? p.price

  /* Shape preference based on style */
  const shapeHints: Record<string, string[]> = {
    defensive: ['עגול', 'טיפה'],
    allround:  ['טיפה', 'עגול', 'יהלום'],
    offensive: ['יהלום', 'טיפה'],
  }
  const preferredShapes = shapeHints[style] ?? []

  let scored = rackets.map(r => {
    let score = 0
    if (r.playerLevel && allowedLevels.includes(r.playerLevel)) score += 3
    if (price(r) >= min && price(r) <= max) score += 3
    if (r.shape && preferredShapes.some(s => r.shape?.includes(s))) score += 2
    if (r.isBestSeller) score += 1
    if (r.isFeatured) score += 1
    return { product: r, score }
  })

  scored.sort((a, b) => b.score - a.score)

  // Return top 3; if fewer than 1 good match, loosen to all rackets
  const top = scored.filter(s => s.score >= 3)
  return (top.length > 0 ? top : scored).slice(0, 3).map(s => s.product)
}

/* ── Slide transition variants ── */
function slideVariants(dir: 1 | -1) {
  return {
    initial:  { opacity: 0, x: dir * 60 },
    animate:  { opacity: 1, x: 0, transition: { duration: 0.45, ease: EASE } },
    exit:     { opacity: 0, x: dir * -60, transition: { duration: 0.3, ease: EASE } },
  }
}

export default function RacketGuidePage() {
  const [stepIndex, setStepIndex] = useState(0)
  const [answers, setAnswers] = useState<Record<StepId, string>>({ level: '', style: '', budget: '' })
  const [dir, setDir] = useState<1 | -1>(1)
  const [done, setDone] = useState(false)

  const currentStep = steps[stepIndex]
  const currentAnswer = answers[currentStep?.id]

  function select(value: string) {
    setAnswers(a => ({ ...a, [currentStep.id]: value }))
  }

  function next() {
    if (!currentAnswer) return
    if (stepIndex < steps.length - 1) {
      setDir(1)
      setStepIndex(i => i + 1)
    } else {
      setDir(1)
      setDone(true)
    }
  }

  function back() {
    if (stepIndex > 0) {
      setDir(-1)
      setStepIndex(i => i - 1)
    }
  }

  function reset() {
    setDir(-1)
    setDone(false)
    setStepIndex(0)
    setAnswers({ level: '', style: '', budget: '' })
  }

  const recommendations = done
    ? getRecommendations(answers.level, answers.style, answers.budget)
    : []

  return (
    <div className="min-h-screen pt-20">
      {/* Hero */}
      <section className="relative py-24 overflow-hidden">
        <div className="absolute inset-0 bg-[#050505]" />
        <div className="absolute inset-0 bg-grid opacity-25" />
        <div
          className="absolute inset-0 opacity-12"
          style={{ background: 'radial-gradient(ellipse at 50% 0%, rgba(201,165,90,0.12) 0%, transparent 60%)' }}
        />

        <div className="relative max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 text-center">
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.8, ease: EASE }}
          >
            <div className="section-label mx-auto inline-flex mb-6">מדריך בחירה</div>
            <h1 className="section-title mb-5">
              מצא את<br />
              <span className="text-gold-gradient">המחבט שלך</span>
            </h1>
            <p className="text-[#f2eddf]/50 text-lg max-w-xl mx-auto leading-relaxed">
              ענה על שלוש שאלות קצרות — נמצא לך את המחבט המושלם.
            </p>
          </motion.div>
        </div>
      </section>

      <div className="divider" />

      <section className="py-16 max-w-2xl mx-auto px-5 sm:px-8">
        {/* Step indicator */}
        {!done && (
          <div className="flex items-center gap-3 justify-center mb-10">
            {steps.map((s, i) => (
              <div key={s.id} className="flex items-center gap-3">
                <div
                  className="w-7 h-7 flex items-center justify-center text-xs font-bold transition-all duration-400"
                  style={{
                    background: i < stepIndex
                      ? '#c9a55a'
                      : i === stepIndex
                        ? 'rgba(201,165,90,0.15)'
                        : 'transparent',
                    border: i === stepIndex
                      ? '1px solid rgba(201,165,90,0.6)'
                      : i < stepIndex
                        ? '1px solid #c9a55a'
                        : '1px solid rgba(201,165,90,0.15)',
                    color: i < stepIndex
                      ? '#08080a'
                      : i === stepIndex
                        ? '#c9a55a'
                        : 'rgba(201,165,90,0.3)',
                  }}
                >
                  {i < stepIndex ? '✓' : i + 1}
                </div>
                {i < steps.length - 1 && (
                  <div
                    className="w-12 h-px transition-all duration-400"
                    style={{ background: i < stepIndex ? '#c9a55a' : 'rgba(201,165,90,0.12)' }}
                  />
                )}
              </div>
            ))}
          </div>
        )}

        <AnimatePresence mode="wait" custom={dir}>
          {!done ? (
            /* Quiz Step */
            <motion.div
              key={`step-${stepIndex}`}
              {...slideVariants(dir)}
            >
              <div className="mb-8 text-center">
                <p className="text-[#c9a55a] text-xs font-bold uppercase tracking-widest mb-3">
                  {currentStep.subtitle}
                </p>
                <h2 className="text-[#f2eddf] font-bold text-2xl md:text-3xl">
                  {currentStep.title}
                </h2>
              </div>

              <div className="space-y-3 mb-10">
                {currentStep.options.map((opt) => {
                  const selected = currentAnswer === opt.value
                  return (
                    <button
                      key={opt.value}
                      onClick={() => select(opt.value)}
                      className="w-full text-right p-5 transition-all duration-300 flex items-center justify-between gap-4 group"
                      style={{
                        background: selected ? 'rgba(201,165,90,0.08)' : '#0d0d10',
                        border: selected
                          ? '1px solid rgba(201,165,90,0.4)'
                          : '1px solid rgba(201,165,90,0.08)',
                      }}
                    >
                      <div>
                        <p
                          className="font-semibold text-base mb-0.5 transition-colors"
                          style={{ color: selected ? '#c9a55a' : '#f2eddf' }}
                        >
                          {opt.label}
                        </p>
                        <p className="text-[#f2eddf]/35 text-xs">{opt.description}</p>
                      </div>
                      <div
                        className="w-5 h-5 flex-shrink-0 flex items-center justify-center transition-all duration-200"
                        style={{
                          border: selected ? '1px solid #c9a55a' : '1px solid rgba(201,165,90,0.2)',
                          background: selected ? '#c9a55a' : 'transparent',
                        }}
                      >
                        {selected && (
                          <motion.span
                            initial={{ scale: 0 }}
                            animate={{ scale: 1 }}
                            className="text-[#08080a] text-xs font-black"
                          >
                            ✓
                          </motion.span>
                        )}
                      </div>
                    </button>
                  )
                })}
              </div>

              {/* Navigation */}
              <div className="flex items-center justify-between gap-4">
                {stepIndex > 0 ? (
                  <button onClick={back} className="btn-outline flex items-center gap-2">
                    <ChevronLeft className="w-4 h-4" />
                    חזרה
                  </button>
                ) : (
                  <div />
                )}
                <button
                  onClick={next}
                  disabled={!currentAnswer}
                  className="btn-gold"
                  style={{ opacity: currentAnswer ? 1 : 0.4, cursor: currentAnswer ? 'pointer' : 'not-allowed' }}
                >
                  {stepIndex < steps.length - 1 ? 'הבא' : 'הצג המלצות'}
                </button>
              </div>
            </motion.div>
          ) : (
            /* Results */
            <motion.div
              key="results"
              initial={{ opacity: 0, y: 30 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.6, ease: EASE }}
            >
              <div className="text-center mb-10">
                <div className="section-label mx-auto inline-flex mb-4">ההמלצות שלנו</div>
                <h2 className="text-[#f2eddf] font-bold text-2xl md:text-3xl mb-3">
                  מצאנו את המחבטים בשבילך
                </h2>
                <p className="text-[#f2eddf]/40 text-sm">
                  על בסיס הרמה, הסגנון והתקציב שלך
                </p>
              </div>

              {/* Answer summary */}
              <div className="flex flex-wrap justify-center gap-2 mb-10">
                {steps.map(s => {
                  const ans = answers[s.id]
                  const opt = s.options.find(o => o.value === ans)
                  return opt ? (
                    <span key={s.id} className="badge-gold">{opt.label}</span>
                  ) : null
                })}
              </div>

              <div className="grid grid-cols-1 gap-4 mb-10">
                {recommendations.length > 0 ? (
                  <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                    {recommendations.map((product, i) => (
                      <ProductCard key={product.id} product={product} index={i} />
                    ))}
                  </div>
                ) : (
                  <div className="text-center py-12">
                    <p className="text-[#f2eddf]/40 text-sm mb-4">
                      לא נמצאו מחבטים מדויקים — הנה כל הקולקציה שלנו
                    </p>
                    <Link href="/shop?category=rackets" className="btn-outline">
                      צפה בכל המחבטים
                    </Link>
                  </div>
                )}
              </div>

              <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
                <button onClick={reset} className="btn-outline flex items-center gap-2">
                  <RotateCcw className="w-4 h-4" />
                  התחל מחדש
                </button>
                <Link href="/shop?category=rackets" className="btn-gold">
                  <ShoppingBag className="w-4 h-4" />
                  כל המחבטים
                </Link>
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </section>
    </div>
  )
}
