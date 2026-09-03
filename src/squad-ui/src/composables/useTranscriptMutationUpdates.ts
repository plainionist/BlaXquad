import { nextTick, watch, type Ref } from 'vue'
import type { TranscriptEntry, TranscriptMutation } from '../protocol/messages'
import type { FollowState } from '../transcript/FollowState'
import type {
  ReadingAnchor,
  RestoreToken,
} from '../transcript/ReadingAnchor'
import type { RowMeasurements } from '../transcript/RowMeasurements'
import type {
  ScrollController,
  ScrollPositionGuard,
} from '../transcript/ScrollController'
import type { TranscriptIndex } from '../transcript/TranscriptIndex'
import { planUpdate } from '../transcript/TranscriptUpdatePlan'

interface TranscriptMutationUpdateOptions {
  entries: () => TranscriptEntry[]
  entryIndices: () => number[]
  mutation: () => TranscriptMutation | undefined
  viewport: Readonly<Ref<HTMLElement | null>>
  following: Readonly<Ref<boolean>>
  anchorCompensation: Readonly<Ref<number>>
  followState: FollowState
  index: TranscriptIndex
  measurements: RowMeasurements
  readingAnchor: ReadingAnchor
  scroll: ScrollController
  clampWindow: (following: boolean) => void
  updateWindow: () => void
  placeWindowAround: (entryIndex: number) => void
  beginEpoch: () => number
  isCurrent: (epoch: number) => boolean
  correctForState: (
    epoch: number,
    token?: RestoreToken,
    tailGuard?: ScrollPositionGuard,
  ) => Promise<void>
}

export function useTranscriptMutationUpdates(
  options: TranscriptMutationUpdateOptions,
) {
  function prepareMeasurementsForUpdate(
    entryIds: number[],
    mutation: TranscriptMutation | undefined,
    mutationIsSingular: boolean,
    clearMeasurements: boolean,
    invalidateMeasurement: number | undefined,
  ) {
    if (clearMeasurements
      || (mutationIsSingular
        && mutation?.kind === 'replace'
        && !options.index.canApplyTargetedReplace(entryIds, mutation))) {
      options.measurements.resetRows()
      return
    }
    if (invalidateMeasurement != null)
      options.measurements.invalidate(invalidateMeasurement)
  }

  watch(
    [options.entries, options.entryIndices, () => options.mutation()?.generation],
    async (
      [newEntries, newEntryIds, mutationGeneration],
      [, , oldMutationGeneration],
    ) => {
      const epoch = options.beginEpoch()
      const tailGuard = options.scroll.positionGuard()
      const mutation = options.mutation()
      const currentAnchor = options.readingAnchor.reading
        ?? (mutation?.kind === 'merge'
          ? options.readingAnchor.capture()
          : undefined)
      const changedEntryPosition = mutation?.entryIndex == null
        ? undefined
        : options.index.positionOf(mutation.entryIndex)
      const anchorPosition = currentAnchor == null
        ? undefined
        : options.index.positionOf(currentAnchor.entryIndex)
      const plan = planUpdate({
        mutation,
        mutationGeneration,
        previousMutationGeneration: oldMutationGeneration,
        following: options.following.value,
        currentAnchor,
        changedEntryPosition,
        anchorPosition,
        compensation: options.anchorCompensation.value,
        hasViewport: options.viewport.value != null,
      })
      const layoutTransaction = plan.layoutAnchor
        ? options.scroll.beginLayoutTransaction(plan.layoutAnchor)
        : undefined

      try {
        if (plan.suspendFollowing)
          options.followState.suspend()
        if (plan.clearCompensation)
          options.readingAnchor.releaseCompensation()

        prepareMeasurementsForUpdate(
          newEntryIds,
          mutation,
          plan.mutationIsSingular,
          plan.clearMeasurements,
          plan.invalidateMeasurement)
        const compensationBefore = plan.reduceCompensationAfterAppend
          ? options.anchorCompensation.value
          : 0
        const indexedHeightBefore = plan.reduceCompensationAfterAppend
          ? options.index.totalHeight()
          : 0

        options.index.applyMutation(
          newEntries,
          newEntryIds,
          mutation,
          plan.mutationIsSingular)
        options.clampWindow(options.following.value)

        if (plan.reduceCompensationAfterAppend) {
          const growth = options.index.totalHeight() - indexedHeightBefore
          if (Math.min(compensationBefore, growth) > 0
            && options.viewport.value) {
            options.scroll.expectPosition(
              options.viewport.value.scrollTop,
              'layout')
          }
          options.readingAnchor.reduceCompensationBy(growth)
        }

        const resolvedAnchor = options.readingAnchor.resolve(
          plan.requestedAnchor)
        if (resolvedAnchor)
          options.readingAnchor.retain(resolvedAnchor)
        let restoreToken = resolvedAnchor
          ? options.readingAnchor.beginRestore(resolvedAnchor)
          : undefined
        if (plan.requestedAnchor && !resolvedAnchor)
          options.readingAnchor.clearReading()
        if (restoreToken)
          options.placeWindowAround(restoreToken.anchor.entryIndex)

        await nextTick()
        if (!options.isCurrent(epoch))
          return
        if (options.measurements.sweep())
          return

        if (!restoreToken
          && layoutTransaction
          && options.scroll.wasClamped(layoutTransaction)) {
          const element = options.viewport.value
          if (element)
            options.scroll.expectPosition(element.scrollTop, 'layout')
          const anchor = options.readingAnchor.resolve(
            layoutTransaction.anchor)
          if (anchor) {
            options.readingAnchor.retain(anchor)
            restoreToken = options.readingAnchor.beginRestore(anchor)
            options.placeWindowAround(anchor.entryIndex)
          }
        }

        await options.correctForState(epoch, restoreToken, tailGuard)
        if (options.isCurrent(epoch))
          options.updateWindow()
      }
      finally {
        if (layoutTransaction)
          options.scroll.finishLayoutTransaction(layoutTransaction)
      }
    },
  )
}
