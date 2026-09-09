/**
 * FadeIn - Simple fade-in animation wrapper
 * Use for sections, cards, or any element that should appear smoothly
 */
import { motion } from 'framer-motion'

export function FadeIn({
  children,
  className = '',
  delay = 0,
  duration = 0.4,
  direction = 'up', // 'up' | 'down' | 'left' | 'right' | 'none'
  distance = 20,
  once = true,
  ...props
}) {
  const directionMap = {
    up: { y: distance },
    down: { y: -distance },
    left: { x: distance },
    right: { x: -distance },
    none: {},
  }

  return (
    <motion.div
      initial={{ opacity: 0, ...directionMap[direction] }}
      whileInView={{ opacity: 1, x: 0, y: 0 }}
      viewport={{ once, margin: '-50px' }}
      transition={{
        duration,
        delay,
        ease: [0.25, 0.46, 0.45, 0.94],
      }}
      className={className}
      {...props}
    >
      {children}
    </motion.div>
  )
}

export default FadeIn
