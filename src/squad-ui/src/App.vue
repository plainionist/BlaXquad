<script setup lang="ts">
import { computed } from 'vue'
import ProtocolErrorBanner from './components/ProtocolErrorBanner.vue'
import IssueExplorer from './components/IssueExplorer.vue'
import RolePanel from './components/RolePanel.vue'
import { useDashboardSession } from './composables/useDashboardSession'

const {
  roles,
  hasRoles,
  leader,
  protocolError,
  rolesWithOlderTranscript,
  rolesWithTruncatedTranscript,
  publishedAnnouncementsByRole,
  issues,
  issuesLoading,
  catalogError,
  requestCatalog,
  permissionsFor,
  inputsFor,
  elicitationsFor,
  promptFor,
  inputDraftFor,
  elicitationValuesFor,
  isHistoryNavigating,
  recallOlderPrompt,
  recallNewerPrompt,
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
} = useDashboardSession()

const rolePanels = new Map<string, InstanceType<typeof RolePanel>>()

function setRolePanelRef(roleName: string, instance: unknown) {
  if (instance) rolePanels.set(roleName, instance as InstanceType<typeof RolePanel>)
  else rolePanels.delete(roleName)
}

const targetRoleName = computed(() => {
  const configuredLeader = leader.value
  if (!configuredLeader) return null
  return roles.value.some(role => role.role === configuredLeader) ? configuredLeader : null
})

function playIssue(path: string) {
  const target = targetRoleName.value
  if (!target) return
  updatePrompt(target, `process this issue: "${path}"`)
  rolePanels.get(target)?.focusPrompt()
}
</script>

<template>
  <main class="workspace">
    <ProtocolErrorBanner
      v-if="protocolError"
      :error-message="protocolError"
      @dismiss="dismissProtocolError"
    />

    <IssueExplorer
      :issues="issues"
      :is-loading="issuesLoading"
      :error="catalogError"
      :has-target-role="targetRoleName !== null"
      @open="requestCatalog"
      @play="playIssue"
    />

    <section v-if="hasRoles" class="role-grid" aria-label="Agent roles">
      <RolePanel
        v-for="role in roles"
        :key="role.role"
        :ref="el => setRolePanelRef(role.role, el)"
        :role="role"
        :permissions="permissionsFor(role.role)"
        :inputs="inputsFor(role.role)"
        :elicitations="elicitationsFor(role.role)"
        :prompt="promptFor(role.role)"
        :history-navigating="isHistoryNavigating(role.role)"
        :recall-older-prompt="recallOlderPrompt"
        :recall-newer-prompt="recallNewerPrompt"
        :input-draft-for="inputDraftFor"
        :elicitation-values-for="elicitationValuesFor"
        :has-older="rolesWithOlderTranscript.has(role.role)"
        :history-truncated="rolesWithTruncatedTranscript.has(role.role)"
        :announcements="publishedAnnouncementsByRole[role.role]"
        @focus="focusRole(role.role)"
        @load-older="requestTranscriptPage(role.role)"
        @load-entry="requestArchivedTranscriptEntry(role.role, $event)"
        @respond-permission="(requestId, approved) => respondToPermission(role.role, requestId, approved)"
        @update-input-draft="updateInputDraft"
        @choose-input="(requestId, answer) => respondToInput(role.role, requestId, answer, false)"
        @submit-input="requestId => respondToInput(role.role, requestId, inputDraftFor(requestId), true)"
        @update-elicitation-field="updateElicitationValue"
        @submit-elicitation="requestId => submitElicitation(role.role, requestId)"
        @respond-elicitation="(requestId, action) => respondToElicitation(role.role, requestId, action)"
        @update-prompt="updatePrompt(role.role, $event)"
        @send-prompt="sendPrompt(role.role)"
        @cancel="cancelRole(role.role)"
      />
    </section>

    <section v-else class="empty-state" aria-live="polite">
      <h1>Awaiting roles</h1>
      <p>The host will populate this view when its agent sessions begin.</p>
    </section>
  </main>
</template>
