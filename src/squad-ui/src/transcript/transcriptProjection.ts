import type { TranscriptEntry } from '../protocol/messages'

export type ConsoleEntry = TranscriptEntry & {
  entryIndex: number
  category: string
  prefix: string
  marker: string
  separated: boolean
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

export function projectEntry(
  entry: TranscriptEntry | undefined,
  entryIndex: number,
  previousRenderableEntry?: TranscriptEntry,
): ConsoleEntry {
  const source = typeof entry?.source === 'string' ? entry.source : 'system'
  const category = categoryFor(source)
  return {
    occurredAt: entry?.occurredAt ?? '',
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
  }
}
