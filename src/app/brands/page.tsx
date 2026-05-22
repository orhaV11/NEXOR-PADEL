import Link from 'next/link'
import { brands, products } from '@/lib/data'
import type { Metadata } from 'next'

export const metadata: Metadata = {
  title: 'מותגים | NEXOR Padel',
  description: 'ספקים מורשים רשמיים — Head, Bullpadel, Nox, Adidas, Wilson ו-NEXOR. ציוד פאדל מקורי 100%.',
}

const brandDetails: Record<string, { tagline: string; color: string }> = {
  Head:      { tagline: 'מוביל השוק הגלובלי בטכנולוגיית פאדל', color: 'from-[#1a0a0a] to-[#0d0d10]' },
  Bullpadel: { tagline: 'ספרד — ביצועים תחרותיים ממוצא', color: 'from-[#0a0a1a] to-[#0d0d10]' },
  Nox:       { tagline: 'שחקני עלית בוחרים Nox', color: 'from-[#0a1a0a] to-[#0d0d10]' },
  Adidas:    { tagline: 'הנדסה גרמנית, ביצועים עולמיים', color: 'from-[#0a0a0a] to-[#0d0d10]' },
  Wilson:    { tagline: 'מורשת ניצחון של עשורים', color: 'from-[#1a0f0a] to-[#0d0d10]' },
  NEXOR:     { tagline: 'המותג הישראלי — מיוצר עבורנו', color: 'from-[#0d0a00] to-[#0d0d10]' },
}

export default function BrandsPage() {
  const displayBrands = brands.filter(b => b !== 'כל המותגים')

  return (
    <div className="min-h-screen pt-20">
      {/* Hero */}
      <section className="relative py-24 md:py-32 overflow-hidden">
        <div className="absolute inset-0 bg-[#050505]" />
        <div className="absolute inset-0 bg-grid opacity-30" />
        <div
          className="absolute inset-0 opacity-15"
          style={{ background: 'radial-gradient(ellipse at 50% 0%, rgba(201,165,90,0.12) 0%, transparent 65%)' }}
        />

        <div className="relative max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 text-center">
          <div className="section-label mx-auto inline-flex mb-6">המותגים שלנו</div>
          <h1 className="section-title mb-5">
            מותגים
          </h1>
          <p className="text-[#f2eddf]/50 text-lg max-w-xl mx-auto leading-relaxed">
            ספקים מורשים רשמיים
          </p>
        </div>
      </section>

      <div className="divider" />

      {/* Brands Grid */}
      <section className="py-20 max-w-7xl mx-auto px-5 sm:px-8 lg:px-10">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-5">
          {displayBrands.map((brand, i) => {
            const count = products.filter(p => p.brand === brand).length
            const details = brandDetails[brand] ?? { tagline: 'מותג פאדל מקצועי', color: 'from-[#0d0d0d] to-[#0d0d10]' }

            return (
              <Link
                key={brand}
                href={`/shop?brand=${encodeURIComponent(brand)}`}
                className="group relative overflow-hidden bg-[#0d0d10] border border-[rgba(201,165,90,0.1)] hover:border-[rgba(201,165,90,0.3)] transition-all duration-500 block"
                style={{
                  animationDelay: `${i * 0.08}s`,
                }}
              >
                {/* Gradient bg */}
                <div className={`absolute inset-0 bg-gradient-to-br ${details.color} opacity-80`} />

                {/* Gold glow on hover */}
                <div
                  className="absolute inset-0 opacity-0 group-hover:opacity-100 transition-opacity duration-500"
                  style={{ background: 'radial-gradient(ellipse at 50% 100%, rgba(201,165,90,0.08) 0%, transparent 70%)' }}
                />

                <div className="relative p-8 md:p-10">
                  {/* Brand name */}
                  <h2
                    className="font-display text-4xl md:text-5xl font-black uppercase tracking-tight mb-2 transition-colors duration-300"
                    style={{ color: 'rgba(242,237,223,0.9)' }}
                  >
                    {brand}
                  </h2>

                  {/* Tagline */}
                  <p className="text-[#f2eddf]/35 text-sm mb-6 leading-relaxed">
                    {details.tagline}
                  </p>

                  {/* Product count + arrow */}
                  <div className="flex items-center justify-between">
                    <span className="badge-gold">
                      {count > 0 ? `${count} מוצרים` : 'קולקציה שלמה'}
                    </span>
                    <span
                      className="text-[#c9a55a] text-xs font-bold uppercase tracking-widest opacity-0 group-hover:opacity-100 translate-x-2 group-hover:translate-x-0 transition-all duration-300"
                    >
                      גלה &larr;
                    </span>
                  </div>
                </div>

                {/* Bottom gold line */}
                <div className="absolute bottom-0 inset-x-0 h-px bg-gradient-to-r from-transparent via-[rgba(201,165,90,0.3)] to-transparent opacity-0 group-hover:opacity-100 transition-opacity duration-500" />
              </Link>
            )
          })}
        </div>

        {/* Bottom note */}
        <div className="mt-16 text-center">
          <div className="divider-subtle mb-8" />
          <p className="text-[#f2eddf]/20 text-xs uppercase tracking-widest">
            כל מוצר הוא 100% מקורי ומגיע עם אחריות יצרן
          </p>
        </div>
      </section>
    </div>
  )
}
