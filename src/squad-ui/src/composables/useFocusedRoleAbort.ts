import { onMounted, onUnmounted } from 'vue'

export function useFocusedRoleAbort(abort: (role: string) => void) {
  let focusedRole: string | null = null
  let lastEscapeAt = 0

  function handleKeyDown(event: KeyboardEvent) {
    if (event.key !== 'Escape' || !focusedRole)
      return
    const now = Date.now()
    if (now - lastEscapeAt <= 500) {
      event.preventDefault()
      abort(focusedRole)
      lastEscapeAt = 0
      return
    }
    lastEscapeAt = now
  }

  onMounted(() => window.addEventListener('keydown', handleKeyDown))
  onUnmounted(() => window.removeEventListener('keydown', handleKeyDown))

  return (role: string) => {
    focusedRole = role
  }
}
