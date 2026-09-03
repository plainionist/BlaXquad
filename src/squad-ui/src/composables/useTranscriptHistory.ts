import { readonly, ref } from 'vue'
import type {
  ArchivedTranscriptEntry,
  IndexedTranscriptEntry,
  TranscriptEntry,
  TranscriptPage,
} from '../protocol/messages'
import { resolveArchivedEntry } from '../transcript/resolveArchivedEntry'

interface TranscriptEntries {
  transcriptEntries: readonly TranscriptEntry[]
  transcriptEntryIndices: readonly number[]
}

interface TranscriptHistoryMerge {
  transcriptEntries: TranscriptEntry[]
  transcriptEntryIndices: number[]
}

type SendTranscriptHistoryRequest = (
  type: 'transcript.page' | 'transcript.entry',
  options: {
    role: string
    payload: { beforeIndex: number } | { entryIndex: number }
  },
) => void

export function useTranscriptHistory(send: SendTranscriptHistoryRequest) {
  const liveEntryIndicesByRole = new Map<string, Set<number>>()
  const pagedEntriesByRole = new Map<string, Map<number, TranscriptEntry>>()
  const rolesWithOlderTranscript = ref(new Set<string>())
  const rolesWithTruncatedTranscript = ref(new Set<string>())
  const pendingPageRequests = new Map<string, number>()
  const pendingArchivedEntryRequests = new Set<string>()
  const exhaustedPageBoundariesByRole = new Map<string, Set<number>>()

  function mergeSynchronizedEntries(
    role: string,
    indexedLiveEntries: readonly IndexedTranscriptEntry[],
    hasMore: boolean,
    historyTruncated: boolean,
    recovery: boolean,
  ): TranscriptHistoryMerge {
    const liveEntries = new Map(indexedLiveEntries.map(
      ({ entryIndex, ...entry }) => [entryIndex, entry]))
    liveEntryIndicesByRole.set(role, new Set(liveEntries.keys()))
    const pageEntries = pagedEntriesByRole.get(role) ?? new Map()
    if (!recovery)
      pageEntries.clear()
    for (const entryIndex of liveEntries.keys())
      pageEntries.delete(entryIndex)
    pagedEntriesByRole.set(role, pageEntries)

    const merged = new Map(pageEntries)
    for (const [entryIndex, entry] of liveEntries)
      merged.set(entryIndex, entry)
    const orderedEntries = [...merged.entries()]
      .sort(([left], [right]) => left - right)
    const transcriptEntryIndices = orderedEntries.map(
      ([entryIndex]) => entryIndex)

    if (!recovery || pageEntries.size === 0)
      exhaustedPageBoundariesByRole.set(role, new Set())
    updateOlderTranscriptAvailability(role, transcriptEntryIndices, hasMore)
    if (historyTruncated)
      rolesWithTruncatedTranscript.value.add(role)
    else
      rolesWithTruncatedTranscript.value.delete(role)

    return {
      transcriptEntries: orderedEntries.map(([, entry]) => entry),
      transcriptEntryIndices,
    }
  }

  function mergeTranscriptPage(
    page: TranscriptPage,
    current: TranscriptEntries,
  ): TranscriptHistoryMerge {
    const requestedBoundary = pendingPageRequests.get(page.role)
    pendingPageRequests.delete(page.role)
    const existingIndices = current.transcriptEntryIndices
    const unseenEntries = page.entries.filter(
      entry => !existingIndices.includes(entry.entryIndex))
    if (requestedBoundary != null && unseenEntries.length === 0 && !page.hasMore)
      exhaustedBoundariesFor(page.role).add(requestedBoundary)

    const existing = new Map(existingIndices.map((entryIndex, index) => [
      entryIndex,
      current.transcriptEntries[index],
    ]))
    const liveEntryIndices = liveEntryIndicesByRole.get(page.role) ?? new Set()
    const pageEntries = pagedEntriesByRole.get(page.role) ?? new Map()
    pagedEntriesByRole.set(page.role, pageEntries)
    for (const { entryIndex, ...entry } of page.entries) {
      if (!existing.has(entryIndex)) {
        existing.set(entryIndex, entry)
        if (!liveEntryIndices.has(entryIndex))
          pageEntries.set(entryIndex, entry)
      }
    }

    const merged = [...existing.entries()]
      .sort(([left], [right]) => left - right)
    const transcriptEntryIndices = merged.map(([entryIndex]) => entryIndex)
    updateOlderTranscriptAvailability(
      page.role,
      transcriptEntryIndices,
      page.hasMore)
    if (page.historyTruncated)
      rolesWithTruncatedTranscript.value.add(page.role)

    return {
      transcriptEntries: merged.map(([, entry]) => entry),
      transcriptEntryIndices,
    }
  }

  function recordLiveEntry(role: string, entryIndex: number) {
    liveEntryIndicesByRole.get(role)?.add(entryIndex)
    pagedEntriesByRole.get(role)?.delete(entryIndex)
  }

  function completeTranscriptPageRequest(role: string) {
    pendingPageRequests.delete(role)
  }

  function selectNextBeforeIndex(
    role: string,
    entryIndices: readonly number[] | undefined,
  ) {
    const exhaustedBoundaries = exhaustedBoundariesFor(role)
    let beforeIndex = entryIndices?.[0]
    for (let index = 1; entryIndices && index < entryIndices.length; index++) {
      if (entryIndices[index] > entryIndices[index - 1] + 1
        && !exhaustedBoundaries.has(entryIndices[index])) {
        beforeIndex = entryIndices[index]
        break
      }
    }
    return beforeIndex
  }

  function requestTranscriptPage(
    role: string,
    entryIndices: readonly number[] | undefined,
    hasTranscriptPosition: boolean,
  ) {
    const beforeIndex = selectNextBeforeIndex(role, entryIndices)
    const exhaustedBoundaries = exhaustedBoundariesFor(role)
    if (!hasTranscriptPosition
      || beforeIndex == null
      || exhaustedBoundaries.has(beforeIndex)
      || pendingPageRequests.has(role))
      return
    pendingPageRequests.set(role, beforeIndex)
    send('transcript.page', { role, payload: { beforeIndex } })
  }

  function requestArchivedTranscriptEntry(role: string, entryIndex: number) {
    const key = archivedEntryRequestKey(role, entryIndex)
    if (pendingArchivedEntryRequests.has(key))
      return
    pendingArchivedEntryRequests.add(key)
    send('transcript.entry', { role, payload: { entryIndex } })
  }

  function completeArchivedTranscriptEntryRequest(
    role: string,
    entryIndex: number,
  ) {
    pendingArchivedEntryRequests.delete(
      archivedEntryRequestKey(role, entryIndex))
  }

  function resolveArchivedTranscriptEntry(
    displayedEntry: TranscriptEntry,
    response: ArchivedTranscriptEntry,
  ) {
    const resolvedEntry = resolveArchivedEntry(displayedEntry, response)
    const pageEntries = pagedEntriesByRole.get(response.role)
    if (pageEntries?.has(response.entryIndex))
      pageEntries.set(response.entryIndex, resolvedEntry)
    return resolvedEntry
  }

  function updateOlderTranscriptAvailability(
    role: string,
    entryIndices: readonly number[],
    hasMore: boolean,
  ) {
    if (hasMore || hasFillableTranscriptGaps(role, entryIndices))
      rolesWithOlderTranscript.value.add(role)
    else
      rolesWithOlderTranscript.value.delete(role)
  }

  function hasFillableTranscriptGaps(
    role: string,
    entryIndices: readonly number[],
  ) {
    const exhaustedBoundaries =
      exhaustedPageBoundariesByRole.get(role) ?? new Set()
    return entryIndices.some((entryIndex, index) =>
      index > 0
      && entryIndex > entryIndices[index - 1] + 1
      && !exhaustedBoundaries.has(entryIndex))
  }

  function exhaustedBoundariesFor(role: string) {
    let boundaries = exhaustedPageBoundariesByRole.get(role)
    if (!boundaries) {
      boundaries = new Set()
      exhaustedPageBoundariesByRole.set(role, boundaries)
    }
    return boundaries
  }

  function archivedEntryRequestKey(role: string, entryIndex: number) {
    return `${role}\u001f${entryIndex}`
  }

  return {
    rolesWithOlderTranscript: readonly(rolesWithOlderTranscript),
    rolesWithTruncatedTranscript: readonly(rolesWithTruncatedTranscript),
    mergeSynchronizedEntries,
    mergeTranscriptPage,
    recordLiveEntry,
    completeTranscriptPageRequest,
    requestTranscriptPage,
    requestArchivedTranscriptEntry,
    completeArchivedTranscriptEntryRequest,
    resolveArchivedTranscriptEntry,
  }
}
