'use client'

import { useState, useMemo } from 'react'
import { motion } from 'framer-motion'
import { Search, SlidersHorizontal, Grid2X2, Grid3X3, X } from 'lucide-react'
import { products } from '@/lib/data'
import ProductCard from '@/components/shop/ProductCard'
import Filters from '@/components/shop/Filters'

type FilterState = {
  categories: string[]
  brands: string[]
  priceRange: [number, number]
  playerLevels: string[]
  onSale: boolean
  isNew: boolean
}

const sortOptions = [
  { value: 'featured', label: 'מומלץ' },
  { value: 'newest', label: 'חדש ביותר' },
  { value: 'price-asc', label: 'מחיר: נמוך לגבוה' },
  { value: 'price-desc', label: 'מחיר: גבוה לנמוך' },
  { value: 'rating', label: 'דירוג גבוה' },
]

export default function ShopPage() {
  const [searchQuery, setSearchQuery] = useState('')
  const [sortBy, setSortBy] = useState('featured')
  const [gridSize, setGridSize] = useState<3 | 4>(4)
  const [mobileFiltersOpen, setMobileFiltersOpen] = useState(false)
  const [filters, setFilters] = useState<FilterState>({
    categories: [],
    brands: [],
    priceRange: [0, 5000],
    playerLevels: [],
    onSale: false,
    isNew: false,
  })

  const filtered = useMemo(() => {
    let result = [...products]

    if (searchQuery) {
      const q = searchQuery.toLowerCase()
      result = result.filter(
        p => p.name.toLowerCase().includes(q) || p.brand.toLowerCase().includes(q) || p.category.includes(q)
      )
    }

    if (filters.categories.length > 0) {
      result = result.filter(p => filters.categories.includes(p.category))
    }

    if (filters.brands.length > 0) {
      result = result.filter(p => filters.brands.includes(p.brand))
    }

    if (filters.playerLevels.length > 0) {
      result = result.filter(p => p.playerLevel && filters.playerLevels.includes(p.playerLevel))
    }

    if (filters.priceRange[1] < 5000 || filters.priceRange[0] > 0) {
      result = result.filter(p => {
        const price = p.salePrice ?? p.price
        return price >= filters.priceRange[0] && price <= filters.priceRange[1]
      })
    }

    if (filters.onSale) result = result.filter(p => !!p.salePrice)
    if (filters.isNew) result = result.filter(p => p.isNew)

    switch (sortBy) {
      case 'newest':
        result = result.filter(p => p.isNew).concat(result.filter(p => !p.isNew))
        break
      case 'price-asc':
        result.sort((a, b) => (a.salePrice ?? a.price) - (b.salePrice ?? b.price))
        break
      case 'price-desc':
        result.sort((a, b) => (b.salePrice ?? b.price) - (a.salePrice ?? a.price))
        break
      case 'rating':
        result.sort((a, b) => b.rating - a.rating)
        break
      default:
        result = result.filter(p => p.isFeatured || p.isBestSeller).concat(result.filter(p => !p.isFeatured && !p.isBestSeller))
    }

    return result
  }, [searchQuery, filters, sortBy])

  const activeFilterCount = filters.categories.length + filters.brands.length + filters.playerLevels.length +
    (filters.onSale ? 1 : 0) + (filters.isNew ? 1 : 0)

  return (
    <div className="min-h-screen pt-20">
      {/* Shop Header */}
      <div className="bg-[#0d0d10] border-b border-[rgba(201,165,90,0.08)] py-12 px-5">
        <div className="max-w-7xl mx-auto text-center">
          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.7, ease: [0.16, 1, 0.3, 1] }}
          >
            <div className="section-label mx-auto mb-4">
              ציוד פרימיום
            </div>
            <h1 className="font-display text-5xl md:text-7xl text-[#f2eddf] tracking-wide uppercase mb-4">
              החנות
            </h1>
            <p className="text-[rgba(242,237,223,0.4)] max-w-lg mx-auto text-sm leading-relaxed">
              ציוד פאדל מאוצר. ביצועים רציניים. מהמשחק הראשון שלך עד הרמה הבאה.
            </p>
          </motion.div>
        </div>
      </div>

      {/* Toolbar */}
      <div className="sticky top-16 z-30 bg-[#08080a]/95 backdrop-blur-xl border-b border-[rgba(201,165,90,0.08)]">
        <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 py-3 flex items-center gap-3">
          {/* Search */}
          <div className="relative flex-1 max-w-xs">
            <Search className="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 text-[rgba(242,237,223,0.3)]" />
            <input
              type="text"
              value={searchQuery}
              onChange={e => setSearchQuery(e.target.value)}
              placeholder="חיפוש מוצרים..."
              className="luxury-input pr-9 pl-8 py-2 text-sm"
            />
            {searchQuery && (
              <button onClick={() => setSearchQuery('')} className="absolute left-2 top-1/2 -translate-y-1/2">
                <X className="w-3.5 h-3.5 text-[rgba(242,237,223,0.3)] hover:text-[#f2eddf] transition-colors" />
              </button>
            )}
          </div>

          {/* Mobile filter button */}
          <button
            onClick={() => setMobileFiltersOpen(true)}
            className="lg:hidden flex items-center gap-2 px-3 py-2 bg-[rgba(255,255,255,0.03)] border border-[rgba(201,165,90,0.1)] text-[rgba(242,237,223,0.6)] text-xs uppercase tracking-wider hover:text-[#f2eddf] transition-colors"
          >
            <SlidersHorizontal className="w-4 h-4" />
            סינון
            {activeFilterCount > 0 && (
              <span className="px-1.5 bg-[#c9a55a] text-[#08080a] text-[10px] font-bold ltr">{activeFilterCount}</span>
            )}
          </button>

          {/* Sort */}
          <div className="me-auto flex items-center gap-3">
            <select
              value={sortBy}
              onChange={e => setSortBy(e.target.value)}
              className="bg-[rgba(255,255,255,0.03)] border border-[rgba(201,165,90,0.1)] text-[rgba(242,237,223,0.6)] text-xs uppercase tracking-wider px-3 py-2 focus:outline-none focus:border-[rgba(201,165,90,0.38)] transition-colors cursor-pointer"
            >
              {sortOptions.map(opt => (
                <option key={opt.value} value={opt.value} className="bg-[#0d0d10] text-[#f2eddf]">
                  {opt.label}
                </option>
              ))}
            </select>

            {/* Grid Toggle */}
            <div className="hidden md:flex items-center border border-[rgba(201,165,90,0.1)]">
              <button
                onClick={() => setGridSize(3)}
                className={`p-2 transition-colors ${gridSize === 3 ? 'bg-[rgba(201,165,90,0.1)] text-[#c9a55a]' : 'text-[rgba(242,237,223,0.3)] hover:text-[#f2eddf]'}`}
              >
                <Grid2X2 className="w-4 h-4" />
              </button>
              <button
                onClick={() => setGridSize(4)}
                className={`p-2 transition-colors ${gridSize === 4 ? 'bg-[rgba(201,165,90,0.1)] text-[#c9a55a]' : 'text-[rgba(242,237,223,0.3)] hover:text-[#f2eddf]'}`}
              >
                <Grid3X3 className="w-4 h-4" />
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Main Content */}
      <div className="max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 py-8 flex gap-8">
        <Filters
          filters={filters}
          onChange={setFilters}
          sortBy={sortBy}
          onSortChange={setSortBy}
          totalProducts={filtered.length}
          mobileOpen={mobileFiltersOpen}
          onMobileClose={() => setMobileFiltersOpen(false)}
        />

        {/* Product Grid */}
        <div className="flex-1 min-w-0">
          <div className="flex items-center justify-between mb-6">
            <p className="text-[rgba(242,237,223,0.3)] text-sm">
              {filtered.length} מוצרים
              {searchQuery && <span> עבור &quot;{searchQuery}&quot;</span>}
            </p>
            {activeFilterCount > 0 && (
              <button
                onClick={() => setFilters({ categories: [], brands: [], priceRange: [0, 5000], playerLevels: [], onSale: false, isNew: false })}
                className="text-[#c9a55a] text-xs uppercase tracking-wider hover:text-[#f2eddf] transition-colors flex items-center gap-1"
              >
                <X className="w-3 h-3" /> נקה סינון
              </button>
            )}
          </div>

          {filtered.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-24 text-center">
              <p className="text-[rgba(242,237,223,0.2)] text-4xl font-display tracking-wider mb-4">אין תוצאות</p>
              <p className="text-[rgba(242,237,223,0.3)] text-sm mb-6">נסה לשנות את החיפוש או הסינון</p>
              <button
                onClick={() => { setSearchQuery(''); setFilters({ categories: [], brands: [], priceRange: [0, 5000], playerLevels: [], onSale: false, isNew: false }) }}
                className="btn-gold"
              >
                אפס הכל
              </button>
            </div>
          ) : (
            <div className={`grid gap-4 ${
              gridSize === 3
                ? 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3'
                : 'grid-cols-2 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4'
            }`}>
              {filtered.map((product, i) => (
                <ProductCard key={product.id} product={product} index={i} />
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
