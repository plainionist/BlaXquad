import { nextTick, type Ref } from 'vue'
import type { FollowState } from '../transcript/FollowState'
import type { ReadingAnchor } from '../transcript/ReadingAnchor'
import type { ScrollController } from '../transcript/ScrollController'

interface ProvisionalMovement {
  version: number
  position: number
  realMaximum: number
  anchor: ReturnType<ReadingAnchor['capture']>
}

interface PendingMovement {
  chainId: number
  correctionVersion: number
  stateEpoch: number
  previousFollowing: boolean
  previousAnchor: ReturnType<ReadingAnchor['capture']>
  events: Map<number, ProvisionalMovement>
}

interface TranscriptScrollIntentOptions {
  following: Readonly<Ref<boolean>>
  anchorCompensation: Readonly<Ref<number>>
  followState: FollowState
  readingAnchor: ReadingAnchor
  scroll: ScrollController
  updateWindow: () => void
  scheduleWindowUpdate: () => void
  beginEpoch: () => number
  isCurrent: (epoch: number) => boolean
  isDisposed: () => boolean
  currentEpoch: () => number
  correctForState: (epoch: number) => Promise<void>
}

export function useTranscriptScrollIntent(
  options: TranscriptScrollIntentOptions,
) {
  let pendingMovementVersion = 0
  let pendingMovement: PendingMovement | undefined

  async function handleScroll(event: Event) {
    let externalEpoch: number | undefined
    const movement = await options.scroll.classifyScroll(
      event.isTrusted,
      (position, generation, chainId, chainStarted) => {
        beginProvisionalMovement(
          position,
          generation,
          chainId,
          chainStarted)
      })
    if (movement.origin === 'pending' || movement.origin === 'superseded')
      return

    if (movement.chainId != null) {
      const pending = pendingMovement
      if (!pending || pending.chainId !== movement.chainId)
        return
      pendingMovement = undefined
      if (movement.origin === 'external') {
        const external = movement.externalGeneration == null
          ? undefined
          : pending.events.get(movement.externalGeneration)
        if (external)
          await confirmPendingExternal(pending, external)
      }
      else {
        reconcileProvisionalMovement(pending)
      }
      return
    }

    if (movement.origin === 'external') {
      externalEpoch = options.beginEpoch()
      const transition = options.followState.onExternalMove(
        movement.position,
        options.scroll.viewportGeometry.realContentMaximum(
          options.anchorCompensation.value))
      if (options.following.value) {
        options.readingAnchor.clearReading()
        if (transition === 'started')
          options.readingAnchor.releaseCompensation()
      }
      else {
        options.readingAnchor.retain(options.readingAnchor.capture())
      }
    }
    else {
      options.scheduleWindowUpdate()
      return
    }
    options.updateWindow()
    if (externalEpoch != null)
      await options.correctForState(externalEpoch)
    options.scheduleWindowUpdate()
  }

  async function confirmPendingExternal(
    pending: PendingMovement,
    movement: ProvisionalMovement,
  ) {
    const stateChanged = pending.stateEpoch !== options.currentEpoch()
    const epoch = options.beginEpoch()
    applyPendingExternal(movement, stateChanged)
    options.updateWindow()
    await correctSettledMovementState(epoch, pending.correctionVersion)
    options.scheduleWindowUpdate()
  }

  async function correctSettledMovementState(
    epoch: number,
    movementVersion: number,
  ) {
    let correctionEpoch = epoch
    while (!options.isDisposed()
      && movementVersion === pendingMovementVersion) {
      await options.correctForState(correctionEpoch)
      if (options.isCurrent(correctionEpoch))
        return
      await nextTick()
      correctionEpoch = options.currentEpoch()
    }
  }

  function beginProvisionalMovement(
    position: number,
    generation: number,
    chainId: number,
    chainStarted: boolean,
  ) {
    const previous = pendingMovement
    const continuing = !chainStarted && previous?.chainId === chainId
    const previousFollowing = continuing
      ? previous.previousFollowing
      : options.following.value
    const previousAnchor = continuing
      ? previous.previousAnchor
      : options.readingAnchor.reading
    const stateEpoch = continuing
      ? previous.stateEpoch
      : options.currentEpoch() + 1
    const realMaximum = options.scroll.viewportGeometry.realContentMaximum(
      options.anchorCompensation.value)
    const provisionalAnchor = options.readingAnchor.capture()
    const correctionVersion = continuing
      ? previous.correctionVersion
      : ++pendingMovementVersion
    options.beginEpoch()
    const movement = {
      version: generation,
      position,
      realMaximum,
      anchor: provisionalAnchor,
    }
    const events = continuing
      ? previous.events
      : new Map<number, ProvisionalMovement>()
    events.set(generation, movement)
    pendingMovement = continuing
      ? previous
      : {
          chainId,
          correctionVersion,
          stateEpoch,
          previousFollowing,
          previousAnchor,
          events,
        }
    options.followState.suspend()
    options.readingAnchor.retain(provisionalAnchor)
    options.updateWindow()
    options.scheduleWindowUpdate()
  }

  function reconcileProvisionalMovement(pending: PendingMovement) {
    const epoch = options.beginEpoch()
    options.followState.reconcileInternal(pending.previousFollowing)
    options.readingAnchor.retain(pending.previousAnchor)
    options.updateWindow()
    void correctSettledMovementState(
      epoch,
      pending.correctionVersion)
  }

  function applyPendingExternal(
    movement: ProvisionalMovement,
    stateChanged: boolean,
  ) {
    const currentMaximum =
      options.scroll.viewportGeometry.realContentMaximum(
        options.anchorCompensation.value)
    const distanceFromObservedTail = Math.max(
      0,
      movement.realMaximum - movement.position)
    const effectivePosition = stateChanged
      ? Math.max(0, currentMaximum - distanceFromObservedTail)
      : movement.position
    const transition = options.followState.onExternalMove(
      effectivePosition,
      currentMaximum)
    if (options.following.value) {
      options.readingAnchor.clearReading()
      if (transition === 'started')
        options.readingAnchor.releaseCompensation()
    }
    else {
      options.readingAnchor.retain(movement.anchor)
    }
  }

  return {
    handleScroll,
  }
}
