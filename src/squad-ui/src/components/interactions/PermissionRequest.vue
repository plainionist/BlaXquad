<script setup lang="ts">
import type { Permission } from '../../protocol/messages'

defineProps<{
  permission: Permission
}>()

const emit = defineEmits<{
  respond: [requestId: string, approved: boolean]
}>()
</script>

<template>
  <section class="interaction permission">
    <div class="interaction-copy">
      <h2>Permission required</h2>
      <p>{{ permission.description }}</p>
    </div>
    <div class="action-row">
      <button class="secondary" type="button" @click="emit('respond', permission.requestId, false)">Reject</button>
      <button class="primary" type="button" @click="emit('respond', permission.requestId, true)">Approve</button>
    </div>
  </section>
</template>
