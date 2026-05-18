<template>
  <div class="stock-card glass-panel glow-border" @click="$emit('click', item)">
    <div class="item-header">
      <div class="item-title">
        <div class="symbol">{{ item.symbol }}</div>
        <div class="name">{{ item.name }}</div>
      </div>
      <div class="item-price" :class="item.change >= 0 ? 'color-up' : 'color-down'">
        {{ item.price.toFixed(2) }}
      </div>
    </div>
    
    <div class="item-change" :class="item.change >= 0 ? 'color-up' : 'color-down'">
      {{ item.change >= 0 ? '+' : '' }}{{ item.change.toFixed(2) }}
      ({{ item.changePercent >= 0 ? '+' : '' }}{{ item.changePercent.toFixed(2) }}%)
    </div>
    
    <div class="mini-chart">
      <v-chart :option="chartOption" autoresize />
    </div>
    
    <div class="item-meta">
      成交量 {{ formatVolume(item.volume) }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import VChart from 'vue-echarts'

const props = defineProps<{
  item: {
    symbol: string
    name: string
    price: number
    change: number
    changePercent: number
    volume: number
    series: number[]
  }
}>()

defineEmits(['click'])

function formatVolume(value: number): string {
  if (!value) return '-'
  if (value >= 1000000000) return `${(value / 1000000000).toFixed(2)}B`
  if (value >= 1000000) return `${(value / 1000000).toFixed(2)}M`
  if (value >= 1000) return `${(value / 1000).toFixed(2)}K`
  return value.toString()
}

const chartOption = computed(() => {
  const series = props.item.series || []
  const up = series.length > 1 ? series[series.length - 1] >= series[0] : true
  return {
    grid: { left: 0, right: 0, top: 4, bottom: 4 },
    xAxis: {
      type: 'category',
      show: false,
      data: series.map((_, idx) => idx)
    },
    yAxis: { type: 'value', show: false, scale: true },
    tooltip: { show: false },
    series: [{
      type: 'line',
      data: series,
      smooth: true,
      symbol: 'none',
      lineStyle: {
        width: 2,
        color: up ? '#10b981' : '#ef4444' // 使用涨跌色变量对应的十六进制
      },
      areaStyle: {
        color: up ? 'rgba(16,185,129,0.15)' : 'rgba(239,68,68,0.15)'
      }
    }]
  }
})
</script>

<style lang="scss" scoped>
.stock-card {
  padding: 16px;
  cursor: pointer;
  display: flex;
  flex-direction: column;
  height: 100%;
}

.item-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 8px;
}

.symbol {
  font-weight: 700;
  font-size: 16px;
  color: var(--qt-text);
  letter-spacing: 0.5px;
}

.name {
  color: var(--qt-text-secondary);
  font-size: 12px;
  margin-top: 2px;
}

.item-price {
  font-weight: 700;
  font-size: 20px;
  letter-spacing: -0.5px;
}

.item-change {
  margin-top: 4px;
  font-size: 13px;
  font-weight: 600;
}

.mini-chart {
  height: 60px;
  margin-top: 16px;
  margin-bottom: 8px;
  flex-grow: 1;
}

.item-meta {
  color: var(--qt-text-muted);
  font-size: 12px;
  display: flex;
  justify-content: space-between;
  border-top: 1px dashed var(--qt-border);
  padding-top: 8px;
}
</style>
