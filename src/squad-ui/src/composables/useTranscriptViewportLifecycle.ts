import { nextTick, onMounted, onUnmounted, type Ref } from 'vue'
import type { TranscriptEntry } from '../protocol/messages'
import {
  type ReadingAnchor,
  type RestoreToken,
} from '../transcript/ReadingAnchor'
import {
  type GeometryBatch,
  type RowMeasurements,
} from '../transcript/RowMeasurements'
import {
  type ScrollController,
  type ScrollPositionGuard,
  type ViewportChange,
} from '../transcript/ScrollController'
import type { TranscriptIndex } from '../transcript/TranscriptIndex'

interface TranscriptViewportLifecycleOptions {
  viewport: Readonly<Ref<HTMLElement | null>>
  entries: () => TranscriptEntry[]
  entryIndices: () => number[]
  index: TranscriptIndex
  measurements: RowMeasurements
  readingAnchor: ReadingAnchor
  scroll: ScrollController
  following: Readonly<Ref<boolean>>
  updateWindow: () => void
  placeWindowAround: (entryIndex: number) => void
  scheduleWindowUpdate: () => void
  disposeVirtualWindow: () => void
}

export function useTranscriptViewportLifecycle(
  options: TranscriptViewportLifecycleOptions,
) {
  let updateEpoch = 0
  let disposed = false

  function beginEpoch() {
    options.readingAnchor.cancel()
    return ++updateEpoch
  }

  function isCurrent(epoch: number) {
    return !disposed && epoch === updateEpoch
  }

  function currentEpoch() {
    return updateEpoch
  }

  function isDisposed() {
    return disposed
  }

  async function correctForState(
    epoch: number,
    token?: RestoreToken,
    tailGuard: ScrollPositionGuard = options.scroll.positionGuard(),
  ) {
    await nextTick()
    if (!isCurrent(epoch))
      return
    if (options.following.value) {
      await options.scroll.scrollToTail(
        tailGuard,
        () => isCurrent(epoch))
      return
    }
    if (token)
      await options.readingAnchor.stabilize(token, () => isCurrent(epoch))
    else
      await options.readingAnchor.restoreRetained(() => isCurrent(epoch))
  }

  function handleGeometryBatch(batch: GeometryBatch) {
    options.scroll.reportGeometryChange()
    for (const change of batch.changes)
      options.index.setMeasuredHeight(change.entryIndex, change.height)
    const epoch = beginEpoch()
    const tailGuard = options.scroll.positionGuard()
    options.updateWindow()
    void correctForState(epoch, undefined, tailGuard)
  }

  function handleViewportChange(change: ViewportChange) {
    const epoch = beginEpoch()
    void applyViewportChange(change, epoch)
  }

  async function applyViewportChange(
    change: ViewportChange,
    epoch: number,
  ) {
    if (change.widthChanged) {
      options.readingAnchor.releaseCompensation()
      options.measurements.clear()
      options.index.rebuild(options.entries(), options.entryIndices())
      const retained = options.readingAnchor.resolve(
        options.readingAnchor.reading)
      if (retained)
        options.placeWindowAround(retained.entryIndex)
    }
    else if (change.heightChanged && options.readingAnchor.reading) {
      options.placeWindowAround(options.readingAnchor.reading.entryIndex)
    }
    options.updateWindow()
    await nextTick()
    if (!isCurrent(epoch))
      return
    options.measurements.sweep()
    const correctionEpoch = beginEpoch()
    await correctForState(
      correctionEpoch,
      undefined,
      options.scroll.positionGuard())
    options.scheduleWindowUpdate()
  }

  function initialize() {
    const element = options.viewport.value
    if (element)
      options.scroll.attach(element, handleViewportChange)
    options.index.rebuild(options.entries(), options.entryIndices())
    options.updateWindow()
    void nextTick().then(async () => {
      options.measurements.sweep()
      const epoch = beginEpoch()
      await correctForState(
        epoch,
        undefined,
        options.scroll.positionGuard())
    })
  }

  function dispose() {
    disposed = true
    updateEpoch++
    options.disposeVirtualWindow()
    options.measurements.dispose()
    options.readingAnchor.dispose()
    options.scroll.dispose()
  }

  options.measurements.setGeometryBatchCallback(handleGeometryBatch)
  onMounted(initialize)
  onUnmounted(dispose)

  return {
    beginEpoch,
    isCurrent,
    isDisposed,
    currentEpoch,
    correctForState,
  }
}
