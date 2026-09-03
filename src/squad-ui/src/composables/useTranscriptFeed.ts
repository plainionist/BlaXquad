import { computed, ref } from 'vue'
import type {
  ArchivedTranscriptEntry,
  RoleState,
  Snapshot,
  TranscriptMutation,
  TranscriptPage,
  TranscriptSynchronization,
  TranscriptUpdate,
} from '../protocol/messages'
import { useTranscriptAnnouncements } from './useTranscriptAnnouncements'
import { useTranscriptHistory } from './useTranscriptHistory'

interface TranscriptPosition {
  visualSequence: number
  announcementSequence: number
}

type SendTranscriptRequest = (
  type: 'transcript.synchronize' | 'transcript.page' | 'transcript.entry',
  options: {
    role?: string
    payload: unknown
  },
) => void

export function useTranscriptFeed(send: SendTranscriptRequest) {
  const roles = ref<RoleState[]>([])
  const transcriptPositions: Record<string, TranscriptPosition> = {}
  const transcriptEntryPositions = new Map<string, Map<number, number>>()
  const transcriptMutationRevisions = new Map<string, number>()
  const {
    rolesWithOlderTranscript,
    rolesWithTruncatedTranscript,
    mergeSynchronizedEntries,
    mergeTranscriptPage,
    recordLiveEntry,
    completeTranscriptPageRequest,
    requestTranscriptPage: requestHistoryPage,
    requestArchivedTranscriptEntry,
    completeArchivedTranscriptEntryRequest,
    resolveArchivedTranscriptEntry,
  } = useTranscriptHistory(send)
  const {
    publishedAnnouncementsByRole,
    queueTranscriptAnnouncements,
    dispose: disposeTranscriptAnnouncements,
  } = useTranscriptAnnouncements()
  let transcriptSynchronizationPending = true

  function applySnapshot(snapshot: Snapshot) {
    const existingRoles = new Map(roles.value.map(role => [role.role, role]))
    roles.value = snapshot.roles.map((role) => ({
      ...role,
      transcriptEntries:
        existingRoles.get(role.role)?.transcriptEntries ?? [],
      transcriptEntryIndices:
        existingRoles.get(role.role)?.transcriptEntryIndices ?? [],
      transcriptMutation:
        existingRoles.get(role.role)?.transcriptMutation,
    }))
  }

  function applyTranscriptSynchronization(
    synchronization: TranscriptSynchronization,
  ) {
    const entriesByRole = new Map(
      synchronization.roles.map(role => [role.role, role]))
    roles.value = roles.value.map((role) => {
      const synchronized = entriesByRole.get(role.role)
      if (!synchronized) return role
      const previousPosition = transcriptPositions[role.role]
        ?? { visualSequence: 0, announcementSequence: 0 }
      const hasAnnouncementInterval = synchronized.announcementAfter != null
        && synchronized.announcementThrough != null
      let announcementSequence = previousPosition.announcementSequence
      if (hasAnnouncementInterval) {
        const intervalCursor = synchronization.recovery
          ? announcementSequence
          : Math.max(announcementSequence, synchronized.announcementAfter!)
        if (synchronized.announcementThrough! > intervalCursor) {
          const fragments = synchronized.announcement?.fragments
            .filter(fragment =>
              fragment.sequence > intervalCursor
              && fragment.sequence <= synchronized.announcementThrough!) ?? []
          queueTranscriptAnnouncements(
            role.role,
            fragments,
            (synchronized.announcement?.truncated ?? false)
              || (synchronization.recovery === true
                && intervalCursor < synchronized.announcementAfter!))
        }
        announcementSequence = Math.max(
          intervalCursor,
          synchronized.announcementThrough!)
      } else {
        if (synchronization.recovery && synchronized.announcement)
          queueTranscriptAnnouncements(
            role.role,
            synchronized.announcement.fragments,
            synchronized.announcement.truncated)
        announcementSequence = Math.max(
          announcementSequence,
          synchronized.sequence)
      }
      transcriptPositions[role.role] = {
        visualSequence: Math.max(
          previousPosition.visualSequence,
          synchronized.sequence),
        announcementSequence,
      }
      if (synchronized.sequence < previousPosition.visualSequence)
        return role
      const {
        transcriptEntries,
        transcriptEntryIndices,
      } = mergeSynchronizedEntries(
        role.role,
        synchronized.entries,
        synchronized.hasMore,
        synchronized.historyTruncated,
        synchronization.recovery === true)
      indexTranscriptEntries(role.role, transcriptEntryIndices)
      return {
        ...role,
        transcriptEntries,
        transcriptEntryIndices,
        transcriptMutation: createTranscriptMutation(role.role, 'reset'),
      }
    })
    transcriptSynchronizationPending = false
  }

  function applyTranscriptUpdate(update: TranscriptUpdate) {
    const roleIndex = roles.value.findIndex(role => role.role === update.role)
    const position = transcriptPositions[update.role]
    if (roleIndex < 0 || !position) {
      requestTranscriptSynchronization()
      return
    }
    const appliesVisualUpdate = update.sequence > position.visualSequence
    const appliesAnnouncement = update.sequence > position.announcementSequence
    if (!appliesVisualUpdate && !appliesAnnouncement) return
    if ((appliesVisualUpdate
        && update.sequence !== position.visualSequence + 1)
      || (appliesAnnouncement
        && update.sequence !== position.announcementSequence + 1))
      return requestTranscriptSynchronization()

    const role = roles.value[roleIndex]
    if (appliesVisualUpdate) {
      const entries = role.transcriptEntries
      const entryIndices = role.transcriptEntryIndices
      const entryPositions = transcriptEntryPositions.get(update.role)
      const localIndex = entryPositions?.get(update.entryIndex)
      const previousSourceLength = entries.length
      let mutationKind: TranscriptMutation['kind']
      let previouslyRenderable: boolean | undefined
      if (update.operation === 'append') {
        if (!update.entry || localIndex != null)
          return requestTranscriptSynchronization()
        entries.push(update.entry)
        entryIndices.push(update.entryIndex)
        entryPositions?.set(update.entryIndex, entries.length - 1)
        mutationKind = 'append'
      } else if (update.operation === 'append-content') {
        if (update.content == null
          || localIndex == null
          || localIndex >= entries.length)
          return requestTranscriptSynchronization()
        previouslyRenderable = isTranscriptEntryRenderable(entries[localIndex])
        entries[localIndex] = {
          ...entries[localIndex],
          content: entries[localIndex].content + update.content,
        }
        mutationKind = 'replace'
      } else if (update.operation === 'replace') {
        if (!update.entry
          || localIndex == null
          || localIndex >= entries.length)
          return requestTranscriptSynchronization()
        previouslyRenderable = isTranscriptEntryRenderable(entries[localIndex])
        entries[localIndex] = update.entry
        mutationKind = 'replace'
      } else {
        return requestTranscriptSynchronization()
      }
      recordLiveEntry(update.role, update.entryIndex)
      roles.value[roleIndex] = {
        ...role,
        transcriptMutation: createTranscriptMutation(
          update.role,
          mutationKind,
          update.entryIndex,
          mutationKind === 'append' ? entries.length - 1 : localIndex,
          previousSourceLength,
          previouslyRenderable),
      }
      position.visualSequence = update.sequence
    }

    if (appliesAnnouncement) {
      if (update.announcement)
        queueTranscriptAnnouncements(update.role, [{
          ...update.announcement,
          sequence: update.sequence,
        }])
      position.announcementSequence = update.sequence
    }
  }

  function applyTranscriptPage(page: TranscriptPage) {
    const roleIndex = roles.value.findIndex(role => role.role === page.role)
    const position = transcriptPositions[page.role]
    if (roleIndex < 0 || !position) {
      completeTranscriptPageRequest(page.role)
      requestTranscriptSynchronization()
      return
    }
    const role = roles.value[roleIndex]
    const {
      transcriptEntries,
      transcriptEntryIndices,
    } = mergeTranscriptPage(page, role)
    indexTranscriptEntries(page.role, transcriptEntryIndices)
    roles.value[roleIndex] = {
      ...role,
      transcriptEntries,
      transcriptEntryIndices,
      transcriptMutation: createTranscriptMutation(page.role, 'merge'),
    }
  }

  function applyArchivedTranscriptEntry(response: ArchivedTranscriptEntry) {
    completeArchivedTranscriptEntryRequest(response.role, response.entryIndex)
    const roleIndex = roles.value.findIndex(
      role => role.role === response.role)
    const position = transcriptPositions[response.role]
    if (roleIndex < 0
      || !position
      || position.visualSequence !== response.sequence)
      return
    const role = roles.value[roleIndex]
    const localIndex = transcriptEntryPositions
      .get(response.role)
      ?.get(response.entryIndex)
    if (localIndex == null)
      return
    const entries = role.transcriptEntries
    const retainedEntry = entries[localIndex]
    const previouslyRenderable = isTranscriptEntryRenderable(retainedEntry)
    entries[localIndex] = resolveArchivedTranscriptEntry(
      retainedEntry,
      response)
    roles.value[roleIndex] = {
      ...role,
      transcriptMutation: createTranscriptMutation(
        response.role,
        'replace',
        response.entryIndex,
        localIndex,
        entries.length,
        previouslyRenderable),
    }
  }

  function requestTranscriptPage(role: string) {
    const roleState = roles.value.find(item => item.role === role)
    requestHistoryPage(
      role,
      roleState?.transcriptEntryIndices,
      transcriptPositions[role] != null)
  }

  function requestTranscriptSynchronization() {
    if (transcriptSynchronizationPending) return
    transcriptSynchronizationPending = true
    send('transcript.synchronize', {
      payload: {
        roles: Object.entries(transcriptPositions)
          .map(([role, position]) => ({
            role,
            visualSequence: position.visualSequence,
            announcementSequence: position.announcementSequence,
          })),
      },
    })
  }

  function createTranscriptMutation(
    role: string,
    kind: TranscriptMutation['kind'],
    entryIndex?: number,
    sourceIndex?: number,
    previousSourceLength?: number,
    previouslyRenderable?: boolean,
  ): TranscriptMutation {
    const generation = (transcriptMutationRevisions.get(role) ?? 0) + 1
    transcriptMutationRevisions.set(role, generation)
    return {
      generation,
      kind,
      entryIndex,
      sourceIndex,
      previousSourceLength,
      previouslyRenderable,
    }
  }

  function indexTranscriptEntries(
    role: string,
    entryIndices: readonly number[],
  ) {
    transcriptEntryPositions.set(role, new Map(
      entryIndices.map(
        (entryIndex, sourceIndex) => [entryIndex, sourceIndex])))
  }

  function isTranscriptEntryRenderable(entry: { content: string }) {
    return entry.content.trim().length > 0
  }

  function dispose() {
    disposeTranscriptAnnouncements()
  }

  return {
    roles: computed<readonly RoleState[]>(() => roles.value),
    rolesWithOlderTranscript,
    rolesWithTruncatedTranscript,
    publishedAnnouncementsByRole,
    applySnapshot,
    applyTranscriptSynchronization,
    applyTranscriptUpdate,
    applyTranscriptPage,
    applyArchivedTranscriptEntry,
    requestTranscriptPage,
    requestArchivedTranscriptEntry,
    dispose,
  }
}
