import { computed, nextTick, ref, type Ref } from 'vue'
import type { ScrollAnchor } from '../transcript/ReadingAnchor'
import type { TranscriptIndex } from '../transcript/TranscriptIndex'
import {
  aroundEntry,
  clampWindow as clampWindowRange,
  forOffset,
  forVisibleAnchor,
  type WindowRange,
} from '../transcript/VirtualWindow'
import type {
  ViewportGeometryObserver,
} from '../transcript/ViewportGeometryObserver'

interface MeasuredRow {
  top: number
  bottom: number
}

interface UseVirtualWindowOptions {
  index: TranscriptIndex
  viewport: Readonly<Ref<HTMLElement | null>>
  content: Readonly<Ref<HTMLElement | null>>
  measuredRowFor: (
    entryIndex: number,
    viewport: HTMLElement,
  ) => MeasuredRow | undefined
  sweepMeasurements: () => void
  layoutAnchor: () => ScrollAnchor | undefined
  retainedAnchor: () => ScrollAnchor | undefined
  scrollGeometry: ViewportGeometryObserver
  topCompensation: () => number
  bottomCompensation: () => number
  overscan: number
}

export function useVirtualWindow(options: UseVirtualWindowOptions) {
  const windowStart = ref(0)
  const windowEnd = ref(0)
  let scheduledFrame: number | undefined
  let disposed = false

  const windowRowCount = computed(() => windowEnd.value - windowStart.value)
  const visibleEntries = computed(() => {
    void options.index.revision.value
    const entries = []
    for (let rank = windowStart.value; rank < windowEnd.value; rank++)
      entries.push(options.index.projectAt(
        options.index.positionForRank(rank)))
    return entries
  })

  const topSpacerHeight = computed(() => {
    void options.index.revision.value
    const position = windowStart.value < options.index.renderableCount
      ? options.index.positionForRank(windowStart.value)
      : options.index.length
    return options.index.heightBefore(position)
      + options.topCompensation()
  })

  const bottomSpacerHeight = computed(() => {
    void options.index.revision.value
    const position = windowEnd.value < options.index.renderableCount
      ? options.index.positionForRank(windowEnd.value)
      : options.index.length
    return options.index.totalHeight()
      - options.index.heightBefore(position)
      + options.bottomCompensation()
  })

  function currentWindow(): WindowRange {
    return { start: windowStart.value, end: windowEnd.value }
  }

  function applyWindow(range: WindowRange | undefined) {
    if (!range)
      return
    windowStart.value = range.start
    windowEnd.value = range.end
  }

  function clampWindow(following: boolean) {
    applyWindow(clampWindowRange(
      currentWindow(),
      options.index.renderableCount,
      following))
  }

  function updateWindow() {
    const element = options.viewport.value
    if (!element || options.index.renderableCount === 0) {
      applyWindow({ start: 0, end: 0 })
      return
    }
    const layout = options.layoutAnchor()
    if (layout && options.index.positionOf(layout.entryIndex) != null) {
      placeWindowAround(layout.entryIndex)
      return
    }
    const retained = options.retainedAnchor()
    const retainedRect = retained
      ? options.measuredRowFor(retained.entryIndex, element)
      : undefined
    const anchoredWindow = retained && retainedRect
      ? forVisibleAnchor(options.index, {
          entryIndex: retained.entryIndex,
          rowTop: retainedRect.top,
          rowBottom: retainedRect.bottom,
          viewportHeight: element.clientHeight,
        }, options.overscan)
      : undefined
    if (anchoredWindow) {
      applyWindow(anchoredWindow)
      return
    }
    applyWindow(forOffset(
      options.index,
      options.scrollGeometry.visibleContentRange(options.content.value),
      options.overscan))
  }

  function placeWindowAround(entryIndex: number) {
    const height = options.viewport.value?.clientHeight ?? 0
    applyWindow(aroundEntry(
      options.index,
      entryIndex,
      height,
      options.overscan))
  }

  function scheduleWindowUpdate() {
    if (scheduledFrame !== undefined || disposed)
      return
    scheduledFrame = requestAnimationFrame(() => {
      scheduledFrame = undefined
      updateWindow()
      void nextTick().then(() => options.sweepMeasurements())
    })
  }

  function dispose() {
    disposed = true
    if (scheduledFrame !== undefined)
      cancelAnimationFrame(scheduledFrame)
  }

  return {
    windowRowCount,
    visibleEntries,
    topSpacerHeight,
    bottomSpacerHeight,
    clampWindow,
    updateWindow,
    placeWindowAround,
    scheduleWindowUpdate,
    dispose,
  }
}
