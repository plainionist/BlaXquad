<script setup lang="ts">
import { computed } from 'vue'
import type { InputRequest } from '../../protocol/messages'

const props = defineProps<{
  input: InputRequest
  draft: string
  roleName: string
}>()

const emit = defineEmits<{
  'update:draft': [draft: string]
  choose: [requestId: string, answer: string]
  submit: [requestId: string]
}>()

const draftModel = computed({
  get: () => props.draft,
  set: (draft: string) => emit('update:draft', draft),
})
</script>

<template>
  <section class="interaction">
    <h2>Input requested</h2>
    <p>{{ input.prompt }}</p>
    <div v-if="input.choices?.length" class="choice-row" role="group" :aria-label="input.prompt">
      <button v-for="choice in input.choices" :key="choice" type="button" class="choice" @click="emit('choose', input.requestId, choice)">{{ choice }}</button>
    </div>
    <form v-if="input.allowFreeform" class="inline-form" @submit.prevent="emit('submit', input.requestId)">
      <label class="sr-only" :for="`input-${input.requestId}`">Response for {{ roleName }}</label>
      <input :id="`input-${input.requestId}`" v-model="draftModel" placeholder="Type a response" autocomplete="off">
      <button class="primary" type="submit">Send</button>
    </form>
  </section>
</template>
