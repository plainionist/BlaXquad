<script setup lang="ts">
import ProtocolErrorBanner from './components/ProtocolErrorBanner.vue'
import RolePanel from './components/RolePanel.vue'
import { useDashboardSession } from './composables/useDashboardSession'

const {
  roles,
  hasRoles,
  protocolError,
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
} = useDashboardSession()
</script>

<template>
  <main class="workspace">
    <ProtocolErrorBanner
      v-if="protocolError"
      :error-message="protocolError"
      @dismiss="dismissProtocolError"
    />

    <section v-if="hasRoles" class="role-grid" aria-label="Agent roles">
      <RolePanel
        v-for="role in roles"
        :key="role.role"
        :role="role"
        :permissions="permissionsFor(role.role)"
        :inputs="inputsFor(role.role)"
        :elicitations="elicitationsFor(role.role)"
        :prompt="promptFor(role.role)"
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
