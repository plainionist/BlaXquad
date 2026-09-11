<script setup lang="ts">
import type { IssueDescriptor } from '../protocol/messages'
import IssueExplorer from './IssueExplorer.vue'
import GitBranchIcon from './icons/GitBranchIcon.vue'

defineProps<{
  issues: readonly IssueDescriptor[]
  issuesLoading: boolean
  catalogError: string | null
  hasTargetRole: boolean
  gitHistoryAvailable: boolean
}>()

const emit = defineEmits<{
  'open-issues': []
  'play-issue': [path: string]
  'open-git-history': []
}>()
</script>

<template>
  <div class="workspace-toolbar">
    <IssueExplorer
      :issues="issues"
      :is-loading="issuesLoading"
      :error="catalogError"
      :has-target-role="hasTargetRole"
      @open="emit('open-issues')"
      @play="path => emit('play-issue', path)"
    />

    <button
      type="button"
      class="toolbar-icon-button"
      aria-label="Git history"
      title="Git history"
      :disabled="!gitHistoryAvailable"
      @click="emit('open-git-history')"
    >
      <GitBranchIcon />
    </button>
  </div>
</template>
