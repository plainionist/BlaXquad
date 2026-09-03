<script setup lang="ts">
import type { RoleState } from '../protocol/messages'

defineProps<{
  role: RoleState
  working: boolean
}>()

function formatAic(value?: number | null) { return value == null ? 'AIC unavailable' : `${value.toLocaleString(undefined, { maximumFractionDigits: 2 })} AIC` }
function contextFill(role: RoleState) {
  const used = role.contextUsedTokens
  const limit = role.contextLimitTokens
  return used != null && limit != null && limit > 0 ? Math.min(100, Math.max(0, used / limit * 100)) : null
}
function contextTone(role: RoleState) {
  const used = role.contextUsedTokens
  const fill = contextFill(role)
  if (used == null || fill == null) return 'safe'
  if (fill >= 80) return 'critical'
  return used < 150_000 ? 'safe' : 'warning'
}
function formatTokensInK(value?: number | null) { return `${Math.round((value ?? 0) / 1000)}k` }
function contextLabel(role: RoleState) {
  if (role.contextUsedTokens == null || role.contextLimitTokens == null) return 'Context usage unavailable'
  return `${formatTokensInK(role.contextUsedTokens)}/${formatTokensInK(role.contextLimitTokens)}`
}
</script>

<template>
  <header class="agent-header">
    <div class="agent-title">
      <h1>{{ role.role }}</h1>
      <span v-if="working" class="working-dot" role="status" aria-label="Working" title="Working"></span>
    </div>
    <div class="context-meter" :class="`is-${contextTone(role)}`" :title="contextLabel(role)" :aria-label="contextLabel(role)">
      <span class="context-meter-fill" :style="{ width: `${contextFill(role) ?? 0}%` }"></span>
    </div>
    <div class="agent-details">
      <span :title="`Selected model: ${role.model ?? 'default'}`">{{ role.model ?? 'default model' }}</span>
      <span v-if="role.effort && role.effort.toLowerCase() !== 'default'" :title="`Reasoning effort: ${role.effort}`">{{ role.effort }}</span>
      <span title="AI credits used in this session">{{ formatAic(role.aicUsed) }}</span>
    </div>
  </header>
</template>
