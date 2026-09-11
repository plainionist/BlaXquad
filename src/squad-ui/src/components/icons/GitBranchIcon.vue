<script setup lang="ts">
// Decorative branching/history glyph for the Git history action. Locally authored and dependency-free; never
// fetched at runtime. Purely presentational - the accessible name comes from the button's aria-label, not this icon.
</script>

<template>
  <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
    <circle cx="5" cy="3.5" r="1.6" fill="none" stroke="currentColor" stroke-width="1.4" />
    <circle cx="5" cy="12.5" r="1.6" fill="none" stroke="currentColor" stroke-width="1.4" />
    <circle cx="11.5" cy="5.5" r="1.6" fill="none" stroke="currentColor" stroke-width="1.4" />
    <path d="M5 5.1V10.9" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" />
    <path d="M5 9.5Q11.5 9.5 11.5 7.1" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round" />
  </svg>
</template>
