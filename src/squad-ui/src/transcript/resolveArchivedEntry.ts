import type {
  ArchivedTranscriptEntry,
  TranscriptEntry,
} from '../protocol/messages'

const archivedContentAvailableMarker =
  '[Earlier content is available in transcript history.]'
const archivedContentUnavailableMarker =
  '[Earlier content is no longer available.]'
const unavailableMiddleContentMarker =
  '\n[Middle content unavailable due to storage limit.]\n'

export function resolveArchivedEntry(
  displayedEntry: TranscriptEntry,
  response: ArchivedTranscriptEntry,
): TranscriptEntry {
  if (!response.entry) {
    const content = displayedEntry.content.includes(
      archivedContentAvailableMarker)
      ? displayedEntry.content.replace(
          archivedContentAvailableMarker,
          archivedContentUnavailableMarker)
      : archivedContentUnavailableMarker
    return {
      ...displayedEntry,
      content,
      hasArchivedContent: false,
    }
  }

  if (!response.contentTruncated) {
    return {
      ...response.entry,
      hasArchivedContent: false,
      contentStart: 0,
    }
  }

  const retainedStart = displayedEntry.contentStart ?? 0
  const retainedTailLength = Math.max(
    0,
    response.totalContentCharacters - retainedStart)
  const retainedTail = retainedTailLength === 0
    ? ''
    : displayedEntry.content.slice(-retainedTailLength)
  const archivedPrefix = response.entry.content.slice(
    0,
    response.archivedPrefixCharacters)
  const overlap = Math.max(
    0,
    response.archivedPrefixCharacters - retainedStart)
  const gap = response.archivedPrefixCharacters < retainedStart
    ? unavailableMiddleContentMarker
    : ''
  return {
    ...response.entry,
    content: archivedPrefix + gap + retainedTail.slice(overlap),
    hasArchivedContent: false,
    contentStart: 0,
  }
}
