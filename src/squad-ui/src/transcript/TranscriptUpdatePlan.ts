import type { TranscriptMutation } from '../protocol/messages'
import type { ScrollAnchor } from './ReadingAnchor'

export interface TranscriptUpdateSnapshot {
  mutation?: TranscriptMutation
  mutationGeneration?: number
  previousMutationGeneration?: number
  following: boolean
  currentAnchor?: ScrollAnchor
  changedEntryPosition?: number
  anchorPosition?: number
  compensation: number
  hasViewport: boolean
}

export interface TranscriptUpdatePlan {
  mutationIsSingular: boolean
  suspendFollowing: boolean
  clearCompensation: boolean
  preserveViewport: boolean
  requestedAnchor?: ScrollAnchor
  layoutAnchor?: ScrollAnchor
  reduceCompensationAfterAppend: boolean
  invalidateMeasurement?: number
  clearMeasurements: boolean
}

export function planUpdate(snapshot: TranscriptUpdateSnapshot): TranscriptUpdatePlan {
  const mutation = snapshot.mutation
  const mutationIsSingular = mutation != null
    && snapshot.mutationGeneration != null
    && snapshot.previousMutationGeneration != null
    && snapshot.mutationGeneration === snapshot.previousMutationGeneration + 1
  const insertedBeforeTail = mutation?.kind === 'merge'
  const changedEntryIsAfterViewport = mutation?.entryIndex != null
    && snapshot.currentAnchor != null
    && snapshot.changedEntryPosition != null
    && snapshot.anchorPosition != null
    && snapshot.changedEntryPosition > snapshot.anchorPosition
  const updateDoesNotShiftViewport = mutation?.kind === 'append'
    || (mutation?.kind === 'replace' && changedEntryIsAfterViewport)
  const preserveViewport = insertedBeforeTail
    || (!snapshot.following
      && (!mutationIsSingular
        || !updateDoesNotShiftViewport
        || (snapshot.compensation > 0 && mutation?.kind !== 'append')))
  const layoutAnchor = !snapshot.following
    && mutationIsSingular
    && mutation?.kind === 'replace'
    && changedEntryIsAfterViewport
    ? snapshot.currentAnchor
    : undefined

  return {
    mutationIsSingular,
    suspendFollowing: insertedBeforeTail,
    clearCompensation: snapshot.following && !insertedBeforeTail,
    preserveViewport,
    requestedAnchor: preserveViewport ? snapshot.currentAnchor : undefined,
    layoutAnchor,
    reduceCompensationAfterAppend: !snapshot.following
      && mutationIsSingular
      && mutation?.kind === 'append'
      && snapshot.compensation > 0
      && snapshot.hasViewport,
    invalidateMeasurement: mutationIsSingular && mutation?.kind === 'replace'
      ? mutation.entryIndex
      : undefined,
    clearMeasurements: !mutationIsSingular || mutation?.kind === 'reset',
  }
}
