import { ref } from 'vue'

const MAX_ENTRIES_PER_ROLE = 50

/**
 * Tracks each role's recently submitted prompts in memory only, so an operator
 * can recall and resend them for the lifetime of the current dashboard.
 */
export function usePromptHistory() {
  const historyByRole = ref<Record<string, string[]>>({})
  const navigationIndexByRole = ref<Record<string, number | null>>({})
  const preservedDraftByRole = ref<Record<string, string>>({})

  function resetNavigation(role: string) {
    navigationIndexByRole.value[role] = null
    delete preservedDraftByRole.value[role]
  }

  function recordSubmission(role: string, prompt: string) {

    if (!prompt) return

    const history = historyByRole.value[role] ?? []

    history.unshift(prompt)

    if (history.length > MAX_ENTRIES_PER_ROLE)
      history.length = MAX_ENTRIES_PER_ROLE

    historyByRole.value[role] = history

    resetNavigation(role)
  }

  function isNavigating(role: string) {
    return (navigationIndexByRole.value[role] ?? null) !== null
  }

  function exitNavigation(role: string) {
    resetNavigation(role)
  }

  function recallOlder(role: string, currentDraft: string): string | null {
    const history = historyByRole.value[role] ?? []

    if (history.length === 0) return null

    const currentIndex = navigationIndexByRole.value[role] ?? null

    if (currentIndex === null) {
      preservedDraftByRole.value[role] = currentDraft
      navigationIndexByRole.value[role] = 0
      return history[0]
    }

    const nextIndex = currentIndex + 1

    if (nextIndex >= history.length) return null

    navigationIndexByRole.value[role] = nextIndex

    return history[nextIndex]
  }

  function recallNewer(role: string): string | null {
    const currentIndex = navigationIndexByRole.value[role] ?? null

    if (currentIndex === null) return null

    const nextIndex = currentIndex - 1

    if (nextIndex < 0) {
      const draft = preservedDraftByRole.value[role] ?? ''
      resetNavigation(role)
      return draft
    }

    navigationIndexByRole.value[role] = nextIndex
    return historyByRole.value[role]?.[nextIndex] ?? null
  }

  return {
    recordSubmission,
    isNavigating,
    exitNavigation,
    recallOlder,
    recallNewer,
  }
}
