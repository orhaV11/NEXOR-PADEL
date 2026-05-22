'use client'

import { motion } from 'framer-motion'
import { clsx } from 'clsx'
import Link from 'next/link'
import type { ReactNode } from 'react'

type ButtonVariant = 'primary' | 'outline' | 'ghost' | 'danger'
type ButtonSize = 'sm' | 'md' | 'lg'

type ButtonProps = {
  children: ReactNode
  variant?: ButtonVariant
  size?: ButtonSize
  href?: string
  onClick?: () => void
  disabled?: boolean
  className?: string
  fullWidth?: boolean
  type?: 'button' | 'submit' | 'reset'
  icon?: ReactNode
}

const variants: Record<ButtonVariant, string> = {
  primary: 'btn-gold',
  outline: 'btn-outline',
  ghost: 'btn-ghost',
  danger: 'bg-red-500/10 border border-red-500/30 text-red-400 hover:bg-red-500/20',
}

const sizes: Record<ButtonSize, string> = {
  sm: 'px-4 py-2 text-xs tracking-widest',
  md: 'px-7 py-3.5 text-sm tracking-widest',
  lg: 'px-9 py-4 text-base tracking-widest',
}

export default function Button({
  children,
  variant = 'primary',
  size = 'md',
  href,
  onClick,
  disabled,
  className,
  fullWidth,
  type = 'button',
  icon,
}: ButtonProps) {
  const classes = clsx(
    'inline-flex items-center justify-center gap-2 font-medium uppercase transition-all duration-300 active:scale-95 select-none',
    variants[variant],
    sizes[size],
    fullWidth && 'w-full',
    disabled && 'opacity-40 pointer-events-none',
    className
  )

  const content = (
    <>
      {icon && <span>{icon}</span>}
      {children}
    </>
  )

  if (href) {
    return (
      <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.97 }}>
        <Link href={href} className={classes}>
          {content}
        </Link>
      </motion.div>
    )
  }

  return (
    <motion.button
      type={type}
      onClick={onClick}
      disabled={disabled}
      className={classes}
      whileHover={{ scale: 1.02 }}
      whileTap={{ scale: 0.97 }}
    >
      {content}
    </motion.button>
  )
}
