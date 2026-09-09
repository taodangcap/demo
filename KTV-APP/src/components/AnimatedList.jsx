/**
 * AnimatedList - Staggered list animations for search results, playlists, etc.
 * Provides smooth entrance animations for list items
 */
import { motion } from 'framer-motion'

const containerVariants = {
  hidden: { opacity: 0 },
  visible: {
    opacity: 1,
    transition: {
      staggerChildren: 0.04,
      delayChildren: 0.05,
    },
  },
}

const itemVariants = {
  hidden: {
    opacity: 0,
    y: 12,
    scale: 0.97,
  },
  visible: {
    opacity: 1,
    y: 0,
    scale: 1,
    transition: {
      duration: 0.25,
      ease: [0.25, 0.46, 0.45, 0.94],
    },
  },
}

export function AnimatedList({ children, className = '', as = 'div' }) {
  const Component = motion[as] || motion.div
  return (
    <Component
      variants={containerVariants}
      initial="hidden"
      animate="visible"
      className={className}
    >
      {children}
    </Component>
  )
}

export function AnimatedListItem({ children, className = '', onClick, ...props }) {
  return (
    <motion.div
      variants={itemVariants}
      className={className}
      onClick={onClick}
      whileHover={{ scale: 1.01, transition: { duration: 0.15 } }}
      whileTap={{ scale: 0.98, transition: { duration: 0.1 } }}
      {...props}
    >
      {children}
    </motion.div>
  )
}

export default AnimatedList
