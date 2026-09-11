<script setup lang="ts">
// Decorative branching/history glyph for the Git history action. Locally authored and dependency-free; never
// fetched at runtime. Purely presentational - the accessible name comes from the button's aria-label, not this icon.
</script>

<template>
  <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
    <circle cx="4" cy="3.5" r="1.75" fill="none" stroke="currentColor" stroke-width="1.25" />
    <circle cx="4" cy="12.5" r="1.75" fill="none" stroke="currentColor" stroke-width="1.25" />
    <circle cx="12" cy="5.5" r="1.75" fill="none" stroke="currentColor" stroke-width="1.25" />
    <path d="M4 5.25V10.75" fill="none" stroke="currentColor" stroke-width="1.25" />
    <path d="M4 9.5C4 7.2 6.8 5.5 10.25 5.5" fill="none" stroke="currentColor" stroke-width="1.25" stroke-linecap="round" />
  </svg>
</template>
