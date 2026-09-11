<script setup lang="ts">
// Decorative document/list glyph for the Issues action. Locally authored and dependency-free; never fetched
// at runtime. Purely presentational - the accessible name comes from the button's aria-label, not this icon.
</script>

<template>
  <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
    <rect x="2.5" y="1.5" width="11" height="13" rx="1" fill="none" stroke="currentColor" stroke-width="1.3" />
    <line x1="4.5" y1="5" x2="11.5" y2="5" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" />
    <line x1="4.5" y1="8" x2="11.5" y2="8" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" />
    <line x1="4.5" y1="11" x2="9.5" y2="11" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" />
  </svg>
</template>
