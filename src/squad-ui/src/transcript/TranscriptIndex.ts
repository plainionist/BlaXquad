import { readonly, ref } from 'vue'
import type { TranscriptEntry, TranscriptMutation } from '../protocol/messages'
import {
  categoryFor,
  isRenderable,
  projectEntry,
  type ConsoleEntry,
} from './transcriptProjection'
import { PrefixSumIndex } from './PrefixSumIndex'

const estimatedRowHeight = 22

export class TranscriptIndex {
  private myEntries: TranscriptEntry[] = []
  private myEntryIds: number[] = []
  private mySourcePositions: number[] = []
  private readonly myEntryPositions = new Map<number, number>()
  private myHeights: number[] = []
  private myRenderableCount = 0
  private readonly myHeightIndex = new PrefixSumIndex()
  private readonly myRenderableIndex = new PrefixSumIndex()
  private readonly myRevision = ref(0)
  private readonly myRebuildCount = ref(0)
  private readonly myTargetedMutationCount = ref(0)
  private readonly myMeasuredHeightFor: (entryIndex: number) => number | undefined

  readonly revision = readonly(this.myRevision)
  readonly rebuildCount = readonly(this.myRebuildCount)
  readonly targetedMutationCount = readonly(this.myTargetedMutationCount)

  constructor(measuredHeightFor: (entryIndex: number) => number | undefined) {
    this.myMeasuredHeightFor = measuredHeightFor
  }

  get length() {
    return this.myEntryIds.length
  }

  get renderableCount() {
    return this.myRenderableCount
  }

  rebuild(entries: TranscriptEntry[], entryIds: number[]) {
    this.myRebuildCount.value++
    this.myEntries = entries
    this.myEntryIds = [...entryIds]
    this.mySourcePositions = entryIds.map((_, index) => index)
    this.myEntryPositions.clear()
    for (let index = 0; index < entryIds.length; index++)
      this.myEntryPositions.set(entryIds[index], index)

    this.myRenderableIndex.rebuild(
      entryIds.map((_, index) => isRenderable(entries[index]) ? 1 : 0))
    this.myRenderableCount = this.myRenderableIndex.total()

    this.myHeights = entryIds.map((entryIndex, index) =>
      isRenderable(entries[index])
        ? this.estimatedHeightForEntry(entryIndex, index, index - 1)
        : 0)
    this.myHeightIndex.rebuild(this.myHeights)
    this.myRevision.value++
  }

  canApplyTargetedReplace(
    entryIds: number[],
    mutation: TranscriptMutation | undefined,
  ) {
    return mutation?.entryIndex != null
      && mutation.sourceIndex != null
      && entryIds.length === mutation.previousSourceLength
      && mutation.sourceIndex >= 0
      && mutation.sourceIndex < entryIds.length
      && entryIds[mutation.sourceIndex] === mutation.entryIndex
      && this.myEntryPositions.has(mutation.entryIndex)
  }

  applyMutation(
    entries: TranscriptEntry[],
    entryIds: number[],
    mutation: TranscriptMutation | undefined,
    mutationIsSingular: boolean,
  ) {
    if (mutationIsSingular
      && mutation?.kind === 'append'
      && mutation.entryIndex === entryIds[entryIds.length - 1]
      && mutation.sourceIndex === entryIds.length - 1
      && entryIds.length === (mutation.previousSourceLength ?? -1) + 1) {
      this.myEntries = entries
      const sourceIndex = entryIds.length - 1
      const renderable = isRenderable(entries[sourceIndex])
      this.append(
        entryIds[sourceIndex],
        sourceIndex,
        renderable
          ? this.estimatedHeightForEntry(
            entryIds[sourceIndex],
            sourceIndex,
            this.myEntryIds.length - 1)
          : 0,
        renderable,
      )
      if (renderable)
        this.myRenderableCount++
      this.myTargetedMutationCount.value++
      return
    }

    if (mutationIsSingular
      && mutation?.kind === 'replace'
      && this.canApplyTargetedReplace(entryIds, mutation)) {
      this.myEntries = entries
      this.myEntryIds = [...entryIds]
      const sourceIndex = mutation.sourceIndex!
      const entryIndex = mutation.entryIndex!
      const position = this.myEntryPositions.get(entryIndex)!
      const renderable = isRenderable(entries[sourceIndex])
      if (renderable !== mutation.previouslyRenderable) {
        this.myRenderableCount += renderable ? 1 : -1
        this.myRenderableIndex.addDelta(position, renderable ? 1 : -1)
      }
      this.updateHeight(position, renderable ? this.estimatedHeight(position) : 0)
      const next = this.nextRenderablePosition(position)
      if (next != null)
        this.updateHeight(next, this.estimatedHeight(next))
      this.myTargetedMutationCount.value++
      this.myRevision.value++
      return
    }

    this.rebuild(entries, entryIds)
  }

  setMeasuredHeight(entryIndex: number, height: number) {
    const position = this.myEntryPositions.get(entryIndex)
    if (position == null || !this.isRenderableAt(position))
      return false
    const effectiveHeight = height + this.separationHeight(position)
    if (this.myHeights[position] === effectiveHeight)
      return false
    this.updateHeight(position, effectiveHeight)
    return true
  }

