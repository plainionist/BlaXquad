<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  role: string
  status: string
  prompt: string
}>()

const emit = defineEmits<{
  'update:prompt': [prompt: string]
  send: []
  cancel: []
  'scroll-to-end': []
}>()

const promptModel = computed({
  get: () => props.prompt,
  set: (prompt: string) => emit('update:prompt', prompt),
})
</script>

<template>
  <form class="composer" @submit.prevent="emit('send')">
    <label class="sr-only" :for="`prompt-${role}`">Message {{ role }}</label>
    <textarea
      :id="`prompt-${role}`"
      v-model="promptModel"
      rows="1"
      placeholder="Message this role"
      @keydown.enter.exact.prevent="emit('send')"
    />
    <div class="composer-actions">
      <button class="primary" type="submit" :disabled="!prompt.trim()">Send</button>
      <button class="cancel" type="button" :disabled="status === 'stopped'" title="Cancel current response" @click="emit('cancel')">Cancel</button>
      <button
        class="scroll-to-end"
        type="button"
        aria-label="Scroll terminal output to end"
        title="Scroll terminal output to end"
        @click="emit('scroll-to-end')"
      >
        &#x2193;
      </button>
    </div>
  </form>
</template>
