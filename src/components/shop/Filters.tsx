'use client'

import { motion, AnimatePresence } from 'framer-motion'
import { X, ChevronDown, SlidersHorizontal } from 'lucide-react'
import { useState } from 'react'
import { categories, brands } from '@/lib/data'

type FilterState = {
  categories: string[]
  brands: string[]
  priceRange: [number, number]
  playerLevels: string[]
  onSale: boolean
  isNew: boolean
}

type Props = {
  filters: FilterState
  onChange: (filters: FilterState) => void
  sortBy: string
  onSortChange: (sort: string) => void
  totalProducts: number
  mobileOpen: boolean
  onMobileClose: () => void
}

const playerLevelLabels: Record<string, string> = {
  beginner: 'מתחיל',
  intermediate: 'בינוני',
  advanced: 'מתקדם',
  professional: 'מקצועי',
}

const playerLevels = ['beginner', 'intermediate', 'advanced', 'professional']

function FilterSection({ title, children, defaultOpen = true }: { title: string; children: React.ReactNode; defaultOpen?: boolean }) {
  const [open, setOpen] = useState(defaultOpen)
  return (
    <div className="border-b border-white/5 pb-4 mb-4">
      <button
        onClick={() => setOpen(!open)}
        className="flex items-center justify-between w-full py-2 text-white/70 text-xs font-bold uppercase tracking-widest hover:text-white transition-colors"
      >
        {title}
        <ChevronDown className={`w-4 h-4 transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>
      <AnimatePresence>
        {open && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.2 }}
            className="overflow-hidden mt-3"
          >
            {children}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}

function FilterCheckbox({
  label,
  checked,
  onChange,
  count,
}: {
  label: string
  checked: boolean
  onChange: (checked: boolean) => void
  count?: number
}) {
  return (
    <label className="flex items-center gap-3 py-1.5 cursor-pointer group">
      <div
        className={`w-4 h-4 border flex-shrink-0 flex items-center justify-center transition-all duration-200 ${
          checked ? 'bg-[#b5f72e] border-[#b5f72e]' : 'border-white/20 group-hover:border-white/40'
        }`}
        onClick={() => onChange(!checked)}
      >
        {checked && (
          <svg className="w-2.5 h-2.5 text-black" viewBox="0 0 10 10" fill="none">
            <path d="M1.5 5L4 7.5L8.5 2.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round"/>
          </svg>
        )}
      </div>
      <span className={`text-sm transition-colors ${checked ? 'text-white' : 'text-white/50 group-hover:text-white/70'}`}>
        {label}
      </span>
      {count !== undefined && (
        <span className="mr-auto text-white/20 text-xs ltr-text">{count}</span>
      )}
    </label>
  )
}

export default function Filters({ filters, onChange, sortBy, onSortChange, totalProducts, mobileOpen, onMobileClose }: Props) {
  const toggleCategory = (id: string) => {
    onChange({
      ...filters,
      categories: filters.categories.includes(id)
        ? filters.categories.filter(c => c !== id)
        : [...filters.categories, id],
    })
  }

  const toggleBrand = (brand: string) => {
    onChange({
      ...filters,
      brands: filters.brands.includes(brand)
        ? filters.brands.filter(b => b !== brand)
        : [...filters.brands, brand],
    })
  }

  const togglePlayerLevel = (level: string) => {
    onChange({
      ...filters,
      playerLevels: filters.playerLevels.includes(level)
        ? filters.playerLevels.filter(l => l !== level)
        : [...filters.playerLevels, level],
    })
  }

  const clearAll = () => {
    onChange({ categories: [], brands: [], priceRange: [0, 5000], playerLevels: [], onSale: false, isNew: false })
  }

  const activeCount = filters.categories.length + filters.brands.length + filters.playerLevels.length +
    (filters.onSale ? 1 : 0) + (filters.isNew ? 1 : 0)

  const filterContent = (
    <div>
      <div className="flex items-center justify-between mb-6 pb-4 border-b border-white/5">
        <div className="flex items-center gap-2">
          <SlidersHorizontal className="w-4 h-4 text-[#b5f72e]" />
          <span className="text-white text-sm font-bold uppercase tracking-widest">סינון</span>
          {activeCount > 0 && (
            <span className="px-1.5 py-0.5 bg-[#b5f72e] text-black text-[10px] font-bold ltr-text">{activeCount}</span>
          )}
        </div>
        <div className="flex items-center gap-3">
          {activeCount > 0 && (
            <button onClick={clearAll} className="text-white/30 text-xs uppercase tracking-wider hover:text-white transition-colors">
              נקה
            </button>
          )}
          <button onClick={onMobileClose} className="lg:hidden text-white/40 hover:text-white transition-colors">
            <X className="w-5 h-5" />
          </button>
        </div>
      </div>

      <p className="text-white/30 text-xs mb-6">{totalProducts} מוצרים</p>

      <FilterSection title="קטגוריה">
        {categories.map(cat => (
          <FilterCheckbox
            key={cat.id}
            label={cat.label}
            checked={filters.categories.includes(cat.id)}
            onChange={() => toggleCategory(cat.id)}
            count={cat.count}
          />
        ))}
      </FilterSection>

      <FilterSection title="רמת שחקן">
        {playerLevels.map(level => (
          <FilterCheckbox
            key={level}
            label={playerLevelLabels[level]}
            checked={filters.playerLevels.includes(level)}
            onChange={() => togglePlayerLevel(level)}
          />
        ))}
      </FilterSection>

      <FilterSection title="מותג" defaultOpen={false}>
        {brands.slice(1).map(brand => (
          <FilterCheckbox
            key={brand}
            label={brand}
            checked={filters.brands.includes(brand)}
            onChange={() => toggleBrand(brand)}
          />
        ))}
      </FilterSection>

      <FilterSection title="מחיר" defaultOpen={false}>
        <div className="space-y-2">
          {[
            { label: 'עד ₪200', min: 0, max: 200 },
            { label: '₪200 – ₪500', min: 200, max: 500 },
            { label: '₪500 – ₪1,000', min: 500, max: 1000 },
            { label: 'מעל ₪1,000', min: 1000, max: 9999 },
          ].map(range => (
            <FilterCheckbox
              key={range.label}
              label={range.label}
              checked={filters.priceRange[0] === range.min && filters.priceRange[1] === range.max}
              onChange={checked => {
                onChange({ ...filters, priceRange: checked ? [range.min, range.max] : [0, 5000] })
              }}
            />
          ))}
        </div>
      </FilterSection>

      <FilterSection title="מיוחד">
        <FilterCheckbox
          label="במבצע"
          checked={filters.onSale}
          onChange={checked => onChange({ ...filters, onSale: checked })}
        />
        <FilterCheckbox
          label="חדש"
          checked={filters.isNew}
          onChange={checked => onChange({ ...filters, isNew: checked })}
        />
      </FilterSection>
    </div>
  )

  return (
    <>
      {/* Desktop Sidebar */}
      <aside className="hidden lg:block w-64 flex-shrink-0">
        <div className="sticky top-24 bg-[#0c0c0c] border border-white/5 p-5">
          {filterContent}
        </div>
      </aside>

      {/* Mobile Drawer — slides from right in RTL */}
      <AnimatePresence>
        {mobileOpen && (
          <>
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              onClick={onMobileClose}
              className="fixed inset-0 bg-black/70 z-40 lg:hidden"
            />
            <motion.div
              initial={{ x: '100%' }}
              animate={{ x: 0 }}
              exit={{ x: '100%' }}
              transition={{ type: 'spring', damping: 30, stiffness: 300 }}
              className="fixed right-0 top-0 bottom-0 w-80 bg-[#0a0a0a] border-l border-white/5 z-50 overflow-y-auto p-5 pt-8 lg:hidden"
            >
              {filterContent}
            </motion.div>
          </>
        )}
      </AnimatePresence>
    </>
  )
}
