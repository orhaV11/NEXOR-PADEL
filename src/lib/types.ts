export type ProductCategory = 'rackets' | 'balls' | 'grips' | 'bags' | 'shoes' | 'apparel' | 'accessories'

export type PlayerLevel = 'beginner' | 'intermediate' | 'advanced' | 'professional'

export type ProductImage = {
  gradient: string
  accent: string
}

export type Product = {
  id: string
  slug: string
  name: string
  brand: string
  category: ProductCategory
  price: number
  salePrice?: number
  isNew?: boolean
  isBestSeller?: boolean
  isFeatured?: boolean
  description: string
  longDescription?: string
  features: string[]
  playerLevel?: PlayerLevel
  weight?: string
  balance?: string
  shape?: string
  material?: string
  color?: string
  size?: string
  image: ProductImage
  rating: number
  reviewCount: number
  inStock: boolean
  stockCount?: number
}

export type CartItem = {
  product: Product
  quantity: number
  size?: string
}

export type WishlistItem = {
  product: Product
  addedAt: Date
}

export type Review = {
  id: string
  author: string
  avatar?: string
  rating: number
  title: string
  body: string
  date: string
  verified: boolean
  product?: string
}

export type FAQ = {
  question: string
  answer: string
}
