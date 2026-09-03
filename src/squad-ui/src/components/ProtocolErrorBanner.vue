<script setup lang="ts">
defineProps<{
  errorMessage: string
}>()

const emit = defineEmits<{
  dismiss: []
}>()
</script>

<template>
  <p class="protocol-error" role="alert">
    {{ errorMessage }}
    <button class="dismiss" type="button" aria-label="Dismiss protocol error" title="Dismiss" @click="emit('dismiss')">x</button>
  </p>
</template>
