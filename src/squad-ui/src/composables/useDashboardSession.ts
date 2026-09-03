import { computed, onMounted, onUnmounted, readonly, ref } from 'vue'
import { createBridge } from '../protocol/bridge'
import type {
  Elicitation,
  InputRequest,
  Permission,
  Snapshot,
} from '../protocol/messages'
import { useFocusedRoleAbort } from './useFocusedRoleAbort'
import { useInteractionDrafts } from './useInteractionDrafts'
import { useRoleInteractions } from './useRoleInteractions'
import { useTranscriptFeed } from './useTranscriptFeed'

export function useDashboardSession() {
  const bridge = createBridge()
  const permissions = ref<Permission[]>([])
  const inputs = ref<InputRequest[]>([])
  const elicitations = ref<Elicitation[]>([])
  const {
    permissionsFor,
    inputsFor,
    elicitationsFor,
  } = useRoleInteractions(permissions, inputs, elicitations)
  const protocolError = ref<string | null>(null)
  const {
    promptFor,
    updatePrompt,
    clearPrompt,
    inputDraftFor,
    updateInputDraft,
    initializeElicitations,
    elicitationValuesFor,
    updateElicitationValue,
    submittedContentFor,
  } = useInteractionDrafts()
  const {
    roles,
    rolesWithOlderTranscript,
    rolesWithTruncatedTranscript,
    publishedAnnouncementsByRole,
    applySnapshot: applyTranscriptSnapshot,
    applyTranscriptSynchronization,
    applyTranscriptUpdate,
    applyTranscriptPage,
    applyArchivedTranscriptEntry,
    requestTranscriptPage,
    requestArchivedTranscriptEntry,
    dispose: disposeTranscriptFeed,
  } = useTranscriptFeed(bridge.send)

  bridge.onSnapshot((snapshot: Snapshot) => {
    applyTranscriptSnapshot(snapshot)
    permissions.value = snapshot.permissions
    inputs.value = snapshot.inputs
    elicitations.value = snapshot.elicitations
    initializeElicitations(snapshot.elicitations)
  })
  bridge.onTranscriptSynchronization(applyTranscriptSynchronization)
  bridge.onTranscriptUpdate(applyTranscriptUpdate)
  bridge.onTranscriptPage(applyTranscriptPage)
  bridge.onArchivedTranscriptEntry(applyArchivedTranscriptEntry)
  bridge.onError((message) => { protocolError.value = message })

  const hasRoles = computed(() => roles.value.length > 0)

  function respondToPermission(
    role: string,
    requestId: string,
    approved: boolean,
  ) {
    bridge.send('permission.respond', {
      role,
      requestId,
      payload: { approved },
    })
  }

  function respondToInput(
    role: string,
    requestId: string,
    answer: string,
    wasFreeform: boolean,
  ) {
    bridge.send('input.respond', {
      role,
      requestId,
      payload: { answer, wasFreeform },
    })
  }

  function submitElicitation(role: string, requestId: string) {
    bridge.send('elicitation.respond', {
      role,
      requestId,
      payload: {
        action: 'accept',
        content: submittedContentFor(requestId),
      },
    })
  }

  function respondToElicitation(
    role: string,
    requestId: string,
    action: 'accept' | 'decline' | 'cancel',
  ) {
    bridge.send('elicitation.respond', {
      role,
      requestId,
      payload: { action },
    })
  }

  function sendPrompt(role: string) {
    const prompt = promptFor(role).trim()
    if (!prompt) return
    bridge.send('prompt.send', { role, payload: { prompt } })
    clearPrompt(role)
  }

  function cancelRole(role: string) {
    bridge.send('role.abort', { role })
  }

  function dismissProtocolError() {
    protocolError.value = null
  }

  onMounted(() => bridge.send('ui.ready'))
  const focusRole = useFocusedRoleAbort(cancelRole)
  onUnmounted(() => {
    disposeTranscriptFeed()
    bridge.dispose()
  })

  return {
    roles,
    hasRoles,
    protocolError: readonly(protocolError),
    rolesWithOlderTranscript,
    rolesWithTruncatedTranscript,
    publishedAnnouncementsByRole,
    permissionsFor,
    inputsFor,
    elicitationsFor,
    promptFor,
    inputDraftFor,
    elicitationValuesFor,
    focusRole,
    requestTranscriptPage,
    requestArchivedTranscriptEntry,
    respondToPermission,
    updateInputDraft,
    respondToInput,
    updateElicitationValue,
    submitElicitation,
    respondToElicitation,
    updatePrompt,
    sendPrompt,
    cancelRole,
    dismissProtocolError,
  }
}
