<script setup lang="ts">
import { computed } from 'vue'
import type { ElicitationProperty } from '../../protocol/messages'

const props = defineProps<{
  name: string
  property: ElicitationProperty
  required: boolean
  value: unknown
}>()

const emit = defineEmits<{
  'update:value': [value: unknown]
}>()

const valueModel = computed({
  get: () => props.value,
  set: (value: unknown) => emit('update:value', value),
})

const choices = computed(() => {
  if (props.property.oneOf)
    return props.property.oneOf.map(choice => ({
      value: choice.const,
      title: choice.title ?? String(choice.const),
    }))
  return (props.property.enum ?? []).map(value => ({
    value,
    title: String(value),
  }))
})

const itemChoices = computed(() => {
  if (props.property.items?.oneOf)
    return props.property.items.oneOf.map(choice => ({
      value: choice.const,
      title: choice.title ?? choice.const,
    }))
  return (props.property.items?.enum ?? []).map(value => ({
    value,
    title: value,
  }))
})
</script>

<template>
  <label class="schema-field">
    <span>{{ property.title ?? name }}<span v-if="required" aria-hidden="true"> *</span></span>
    <input
      v-if="property.type === 'boolean'"
      v-model="valueModel"
      type="checkbox"
    >
    <select
      v-else-if="property.type === 'array'"
      v-model="valueModel"
      multiple
      :required="required"
      :size="Math.min(5, Math.max(2, itemChoices.length))"
    >
      <option v-for="choice in itemChoices" :key="String(choice.value)" :value="choice.value">{{ choice.title }}</option>
    </select>
    <select
      v-else-if="choices.length"
      v-model="valueModel"
      :required="required"
    >
      <option value="" disabled>Select an option</option>
      <option v-for="choice in choices" :key="String(choice.value)" :value="choice.value">{{ choice.title }}</option>
    </select>
    <input
      v-else-if="property.type === 'number' || property.type === 'integer'"
      v-model.number="valueModel"
      type="number"
      :step="property.type === 'integer' ? '1' : property.type === 'number' ? 'any' : undefined"
      :required="required"
      :min="property.minimum"
      :max="property.maximum"
      autocomplete="off"
    >
    <input
      v-else
      v-model="valueModel"
      type="text"
      :required="required"
      :minlength="property.minLength"
      :maxlength="property.maxLength"
      :pattern="property.pattern"
      autocomplete="off"
    >
  </label>
</template>
