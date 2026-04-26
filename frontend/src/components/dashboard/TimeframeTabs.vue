<template>
  <div class="timeframe-tabs">
    <div 
      v-for="option in options" 
      :key="option.value"
      class="tab-item"
      :class="{ 'is-active': modelValue === option.value }"
      @click="$emit('update:modelValue', option.value); $emit('change', option.value)"
    >
      {{ option.label }}
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  modelValue: string
  options: Array<{ label: string, value: string }>
}>()

defineEmits(['update:modelValue', 'change'])
</script>

<style lang="scss" scoped>
.timeframe-tabs {
  display: inline-flex;
  background: var(--qt-bg-soft);
  padding: 4px;
  border-radius: var(--qt-radius-md);
  border: 1px solid var(--qt-border);
}

.tab-item {
  padding: 4px 12px;
  font-size: 13px;
  font-weight: 500;
  color: var(--qt-text-secondary);
  border-radius: var(--qt-radius-sm);
  cursor: pointer;
  transition: all 0.2s;
  user-select: none;
  
  &:hover {
    color: var(--qt-text);
  }
  
  &.is-active {
    background: var(--qt-surface);
    color: var(--qt-primary);
    box-shadow: var(--qt-shadow-card);
  }
}
</style>