  positionOf(entryIndex: number) {
    return this.myEntryPositions.get(entryIndex)
  }

  entryIdAt(position: number) {
    return this.myEntryIds[position]
  }

  sourceIndexAt(position: number) {
    return this.mySourcePositions[position]
  }

  heightAt(position: number) {
    return this.myHeights[position] ?? 0
  }

  heightBefore(position: number) {
    return this.myHeightIndex.prefixSum(position)
  }

  totalHeight() {
    return this.myHeightIndex.total()
  }

  isRenderableAt(position: number) {
    const sourceIndex = this.mySourcePositions[position]
    return sourceIndex != null && isRenderable(this.myEntries[sourceIndex])
  }

  positionForRank(rank: number) {
    if (!Number.isInteger(rank) || rank < 0 || rank >= this.myRenderableCount)
      throw new RangeError(`Invalid renderable transcript rank: ${rank}.`)
    return this.myRenderableIndex.firstPositionWhosePrefixReaches(
      rank + 1,
      0,
      this.mySourcePositions.length - 1)
  }

  rankForPosition(position: number) {
    return this.myRenderableIndex.prefixSum(position + 1) - 1
  }

  rankAtOffset(offset: number) {
    if (this.myRenderableCount === 0)
      return 0
    const position = this.myHeightIndex.lastPositionWithPrefixAtMost(offset)
    return Math.max(
      0,
      Math.min(this.myRenderableCount - 1, this.rankForPosition(position)))
  }

  previousRenderablePosition(beforePosition: number) {
    const target = this.myRenderableIndex.prefixSum(beforePosition)
    return target === 0
      ? -1
      : this.myRenderableIndex.firstPositionWhosePrefixReaches(
          target,
          0,
          beforePosition - 1)
  }

  nextRenderablePosition(afterPosition: number) {
    const target = this.myRenderableIndex.prefixSum(afterPosition + 1) + 1
    return target > this.myRenderableCount
      ? undefined
      : this.myRenderableIndex.firstPositionWhosePrefixReaches(
          target,
          afterPosition + 1,
          this.mySourcePositions.length - 1)
  }

  nearestRenderableEntry(entryIndex: number) {
    const position = this.myEntryPositions.get(entryIndex)
    if (position != null && this.isRenderableAt(position))
      return entryIndex
    if (this.myRenderableCount === 0)
      return undefined
    let nearest = this.myEntryIds[this.positionForRank(0)]
    let distance = Math.abs(nearest - entryIndex)
    for (let rank = 1; rank < this.myRenderableCount; rank++) {
      const candidate = this.myEntryIds[this.positionForRank(rank)]
      const candidateDistance = Math.abs(candidate - entryIndex)
      if (candidateDistance < distance) {
        nearest = candidate
        distance = candidateDistance
      }
    }
    return nearest
  }

  projectAt(position: number): ConsoleEntry {
    const sourceIndex = this.mySourcePositions[position]
    const previousPosition = this.previousRenderablePosition(position)
    const previousEntry = previousPosition >= 0
      ? this.myEntries[this.mySourcePositions[previousPosition]]
      : undefined
    return projectEntry(
      this.myEntries[sourceIndex],
      this.myEntryIds[position],
      previousEntry)
  }

  private append(
    entryId: number,
    sourceIndex: number,
    height: number,
    renderable: boolean,
  ) {
    const index = this.myEntryIds.length
    this.myEntryIds.push(entryId)
    this.mySourcePositions.push(sourceIndex)
    this.myHeights.push(height)
    this.myEntryPositions.set(entryId, index)
    this.myHeightIndex.append(height)
    this.myRenderableIndex.append(renderable ? 1 : 0)
    this.myRevision.value++
  }

  private estimatedHeight(position: number) {
    const sourceIndex = this.mySourcePositions[position]
    if (!isRenderable(this.myEntries[sourceIndex]))
      return 0
    return this.estimatedHeightForEntry(
      this.myEntryIds[position],
      sourceIndex,
      position - 1)
  }

  private estimatedHeightForEntry(
    entryIndex: number,
    sourceIndex: number,
    previousPosition: number,
  ) {
    return (this.myMeasuredHeightFor(entryIndex) ?? estimatedRowHeight)
      + this.separationHeightForSource(sourceIndex, previousPosition)
  }

  private separationHeight(position: number) {
    return this.separationHeightForSource(
      this.mySourcePositions[position],
      position - 1)
  }

  private separationHeightForSource(sourceIndex: number, previousPosition: number) {
    const previousRenderable = this.previousRenderablePosition(previousPosition + 1)
    if (previousRenderable < 0)
      return 0
    const previousSource = this.mySourcePositions[previousRenderable]
    return categoryFor(this.myEntries[sourceIndex]?.source ?? 'system')
      === categoryFor(this.myEntries[previousSource]?.source ?? 'system')
      ? 0
      : estimatedRowHeight
  }

  private updateHeight(position: number, height: number) {
    const previous = this.myHeights[position]
    if (previous == null || previous === height)
      return
    this.myHeights[position] = height
    this.myHeightIndex.addDelta(position, height - previous)
    this.myRevision.value++
  }
}
