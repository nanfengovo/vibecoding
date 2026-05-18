<template>
  <div class="metric-card glass-panel glow-border">
    <div class="metric-header">
      <span class="metric-label">{{ title }}</span>
      <el-icon v-if="icon" class="metric-icon"><component :is="icon" /></el-icon>
    </div>
    
    <div class="metric-body">
      <div class="metric-value" :class="valueClass">{{ value }}</div>
    </div>
    
    <div class="metric-footer">
      <div v-if="change !== undefined" class="metric-change" :class="changeClass">
        <el-icon v-if="change !== 0">
          <component :is="change > 0 ? 'CaretTop' : 'CaretBottom'" />
        </el-icon>
        <span>{{ Math.abs(change).toFixed(2) }}%</span>
      </div>
      <div v-if="subtitle" class="metric-subtitle">{{ subtitle }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  title: string
  value: string | number
  change?: number
  subtitle?: string
  icon?: string
  valueColor?: 'up' | 'down' | 'default'
}>()

const valueClass = computed(() => {
  if (props.valueColor === 'up') return 'color-up'
  if (props.valueColor === 'down') return 'color-down'
  return ''
})

const changeClass = computed(() => {
  if (props.change === undefined || props.change === 0) return 'color-flat'
  return props.change > 0 ? 'color-up' : 'color-down'
})
</script>

<style lang="scss" scoped>
.metric-card {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.metric-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  
  .metric-label {
    font-size: 14px;
    font-weight: 500;
    color: var(--qt-text-secondary);
  }
  
  .metric-icon {
    font-size: 16px;
    color: var(--qt-text-muted);
  }
}

.metric-body {
  .metric-value {
    font-size: 28px;
    font-weight: 700;
    line-height: 1.2;
    color: var(--qt-text);
  }
}

.metric-footer {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  
  .metric-change {
    display: inline-flex;
    align-items: center;
    font-weight: 600;
    padding: 2px 6px;
    border-radius: 4px;
    background: rgba(255, 255, 255, 0.05);
    
    :global(.color-up) {
      background: rgba(16, 185, 129, 0.1);
    }
    :global(.color-down) {
      background: rgba(239, 68, 68, 0.1);
    }
  }
  
  .metric-subtitle {
    color: var(--qt-text-muted);
  }
}
</style>
