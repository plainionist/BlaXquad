<script setup lang="ts">
import { computed, ref } from 'vue'
import type {
  Elicitation,
  InputRequest,
  Permission,
  RoleState,
} from '../protocol/messages'
import PromptComposer from './PromptComposer.vue'
import RoleHeader from './RoleHeader.vue'
import ElicitationRequest from './interactions/ElicitationRequest.vue'
import InputRequestView from './interactions/InputRequest.vue'
import PermissionRequest from './interactions/PermissionRequest.vue'
import VirtualTranscript from './transcript/VirtualTranscript.vue'

const props = defineProps<{
  role: RoleState
  permissions: readonly Permission[]
  inputs: readonly InputRequest[]
  elicitations: readonly Elicitation[]
  prompt: string
  inputDraftFor: (requestId: string) => string
  elicitationValuesFor: (
    requestId: string,
  ) => Readonly<Record<string, unknown>>
  hasOlder: boolean
  historyTruncated: boolean
  announcements?: readonly { id: number; content: string }[]
}>()

const emit = defineEmits<{
  focus: []
  'load-older': []
  'load-entry': [entryIndex: number]
  'respond-permission': [requestId: string, approved: boolean]
  'update-input-draft': [requestId: string, draft: string]
  'choose-input': [requestId: string, answer: string]
  'submit-input': [requestId: string]
  'update-elicitation-field': [requestId: string, name: string, value: unknown]
  'submit-elicitation': [requestId: string]
  'respond-elicitation': [
    requestId: string,
    action: 'accept' | 'decline' | 'cancel',
  ]
  'update-prompt': [prompt: string]
  'send-prompt': []
  cancel: []
}>()

const transcript = ref<InstanceType<typeof VirtualTranscript> | null>(null)
const working = computed(() =>
  props.role.isWorking || props.role.status !== 'idle')

function scrollTranscriptToEnd() {
  transcript.value?.scrollToEnd()
}
</script>

<template>
  <article class="role-panel" :class="{ 'is-working': working }" @focusin="emit('focus')">
    <RoleHeader :role="role" :working="working" />

    <p v-if="role.error" class="role-error" role="alert">{{ role.error }}</p>

    <div class="console-frame">
      <VirtualTranscript
        ref="transcript"
        :entries="role.transcriptEntries"
        :entry-indices="role.transcriptEntryIndices"
        :mutation="role.transcriptMutation"
        :is-working="role.isWorking"
        :active-tool="role.activeTool"
        :has-older="hasOlder"
        :history-truncated="historyTruncated"
        :announcements="announcements"
        @load-older="emit('load-older')"
        @load-entry="emit('load-entry', $event)"
      />
    </div>

    <PermissionRequest
      v-for="permission in permissions"
      :key="permission.requestId"
      :permission="permission"
      @respond="(requestId, approved) => emit('respond-permission', requestId, approved)"
    />

    <InputRequestView
      v-for="input in inputs"
      :key="input.requestId"
      :input="input"
      :draft="inputDraftFor(input.requestId)"
      :role-name="role.role"
      @update:draft="emit('update-input-draft', input.requestId, $event)"
      @choose="(requestId, answer) => emit('choose-input', requestId, answer)"
      @submit="emit('submit-input', $event)"
    />

    <ElicitationRequest
      v-for="elicitation in elicitations"
      :key="elicitation.requestId"
      :elicitation="elicitation"
      :values="elicitationValuesFor(elicitation.requestId)"
      @update:field="(name, value) => emit('update-elicitation-field', elicitation.requestId, name, value)"
      @submit="emit('submit-elicitation', elicitation.requestId)"
      @respond="action => emit('respond-elicitation', elicitation.requestId, action)"
    />

    <PromptComposer
      :role="role.role"
      :status="role.status"
      :prompt="prompt"
      @update:prompt="emit('update-prompt', $event)"
      @send="emit('send-prompt')"
      @cancel="emit('cancel')"
      @scroll-to-end="scrollTranscriptToEnd"
    />
  </article>
</template>
