<script setup lang="ts">
// Decorative branching/history glyph for the Git history action. Locally authored and dependency-free; never
// fetched at runtime. Purely presentational - the accessible name comes from the button's aria-label, not this icon.
</script>

<template>
  <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
    <circle cx="4" cy="3" r="1.6" fill="none" stroke="currentColor" stroke-width="1.3" />
    <circle cx="4" cy="13" r="1.6" fill="none" stroke="currentColor" stroke-width="1.3" />
    <circle cx="12" cy="8" r="1.6" fill="none" stroke="currentColor" stroke-width="1.3" />
    <path d="M4 4.6V11.4" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" />
    <path d="M4 7.2c0-2 1.8-2.9 4-2.9s4 0.8 4 2.4" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" />
  </svg>
</template>
