'use client'

import { createContext, useContext, useReducer, useEffect, useState } from 'react'
import type { Product } from '@/lib/types'

type WishlistState = {
  items: Product[]
}

type WishlistAction =
  | { type: 'ADD_ITEM'; product: Product }
  | { type: 'REMOVE_ITEM'; productId: string }
  | { type: 'CLEAR' }

type WishlistContextType = {
  items: Product[]
  addItem: (product: Product) => void
  removeItem: (productId: string) => void
  isWishlisted: (productId: string) => boolean
  toggle: (product: Product) => void
  count: number
}

const WishlistContext = createContext<WishlistContextType | undefined>(undefined)

function wishlistReducer(state: WishlistState, action: WishlistAction): WishlistState {
  switch (action.type) {
    case 'ADD_ITEM':
      if (state.items.find(i => i.id === action.product.id)) return state
      return { items: [...state.items, action.product] }
    case 'REMOVE_ITEM':
      return { items: state.items.filter(i => i.id !== action.productId) }
    case 'CLEAR':
      return { items: [] }
    default:
      return state
  }
}

export function WishlistProvider({ children }: { children: React.ReactNode }) {
  const [state, dispatch] = useReducer(wishlistReducer, { items: [] })
  const [mounted, setMounted] = useState(false)

  useEffect(() => {
    setMounted(true)
    try {
      const saved = localStorage.getItem('nexor-wishlist')
      if (saved) {
        const parsed = JSON.parse(saved) as Product[]
        parsed.forEach(p => dispatch({ type: 'ADD_ITEM', product: p }))
      }
    } catch {}
  }, [])

  useEffect(() => {
    if (mounted) localStorage.setItem('nexor-wishlist', JSON.stringify(state.items))
  }, [state.items, mounted])

  return (
    <WishlistContext.Provider
      value={{
        items: state.items,
        addItem: product => dispatch({ type: 'ADD_ITEM', product }),
        removeItem: productId => dispatch({ type: 'REMOVE_ITEM', productId }),
        isWishlisted: productId => state.items.some(i => i.id === productId),
        toggle: product => {
          if (state.items.find(i => i.id === product.id)) {
            dispatch({ type: 'REMOVE_ITEM', productId: product.id })
          } else {
            dispatch({ type: 'ADD_ITEM', product })
          }
        },
        count: state.items.length,
      }}
    >
      {children}
    </WishlistContext.Provider>
  )
}

export function useWishlist() {
  const ctx = useContext(WishlistContext)
  if (!ctx) throw new Error('useWishlist must be used within WishlistProvider')
  return ctx
}
