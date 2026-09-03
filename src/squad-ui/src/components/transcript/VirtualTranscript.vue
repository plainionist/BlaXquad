<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import { useTranscriptMutationUpdates } from '../../composables/useTranscriptMutationUpdates'
import { useTranscriptScrollIntent } from '../../composables/useTranscriptScrollIntent'
import { useTranscriptViewportLifecycle } from '../../composables/useTranscriptViewportLifecycle'
import { useVirtualWindow } from '../../composables/useVirtualWindow'
import type { TranscriptEntry, TranscriptMutation } from '../../protocol/messages'
import { FollowState } from '../../transcript/FollowState'
import { ReadingAnchor } from '../../transcript/ReadingAnchor'
import { RowMeasurements } from '../../transcript/RowMeasurements'
import { ScrollController } from '../../transcript/ScrollController'
import { TranscriptIndex } from '../../transcript/TranscriptIndex'
import { categoryFor } from '../../transcript/transcriptProjection'

const overscanRows = 12
const props = defineProps<{
  entries: TranscriptEntry[]
  entryIndices: number[]
  mutation?: TranscriptMutation
  isWorking: boolean
  activeTool?: string
  hasOlder?: boolean
  historyTruncated?: boolean
  announcements?: readonly { id: number; content: string }[]
}>()
const emit = defineEmits<{ loadOlder: []; loadEntry: [entryIndex: number] }>()

const viewport = ref<HTMLElement | null>(null)
const transcriptLines = ref<HTMLElement | null>(null)
const followState = new FollowState()
const scrollController = new ScrollController()

let transcriptIndex: TranscriptIndex
let readingAnchor: ReadingAnchor
const rowMeasurements = new RowMeasurements()
transcriptIndex = new TranscriptIndex(entryIndex =>
  rowMeasurements.heightFor(entryIndex))
const {
  windowRowCount,
  visibleEntries,
  topSpacerHeight,
  bottomSpacerHeight,
  clampWindow,
  updateWindow,
  placeWindowAround,
  scheduleWindowUpdate,
  dispose: disposeVirtualWindow,
} = useVirtualWindow({
  index: transcriptIndex,
  viewport,
  content: transcriptLines,
  measuredRowFor: (entryIndex, element) =>
    rowMeasurements.rectFor(entryIndex, element),
  sweepMeasurements: () => { rowMeasurements.sweep() },
  layoutAnchor: () => readingAnchor.layout,
  retainedAnchor: () => readingAnchor.reading,
  scrollGeometry: scrollController.viewportGeometry,
  topCompensation: () => readingAnchor.topCompensation.value,
  bottomCompensation: () => readingAnchor.bottomCompensation.value,
  overscan: overscanRows,
})
readingAnchor = new ReadingAnchor(
  transcriptIndex,
  rowMeasurements,
  scrollController,
  () => transcriptLines.value,
  placeWindowAround,
  updateWindow,
)

const following = followState.following
const indexRebuildCount = transcriptIndex.rebuildCount
const targetedMutationCount = transcriptIndex.targetedMutationCount
const measurementCount = rowMeasurements.count
const pendingInternalScrolls = scrollController.pendingPositionCount
const anchorCompensation = readingAnchor.bottomCompensation
const viewportLifecycle = useTranscriptViewportLifecycle({
  viewport,
  entries: () => props.entries,
  entryIndices: () => props.entryIndices,
  index: transcriptIndex,
  measurements: rowMeasurements,
  readingAnchor,
  scroll: scrollController,
  following,
  updateWindow,
  placeWindowAround,
  scheduleWindowUpdate,
  disposeVirtualWindow,
})
const {
  beginEpoch,
  isCurrent,
  isDisposed,
  currentEpoch,
  correctForState,
} = viewportLifecycle
const { handleScroll } = useTranscriptScrollIntent({
  following,
  anchorCompensation,
  followState,
  readingAnchor,
  scroll: scrollController,
  updateWindow,
  scheduleWindowUpdate,
  beginEpoch,
  isCurrent,
  isDisposed,
  currentEpoch,
  correctForState: epoch => correctForState(
    epoch,
    undefined,
    scrollController.positionGuard()),
})
useTranscriptMutationUpdates({
  entries: () => props.entries,
  entryIndices: () => props.entryIndices,
  mutation: () => props.mutation,
  viewport,
  following,
  anchorCompensation,
  followState,
  index: transcriptIndex,
  measurements: rowMeasurements,
  readingAnchor,
  scroll: scrollController,
  clampWindow,
  updateWindow,
  placeWindowAround,
  beginEpoch,
  isCurrent,
  correctForState,
})
const showsThinking = computed(() => props.isWorking && !props.activeTool)
const hasVisibleEntries = computed(() => {
  void transcriptIndex.revision.value
  return transcriptIndex.renderableCount > 0
})
const lastVisibleCategory = computed(() => {
  void transcriptIndex.revision.value
  if (transcriptIndex.renderableCount === 0)
    return 'system'
  const position = transcriptIndex.positionForRank(
    transcriptIndex.renderableCount - 1)
  const sourceIndex = transcriptIndex.sourceIndexAt(position)
  return categoryFor(props.entries[sourceIndex]?.source ?? 'system')
})
async function scrollToEnd() {
  const epoch = beginEpoch()
  const element = viewport.value
  if (!element)
    return
  const maximum = scrollController.viewportGeometry.realContentMaximum(
    anchorCompensation.value)
  followState.onExternalMove(maximum, maximum)
  readingAnchor.clearReading()
  readingAnchor.releaseCompensation()
  await nextTick()
  if (!isCurrent(epoch))
    return
  scrollController.writePosition(element.scrollHeight)
  updateWindow()
  scheduleWindowUpdate()
}

