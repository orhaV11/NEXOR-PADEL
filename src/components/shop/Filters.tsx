'use client'

import { motion, AnimatePresence } from 'framer-motion'
import { X, ChevronDown, SlidersHorizontal } from 'lucide-react'
import { useState } from 'react'
import { categories, brands, playerLevelLabels } from '@/lib/data'

export type FilterState = {
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

const playerLevels = ['beginner', 'intermediate', 'advanced', 'professional']

function FilterSection({
  title,
  children,
  defaultOpen = true,
}: {
  title: string
  children: React.ReactNode
  defaultOpen?: boolean
}) {
  const [open, setOpen] = useState(defaultOpen)
  return (
    <div className="border-b pb-4 mb-4" style={{ borderColor: 'rgba(201,165,90,0.08)' }}>
      <button
        onClick={() => setOpen(!open)}
        className="flex items-center justify-between w-full py-2 text-xs font-bold uppercase tracking-widest transition-colors duration-200"
        style={{ color: open ? '#e2c890' : 'rgba(242,237,223,0.45)' }}
      >
        {title}
        <ChevronDown
          className="w-3.5 h-3.5 transition-transform duration-300"
          style={{ transform: open ? 'rotate(180deg)' : 'rotate(0deg)', color: '#c9a55a' }}
        />
      </button>
      <AnimatePresence>
        {open && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.22, ease: [0.16, 1, 0.3, 1] }}
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
        onClick={() => onChange(!checked)}
        className="w-4 h-4 border flex-shrink-0 flex items-center justify-center transition-all duration-200"
        style={{
          background: checked ? 'rgba(201,165,90,0.15)' : 'transparent',
          borderColor: checked ? '#c9a55a' : 'rgba(242,237,223,0.18)',
        }}
      >
        {checked && (
          <svg className="w-2.5 h-2.5" viewBox="0 0 10 10" fill="none">
            <path d="M1.5 5L4 7.5L8.5 2.5" stroke="#c9a55a" strokeWidth="1.5" strokeLinecap="round" />
          </svg>
        )}
      </div>
      <span
        className="text-sm transition-colors duration-200"
        style={{ color: checked ? '#f2eddf' : 'rgba(242,237,223,0.45)' }}
      >
        {label}
      </span>
      {count !== undefined && (
        <span className="mr-auto text-[10px] ltr" style={{ color: 'rgba(242,237,223,0.2)' }}>
          {count}
        </span>
      )}
    </label>
  )
}

export default function Filters({
  filters,
  onChange,
  totalProducts,
  mobileOpen,
  onMobileClose,
}: Props) {
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
    onChange({
      categories: [],
      brands: [],
      priceRange: [0, 5000],
      playerLevels: [],
      onSale: false,
      isNew: false,
    })
  }

  const activeCount =
    filters.categories.length +
    filters.brands.length +
    filters.playerLevels.length +
    (filters.onSale ? 1 : 0) +
    (filters.isNew ? 1 : 0)

  const filterContent = (
    <div>
      {/* Header */}
      <div
        className="flex items-center justify-between mb-6 pb-4"
        style={{ borderBottom: '1px solid rgba(201,165,90,0.1)' }}
      >
        <div className="flex items-center gap-2">
          <SlidersHorizontal className="w-4 h-4" style={{ color: '#c9a55a' }} />
          <span
            className="text-sm font-bold uppercase tracking-widest"
            style={{ color: '#f2eddf' }}
          >
            סינון
          </span>
          {activeCount > 0 && (
            <span
              className="px-1.5 py-0.5 text-[10px] font-bold ltr"
              style={{ background: '#c9a55a', color: '#08080a' }}
            >
              {activeCount}
            </span>
          )}
        </div>
        <div className="flex items-center gap-3">
          {activeCount > 0 && (
            <button
              onClick={clearAll}
              className="text-xs uppercase tracking-wider transition-colors duration-200"
              style={{ color: 'rgba(201,165,90,0.5)' }}
              onMouseEnter={e => (e.currentTarget.style.color = '#c9a55a')}
              onMouseLeave={e => (e.currentTarget.style.color = 'rgba(201,165,90,0.5)')}
            >
              נקה
            </button>
          )}
          <button
            onClick={onMobileClose}
            className="lg:hidden transition-colors duration-200"
            style={{ color: 'rgba(242,237,223,0.3)' }}
            onMouseEnter={e => (e.currentTarget.style.color = '#f2eddf')}
            onMouseLeave={e => (e.currentTarget.style.color = 'rgba(242,237,223,0.3)')}
          >
            <X className="w-5 h-5" />
          </button>
        </div>
      </div>

      <p className="text-xs mb-6" style={{ color: 'rgba(242,237,223,0.25)' }}>
        {totalProducts} מוצרים
      </p>

      {/* קטגוריה */}
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

      {/* רמת שחקן */}
      <FilterSection title="רמת שחקן">
        {playerLevels.map(level => (
          <FilterCheckbox
            key={level}
            label={playerLevelLabels[level] ?? level}
            checked={filters.playerLevels.includes(level)}
            onChange={() => togglePlayerLevel(level)}
          />
        ))}
      </FilterSection>

      {/* מותג */}
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

      {/* מחיר */}
      <FilterSection title="מחיר" defaultOpen={false}>
        <div className="space-y-1">
          {[
            { label: 'עד ₪200', min: 0, max: 200 },
            { label: '₪200 – ₪500', min: 200, max: 500 },
            { label: '₪500 – ₪1,000', min: 500, max: 1000 },
            { label: 'מעל ₪1,000', min: 1000, max: 9999 },
          ].map(range => (
            <FilterCheckbox
              key={range.label}
              label={range.label}
              checked={
                filters.priceRange[0] === range.min && filters.priceRange[1] === range.max
              }
              onChange={checked => {
                onChange({
                  ...filters,
                  priceRange: checked ? [range.min, range.max] : [0, 5000],
                })
              }}
            />
          ))}
        </div>
      </FilterSection>

      {/* מיוחד */}
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
        <div
          className="sticky top-24 p-5"
          style={{
            background: '#0d0d10',
            border: '1px solid rgba(201,165,90,0.1)',
          }}
        >
          {filterContent}
        </div>
      </aside>

      {/* Mobile Drawer — slides from right (RTL) */}
      <AnimatePresence>
        {mobileOpen && (
          <>
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              onClick={onMobileClose}
              className="fixed inset-0 z-40 lg:hidden"
              style={{ background: 'rgba(0,0,0,0.75)' }}
            />
            <motion.div
              initial={{ x: '100%' }}
              animate={{ x: 0 }}
              exit={{ x: '100%' }}
              transition={{ type: 'spring', damping: 30, stiffness: 280 }}
              className="fixed right-0 top-0 bottom-0 w-80 z-50 overflow-y-auto p-5 pt-8 lg:hidden"
              style={{
                background: '#0d0d10',
                borderLeft: '1px solid rgba(201,165,90,0.12)',
              }}
            >
              {filterContent}
            </motion.div>
          </>
        )}
      </AnimatePresence>
    </>
  )
}
