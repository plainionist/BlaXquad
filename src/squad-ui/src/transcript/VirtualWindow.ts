import type { TranscriptIndex } from './TranscriptIndex'

export interface WindowRange {
  start: number
  end: number
}

export interface ViewportWindowGeometry {
  visibleTop: number
  visibleBottom: number
}

export interface VisibleAnchorGeometry {
  entryIndex: number
  rowTop: number
  rowBottom: number
  viewportHeight: number
}

export function clampWindow(
  current: WindowRange,
  renderableCount: number,
  following: boolean,
): WindowRange {
  if (renderableCount === 0)
    return { start: 0, end: 0 }
  const previousSize = Math.max(1, current.end - current.start)
  if (following || current.start >= renderableCount) {
    const end = renderableCount
    return { start: Math.max(0, end - previousSize), end }
  }
  const start = Math.max(0, Math.min(current.start, renderableCount - 1))
  return {
    start,
    end: Math.max(start + 1, Math.min(current.end, renderableCount)),
  }
}

export function forOffset(
  index: TranscriptIndex,
  geometry: ViewportWindowGeometry,
  overscan: number,
): WindowRange {
  return {
    start: Math.max(0, index.rankAtOffset(geometry.visibleTop) - overscan),
    end: Math.min(
      index.renderableCount,
      index.rankAtOffset(geometry.visibleBottom) + overscan + 1),
  }
}

export function aroundEntry(
  index: TranscriptIndex,
  entryIndex: number,
  viewportHeight: number,
  overscan: number,
): WindowRange | undefined {
  const position = index.positionOf(entryIndex)
  if (position == null) return undefined
  const rank = index.rankForPosition(position)
  let coveredHeight = 0
  let end = rank
  while (end < index.renderableCount && coveredHeight < viewportHeight) {
    coveredHeight += index.heightAt(index.positionForRank(end))
    end++
  }
  return {
    start: Math.max(0, rank - overscan),
    end: Math.min(index.renderableCount, end + overscan),
  }
}

export function forVisibleAnchor(
  index: TranscriptIndex,
  geometry: VisibleAnchorGeometry,
  overscan: number,
): WindowRange | undefined {
  const position = index.positionOf(geometry.entryIndex)
  if (position == null
    || geometry.rowBottom < 0
    || geometry.rowTop > geometry.viewportHeight)
    return undefined
  const rank = index.rankForPosition(position)
  let renderedBottom = geometry.rowBottom
  let end = rank + 1
  while (end < index.renderableCount && renderedBottom < geometry.viewportHeight) {
    renderedBottom += index.heightAt(index.positionForRank(end))
    end++
  }
  return {
    start: Math.max(0, rank - overscan),
    end: Math.min(index.renderableCount, end + overscan),
  }
}