defineExpose({ scrollToEnd })

function setRowElement(entryIndex: number, element: Element | null) {
  rowMeasurements.observe(entryIndex, element)
}
</script>

<template>
  <div
    ref="viewport"
    class="transcript"
    role="log"
    aria-label="Agent console"
    aria-live="off"
    :data-index-rebuilds="indexRebuildCount"
    :data-targeted-mutations="targetedMutationCount"
    :data-measurement-count="measurementCount"
    :data-projected-rows="visibleEntries.length"
    :data-window-rows="windowRowCount"
    :data-following="following"
    :data-pending-internal-scrolls="pendingInternalScrolls"
    :data-anchor-compensation="anchorCompensation"
    tabindex="0"
    @scroll="handleScroll"
  >
    <button v-if="props.hasOlder" class="load-older" type="button" @click="emit('loadOlder')">Load older</button>
    <p v-else-if="props.historyTruncated" class="transcript-history-truncated">Earlier transcript history is unavailable.</p>
    <div v-if="hasVisibleEntries || showsThinking" ref="transcriptLines" class="transcript-lines">
      <div class="transcript-spacer" :style="{ height: `${topSpacerHeight}px` }" aria-hidden="true"></div>
      <div
        v-for="entry in visibleEntries"
        :key="entry.entryIndex"
        :ref="element => setRowElement(entry.entryIndex, element as Element | null)"
        :data-entry-index="entry.entryIndex"
        class="transcript-line"
        :class="[`is-${entry.category}`, { 'is-separated': entry.separated }]"
      >
        <span v-if="entry.marker" class="transcript-marker" :class="`is-${entry.marker}`" aria-hidden="true"></span>
        <span v-if="entry.prefix" class="transcript-prefix">{{ entry.prefix }}</span>
        <span class="transcript-content">{{ entry.content || ' ' }}</span>
        <button v-if="entry.hasArchivedContent" class="load-entry" type="button" @click="emit('loadEntry', entry.entryIndex)">Load full entry</button>
      </div>
      <div class="transcript-spacer" :style="{ height: `${bottomSpacerHeight}px` }" aria-hidden="true"></div>
      <div
        v-if="showsThinking"
        :ref="element => rowMeasurements.observeAuxiliary('thinking', element as Element | null)"
        class="transcript-line is-thinking"
        :class="{ 'is-separated': hasVisibleEntries && lastVisibleCategory !== 'thinking' }"
      >
        <span class="transcript-prefix">&gt;</span>
        <span class="transcript-content">Thinking ...</span>
      </div>
    </div>
    <span v-else class="transcript-empty">No events yet.</span>
  </div>
  <span class="sr-only" aria-live="polite" aria-atomic="false" aria-relevant="additions text" data-transcript-announcer>
    <span
      v-for="announcement in props.announcements"
      :key="announcement.id"
      data-transcript-announcement
    >{{ announcement.content }}</span>
  </span>
</template>
