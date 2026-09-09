/**
 * SkeletonLoader - Loading placeholder animations
 * Shows smooth shimmer effect while content is loading
 */
import { motion } from 'framer-motion'

export function SkeletonPulse({ className = '' }) {
  return (
    <motion.div
      className={`bg-slate-800/50 rounded-lg ${className}`}
      animate={{ opacity: [0.4, 0.7, 0.4] }}
      transition={{ duration: 1.5, repeat: Infinity, ease: 'easeInOut' }}
    />
  )
}

export function VideoCardSkeleton() {
  return (
    <div className="flex flex-col bg-[#0B1224] border border-white/5 rounded-xl overflow-hidden">
      <SkeletonPulse className="w-full aspect-video" />
      <div className="p-2 space-y-2">
        <SkeletonPulse className="h-3 w-full" />
        <SkeletonPulse className="h-3 w-3/4" />
        <div className="flex justify-between items-center pt-1">
          <SkeletonPulse className="h-2.5 w-16" />
          <SkeletonPulse className="h-6 w-14 rounded-lg" />
        </div>
      </div>
    </div>
  )
}

export function SearchResultsSkeleton({ count = 6, columns = 2 }) {
  return (
    <div className={`grid grid-cols-${columns} gap-2.5`}>
      {Array.from({ length: count }).map((_, i) => (
        <VideoCardSkeleton key={i} />
      ))}
    </div>
  )
}

export function PlaylistItemSkeleton() {
  return (
    <div className="flex gap-3 p-2 bg-[#0B1224] border border-white/5 rounded-xl items-center">
      <SkeletonPulse className="w-5 h-5 rounded" />
      <SkeletonPulse className="w-16 aspect-video rounded-lg" />
      <div className="flex-1 space-y-1.5">
        <SkeletonPulse className="h-3 w-full" />
        <SkeletonPulse className="h-2 w-1/2" />
      </div>
      <div className="flex gap-1">
        <SkeletonPulse className="w-8 h-8 rounded-lg" />
        <SkeletonPulse className="w-8 h-8 rounded-lg" />
      </div>
    </div>
  )
}

export default SkeletonLoader
function SkeletonLoader({ type = 'search', count = 6 }) {
  if (type === 'playlist') {
    return (
      <div className="space-y-2">
        {Array.from({ length: count }).map((_, i) => (
          <PlaylistItemSkeleton key={i} />
        ))}
      </div>
    )
  }
  return <SearchResultsSkeleton count={count} />
}
