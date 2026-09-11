<script setup lang="ts">
import { computed, ref } from 'vue'

const props = defineProps<{
  role: string
  status: string
  prompt: string
  historyNavigating: boolean
  recallOlderPrompt: (role: string) => boolean
  recallNewerPrompt: (role: string) => boolean
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

const textarea = ref<HTMLTextAreaElement | null>(null)

function focusPrompt() {
  textarea.value?.focus()
}

function onArrowUp(event: KeyboardEvent) {
  if (event.isComposing) return
  if (promptModel.value !== '' && !props.historyNavigating) return
  if (props.recallOlderPrompt(props.role)) event.preventDefault()
}

function onArrowDown(event: KeyboardEvent) {
  if (event.isComposing) return
  if (!props.historyNavigating) return
  if (props.recallNewerPrompt(props.role)) event.preventDefault()
}

defineExpose({ focusPrompt })
</script>

<template>
  <form class="composer" @submit.prevent="emit('send')">
    <label class="sr-only" :for="`prompt-${role}`">Message {{ role }}</label>
    <textarea
      :id="`prompt-${role}`"
      ref="textarea"
      v-model="promptModel"
      rows="1"
      placeholder="Message this role"
      @keydown.enter.exact.prevent="emit('send')"
      @keydown.up.exact="onArrowUp"
      @keydown.down.exact="onArrowDown"
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
