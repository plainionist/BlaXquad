import { readonly, ref } from 'vue'
import type {
  SequencedTranscriptAnnouncement,
  TranscriptAnnouncement,
} from '../protocol/messages'

interface PendingTranscriptAnnouncement {
  content: string
  lastEntryIndex?: number
  lastEntryStart: number
  truncated: boolean
}

interface PublishedTranscriptAnnouncement {
  id: number
  content: string
}

const maxPendingCharacters = 16_384
const maxPublishedItems = 64
const maxPublishedCharacters = 16_384
const publicationIntervalMilliseconds = 75
const omissionMarker = '[Earlier live output was omitted.]'
const removalMarker = '[Previous live output was removed.]'

export function useTranscriptAnnouncements() {
  const publishedAnnouncementsByRole =
    ref<Record<string, PublishedTranscriptAnnouncement[]>>({})
  const pendingAnnouncements =
    new Map<string, PendingTranscriptAnnouncement>()
  const publicationTimers =
    new Map<string, ReturnType<typeof setTimeout>>()
  let nextPublicationId = 0

  function queueTranscriptAnnouncements(
    role: string,
    fragments:
      readonly (TranscriptAnnouncement | SequencedTranscriptAnnouncement)[],
    truncated = false,
  ) {
    const pending = pendingAnnouncements.get(role) ?? {
      content: '',
      lastEntryIndex: undefined,
      lastEntryStart: 0,
      truncated: false,
    }
    pending.truncated ||= truncated
    for (const fragment of fragments) {
      pending.truncated ||= fragment.truncated ?? false
      const sameEntry = pending.lastEntryIndex === fragment.entryIndex
      if (fragment.operation === 'replace' && sameEntry) {
        pending.content = pending.content.slice(0, pending.lastEntryStart)
      } else if (fragment.operation !== 'append-content' || !sameEntry) {
        if (pending.content.length > 0)
          pending.content += '\n'
        pending.lastEntryStart = pending.content.length
      }
      const content = fragment.operation === 'replace'
        && fragment.content.trim().length === 0
        ? removalMarker
        : fragment.content
      pending.content += content
      pending.lastEntryIndex = fragment.entryIndex
    }
    if (pending.content.length === 0 && !pending.truncated) return
    if (pending.content.length > maxPendingCharacters) {
      const removedCharacters = pending.content.length - maxPendingCharacters
      pending.content = pending.content.slice(-maxPendingCharacters)
      pending.lastEntryStart = Math.max(
        0,
        pending.lastEntryStart - removedCharacters)
      pending.truncated = true
    }
    pendingAnnouncements.set(role, pending)
    if (!publicationTimers.has(role)) {
      publicationTimers.set(role, setTimeout(
        () => publishTranscriptAnnouncement(role),
        publicationIntervalMilliseconds))
    }
  }

  function publishTranscriptAnnouncement(role: string) {
    publicationTimers.delete(role)
    const pending = pendingAnnouncements.get(role)
    if (!pending) return
    if (pending.content.trim().length === 0 && !pending.truncated)
      return
    pendingAnnouncements.delete(role)
    const prefix = pending.truncated
      ? `${omissionMarker}\n`
      : ''
    let item = {
      id: ++nextPublicationId,
      content: prefix + pending.content,
    }
    const existing = publishedAnnouncementsByRole.value[role] ?? []
    const existingCharacters = existing.reduce(
      (total, announcement) => total + announcement.content.length,
      0)
    let announcements = [...existing, item]
    if (announcements.length > maxPublishedItems
      || existingCharacters + item.content.length > maxPublishedCharacters) {
      const availableCharacters = Math.max(
        0,
        maxPublishedCharacters - omissionMarker.length - 1)
      item = {
        ...item,
        content: `${omissionMarker}\n${pending.content.slice(-availableCharacters)}`,
      }
      announcements = [item]
    }
    publishedAnnouncementsByRole.value = {
      ...publishedAnnouncementsByRole.value,
      [role]: announcements,
    }
  }

  function dispose() {
    for (const timer of publicationTimers.values())
      clearTimeout(timer)
  }

  return {
    publishedAnnouncementsByRole: readonly(publishedAnnouncementsByRole),
    queueTranscriptAnnouncements,
    dispose,
  }
}
