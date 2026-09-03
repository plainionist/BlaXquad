import { ref } from 'vue'
import type { Elicitation } from '../protocol/messages'

export function useInteractionDrafts() {
  const prompts = ref<Record<string, string>>({})
  const inputDrafts = ref<Record<string, string>>({})
  const elicitationValues =
    ref<Record<string, Record<string, unknown>>>({})

  function promptFor(role: string) {
    return prompts.value[role] ?? ''
  }

  function updatePrompt(role: string, prompt: string) {
    prompts.value[role] = prompt
  }

  function clearPrompt(role: string) {
    prompts.value[role] = ''
  }

  function inputDraftFor(requestId: string) {
    return inputDrafts.value[requestId] ?? ''
  }

  function updateInputDraft(requestId: string, draft: string) {
    inputDrafts.value[requestId] = draft
  }

  function initializeElicitations(elicitations: readonly Elicitation[]) {
    for (const elicitation of elicitations) {
      if (elicitationValues.value[elicitation.requestId])
        continue
      const values: Record<string, unknown> = {}
      for (const [name, property] of Object.entries(
        elicitation.requestedSchema?.properties ?? {},
      )) {
        if (property.default !== undefined)
          values[name] = property.default
        else if (property.type === 'boolean')
          values[name] = false
        else if (property.type === 'array')
          values[name] = []
        else
          values[name] = ''
      }
      elicitationValues.value[elicitation.requestId] = values
    }
  }

  function elicitationValuesFor(requestId: string) {
    return elicitationValues.value[requestId] ?? {}
  }

  function updateElicitationValue(
    requestId: string,
    name: string,
    value: unknown,
  ) {
    elicitationValues.value[requestId][name] = value
  }

  function submittedContentFor(requestId: string) {
    return elicitationValues.value[requestId] ?? {}
  }

  return {
    promptFor,
    updatePrompt,
    clearPrompt,
    inputDraftFor,
    updateInputDraft,
    initializeElicitations,
    elicitationValuesFor,
    updateElicitationValue,
    submittedContentFor,
  }
}
