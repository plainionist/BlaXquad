<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { IssueDescriptor } from '../protocol/messages'

const props = defineProps<{
  issues: readonly IssueDescriptor[]
  isLoading: boolean
  error: string | null
}>()

const emit = defineEmits<{
  open: []
}>()

const root = ref<HTMLElement | null>(null)
const trigger = ref<HTMLButtonElement | null>(null)
const isOpen = ref(false)
const selectedPath = ref<string | null>(null)

const selectedIssue = computed(
  () => props.issues.find(issue => issue.path === selectedPath.value) ?? null,
)
const previewText = computed(
  () => selectedIssue.value?.previewLines.join('\n') ?? '',
)

function toggle() {
  if (isOpen.value) close()
  else open()
}

function open() {
  isOpen.value = true
  selectedPath.value = null
  emit('open')
}

function close() {
  isOpen.value = false
  selectedPath.value = null
}

function closeAndRefocusTrigger() {
  close()
  trigger.value?.focus()
}

function select(path: string) {
  selectedPath.value = path
}

function handlePointerDown(event: PointerEvent) {
  if (!isOpen.value) return
  if (root.value?.contains(event.target as Node)) return
  close()
}

function handleKeyDown(event: KeyboardEvent) {
  if (!isOpen.value || event.key !== 'Escape') return
  event.preventDefault()
  closeAndRefocusTrigger()
}

onMounted(() => {
  document.addEventListener('pointerdown', handlePointerDown)
  document.addEventListener('keydown', handleKeyDown)
})
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', handlePointerDown)
  document.removeEventListener('keydown', handleKeyDown)
})
</script>

<template>
  <div ref="root" class="issue-toolbar">
    <button
      ref="trigger"
      type="button"
      class="issue-trigger"
      aria-haspopup="listbox"
      :aria-expanded="isOpen"
      @click="toggle"
    >
      Issues
    </button>

    <div v-if="isOpen" class="issue-panel">
      <p v-if="isLoading" class="issue-status" aria-live="polite">Loading issues…</p>
      <p v-else-if="error" class="issue-status issue-status-error" role="alert">{{ error }}</p>
      <ul v-else class="issue-menu" role="listbox" aria-label="Issues">
        <li
          v-if="issues.length === 0"
          class="issue-menu-empty"
          role="option"
          aria-disabled="true"
          aria-selected="false"
        >
          {{ '<no issues>' }}
        </li>
        <li
          v-for="issue in issues"
          :key="issue.path"
          class="issue-menu-item"
          :class="{ 'is-selected': selectedPath === issue.path }"
          role="option"
          tabindex="0"
          :aria-selected="selectedPath === issue.path"
          @mouseenter="select(issue.path)"
          @focus="select(issue.path)"
        >
          {{ issue.title }}
        </li>
      </ul>

      <div v-if="selectedIssue" class="issue-flyout" role="region" aria-label="Issue preview">
        <pre class="issue-flyout-frontmatter">{{ selectedIssue.frontmatter }}</pre>
        <pre class="issue-flyout-preview">{{ previewText }}</pre>
      </div>
    </div>
  </div>
</template>
