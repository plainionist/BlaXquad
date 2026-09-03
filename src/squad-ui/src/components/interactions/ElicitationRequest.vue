<script setup lang="ts">
import { computed } from 'vue'
import type { Elicitation } from '../../protocol/messages'
import ElicitationField from './ElicitationField.vue'

const props = defineProps<{
  elicitation: Elicitation
  values: Readonly<Record<string, unknown>>
}>()

const emit = defineEmits<{
  'update:field': [name: string, value: unknown]
  submit: []
  respond: [action: 'accept' | 'decline' | 'cancel']
}>()

const schemaProperties = computed(() =>
  Object.entries(props.elicitation.requestedSchema?.properties ?? {}))

function isRequired(name: string) {
  return props.elicitation.requestedSchema?.required?.includes(name) ?? false
}

function urlHost(value?: string) {
  if (!value) return ''
  try { return new URL(value).host }
  catch { return 'Invalid URL' }
}
</script>

<template>
  <section class="interaction">
    <h2>{{ elicitation.mode === 'url' ? 'Action required' : 'Form requested' }}</h2>
    <p>{{ elicitation.prompt }}</p>
    <div v-if="elicitation.mode === 'url' && elicitation.url" class="url-consent">
      <strong>{{ urlHost(elicitation.url) }}</strong>
      <code>{{ elicitation.url }}</code>
      <div class="action-row">
        <button class="primary" type="button" @click="emit('respond', 'accept')">Open requested page</button>
        <button class="secondary" type="button" @click="emit('respond', 'cancel')">Cancel request</button>
      </div>
    </div>
    <form v-else class="schema-form" @submit.prevent="emit('submit')">
      <ElicitationField
        v-for="[name, property] in schemaProperties"
        :key="name"
        :name="name"
        :property="property"
        :required="isRequired(name)"
        :value="values[name]"
        @update:value="emit('update:field', name, $event)"
      />
      <div class="action-row">
        <button class="primary" type="submit">Submit</button>
        <button class="secondary" type="button" @click="emit('respond', 'decline')">Decline</button>
        <button class="text-button" type="button" @click="emit('respond', 'cancel')">Cancel request</button>
      </div>
    </form>
  </section>
</template>
