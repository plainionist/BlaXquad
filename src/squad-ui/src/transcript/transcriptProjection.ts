import type { TranscriptEntry } from '../protocol/messages'

export type ConsoleEntry = TranscriptEntry & {
  entryIndex: number
  category: string
  prefix: string
  marker: string
  separated: boolean
  timestamp: string
}

export function categoryFor(source: string) {
  if (source === 'harness' || source === 'reasoning' || source === 'assistant')
    return 'message'
  if (source === 'read' || source === 'tool_output')
    return 'tool'
  return source
}

export function prefixFor(source: string) {
  if (source === 'user') return '>'
  if (source === 'subagent') return '>>'
  if (source === 'tool') return '$'
  return ''
}

export function markerFor(source: string) {
  if (source === 'read') return 'read'
  if (source === 'harness' || source === 'reasoning' || source === 'assistant')
    return 'response'
  return ''
}

export function normalizeContent(content: unknown) {
  return typeof content === 'string'
    ? content.replace(/^(?:\r?\n)+/, '').replace(/(?:\r?\n)+$/, '')
    : ''
}

export function isRenderable(entry: TranscriptEntry | undefined) {
  return normalizeContent(entry?.content).trim().length > 0
}

export function formatTimestamp(occurredAt: string) {
  const occurredAtDate = new Date(occurredAt)
  if (Number.isNaN(occurredAtDate.getTime()))
    return ''
  const hours = String(occurredAtDate.getHours()).padStart(2, '0')
  const minutes = String(occurredAtDate.getMinutes()).padStart(2, '0')
  const seconds = String(occurredAtDate.getSeconds()).padStart(2, '0')
  return `${hours}:${minutes}:${seconds}`
}

export function projectEntry(
  entry: TranscriptEntry | undefined,
  entryIndex: number,
  previousRenderableEntry?: TranscriptEntry,
): ConsoleEntry {
  const source = typeof entry?.source === 'string' ? entry.source : 'system'
  const category = categoryFor(source)
  const occurredAt = entry?.occurredAt ?? ''
  return {
    occurredAt,
    source,
    content: normalizeContent(entry?.content),
    entryIndex,
    hasArchivedContent: entry?.hasArchivedContent,
    contentStart: entry?.contentStart,
    category,
    prefix: prefixFor(source),
    marker: markerFor(source),
    separated: previousRenderableEntry != null
      && category !== categoryFor(previousRenderableEntry.source ?? 'system'),
    timestamp: formatTimestamp(occurredAt),
  }
}
