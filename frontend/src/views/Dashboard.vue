<template>
  <div class="dashboard">
    <div class="page-header">
      <h1>仪表盘</h1>
      <div class="header-actions">
        <el-button type="primary" :icon="Refresh" @click="refreshData">刷新数据</el-button>
      </div>
    </div>

    <el-row :gutter="20" class="stat-cards">
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-label">今日盈亏</div>
          <div :class="['stat-value', todayPnl >= 0 ? 'price-up' : 'price-down']">
            {{ formatCurrency(todayPnl) }}
          </div>
          <div :class="['stat-change', todayPnlPercent >= 0 ? 'positive' : 'negative']">
            <el-icon><component :is="todayPnlPercent >= 0 ? 'CaretTop' : 'CaretBottom'" /></el-icon>
            {{ Math.abs(todayPnlPercent).toFixed(2) }}%
          </div>
        </div>
      </el-col>
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-label">总资产</div>
          <div class="stat-value">{{ formatCurrency(totalAssets) }}</div>
          <div class="stat-change">
            <span class="stat-subtitle">可用现金: {{ formatCurrency(availableCash) }}</span>
          </div>
        </div>
      </el-col>
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-label">运行中策略</div>
          <div class="stat-value">{{ activeStrategiesCount }}</div>
          <div class="stat-change">
            <span class="stat-subtitle">共 {{ totalStrategiesCount }} 个策略</span>
          </div>
        </div>
      </el-col>
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-label">今日交易</div>
          <div class="stat-value">{{ todayTradesCount }}</div>
          <div class="stat-change">
            <span class="stat-subtitle">成交金额: {{ formatCurrency(todayVolume) }}</span>
          </div>
        </div>
      </el-col>
    </el-row>

    <div class="card watch-board">
      <div class="board-header">
        <h3>多股同列（关注列表）</h3>
        <el-radio-group v-model="boardPeriod" size="small" @change="loadBoardData">
          <el-radio-button value="1">1分</el-radio-button>
          <el-radio-button value="5">5分</el-radio-button>
          <el-radio-button value="15">15分</el-radio-button>
          <el-radio-button value="60">60分</el-radio-button>
          <el-radio-button value="D">日K</el-radio-button>
          <el-radio-button value="W">周K</el-radio-button>
          <el-radio-button value="M">月K</el-radio-button>
          <el-radio-button value="Y">年K</el-radio-button>
        </el-radio-group>
      </div>

      <el-skeleton v-if="boardLoading" :rows="6" animated />
      <div v-else-if="boardItems.length === 0" class="board-empty">
        还没有关注股票，先去“股票监控”添加自选吧。
      </div>
      <div v-else class="board-grid">
        <div
          v-for="item in boardItems"
          :key="item.symbol"
          class="board-item"
          @click="$router.push(`/stock/${item.symbol}`)"
        >
          <div class="item-header">
            <div class="item-title">
              <div class="symbol">{{ item.symbol }}</div>
              <div class="name">{{ item.name }}</div>
            </div>
            <div class="item-price" :class="item.change >= 0 ? 'price-up' : 'price-down'">
              {{ item.price.toFixed(2) }}
            </div>
          </div>
          <div class="item-change" :class="item.change >= 0 ? 'price-up' : 'price-down'">
            {{ item.change >= 0 ? '+' : '' }}{{ item.change.toFixed(2) }}
            ({{ item.changePercent >= 0 ? '+' : '' }}{{ item.changePercent.toFixed(2) }}%)
          </div>
          <div class="mini-chart">
            <v-chart :option="buildMiniChartOption(item.series)" autoresize />
          </div>
          <div class="item-meta">成交量 {{ formatVolume(item.volume) }}</div>
        </div>
      </div>
    </div>

    <el-row :gutter="20" class="bottom-content">
      <el-col :span="12">
        <div class="card">
          <div class="card-header">
            <h3>关注列表</h3>
            <el-button type="primary" link @click="$router.push('/watchlist')">
              查看全部
            </el-button>
          </div>
          <el-table :data="watchlistData" style="width: 100%" max-height="300">
            <el-table-column prop="symbol" label="代码" width="110">
              <template #default="{ row }">
                <el-link type="primary" @click="$router.push(`/stock/${row.symbol}`)">
                  {{ row.symbol }}
                </el-link>
              </template>
            </el-table-column>
            <el-table-column prop="name" label="名称" />
            <el-table-column prop="current" label="现价" width="100" align="right">
              <template #default="{ row }">
                {{ row.current?.toFixed(2) || '-' }}
              </template>
            </el-table-column>
            <el-table-column prop="changePercent" label="涨跌幅" width="100" align="right">
              <template #default="{ row }">
                <span :class="(row.changePercent ?? 0) >= 0 ? 'price-up' : 'price-down'">
                  {{ (row.changePercent ?? 0) >= 0 ? '+' : '' }}{{ (row.changePercent ?? 0).toFixed(2) }}%
                </span>
              </template>
            </el-table-column>
          </el-table>
        </div>
      </el-col>

      <el-col :span="12">
        <div class="card">
          <div class="card-header">
            <h3>最近交易</h3>
            <el-button type="primary" link @click="$router.push('/trades')">
              查看全部
            </el-button>
          </div>
          <el-table :data="recentTrades" style="width: 100%" max-height="300">
            <el-table-column prop="symbol" label="代码" width="100" />
            <el-table-column prop="side" label="方向" width="80">
              <template #default="{ row }">
                <el-tag :type="row.side === 'buy' ? 'success' : 'danger'" size="small">
                  {{ row.side === 'buy' ? '买入' : '卖出' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="quantity" label="数量" width="80" />
            <el-table-column prop="price" label="价格" width="100" align="right">
              <template #default="{ row }">
                {{ Number(row.price || row.filledPrice || 0).toFixed(2) }}
              </template>
            </el-table-column>
            <el-table-column prop="executedAt" label="时间">
              <template #default="{ row }">
                {{ formatDate(row.executedAt || row.filledAt || row.createdAt) }}
              </template>
            </el-table-column>
          </el-table>
        </div>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent } from 'echarts/components'
import VChart from 'vue-echarts'
import { Refresh } from '@element-plus/icons-vue'
import { stockApi, tradeApi } from '@/api'
import { useAppStore } from '@/stores/app'
import type { Trade } from '@/types'

use([CanvasRenderer, LineChart, GridComponent, TooltipComponent])

interface BoardItem {
  symbol: string
  name: string
  price: number
  change: number
  changePercent: number
  volume: number
  series: number[]
}

const appStore = useAppStore()

const boardPeriod = ref('60')
const boardLoading = ref(false)
const boardItems = ref<BoardItem[]>([])
const recentTrades = ref<Trade[]>([])

const todayPnl = ref(0)
const todayPnlPercent = ref(0)
const totalAssets = ref(0)
const availableCash = ref(0)
const todayTradesCount = ref(0)
const todayVolume = ref(0)

const activeStrategiesCount = computed(() => appStore.activeStrategies.length)
const totalStrategiesCount = computed(() => appStore.strategies.length)

const watchlistData = computed(() => {
  return appStore.watchlist.map(item => {
    const quote = appStore.quotes.get(item.symbol)
      return {
        ...item,
      current: quote?.current ?? 0,
        change: quote?.change ?? 0,
        changePercent: quote?.changePercent ?? 0
      }
  })
})

function mapQuote(raw: any) {
  const current = Number(raw?.current ?? raw?.price ?? 0)
  const previousClose = Number(raw?.previousClose ?? raw?.prevClose ?? current)
  const change = Number(raw?.change ?? (current - previousClose))
  const changePercent = Number(raw?.changePercent ?? (previousClose ? (change / previousClose) * 100 : 0))
  return {
    current,
    change,
    changePercent,
    volume: Number(raw?.volume ?? 0)
  }
}

function normalizeSeries(rows: any[]): number[] {
  const values = rows
    .map((x: any) => Number(x?.close ?? x?.price ?? 0))
    .filter((x: number) => Number.isFinite(x) && x > 0)

  if (boardPeriod.value !== 'Y') {
    return values
  }

  const grouped = new Map<number, number[]>()
  rows.forEach((row: any) => {
    const dt = new Date(row?.timestamp || row?.time || row?.date)
    const year = dt.getUTCFullYear()
    const close = Number(row?.close ?? row?.price ?? 0)
    if (!Number.isFinite(close) || close <= 0) {
      return
    }
    const bucket = grouped.get(year) || []
    bucket.push(close)
    grouped.set(year, bucket)
  })

  return Array.from(grouped.entries())
    .sort((a, b) => a[0] - b[0])
    .map(([, bucket]) => bucket[bucket.length - 1])
}

function buildMiniChartOption(series: number[]) {
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
        color: up ? '#10b981' : '#ef4444'
      },
      areaStyle: {
        color: up ? 'rgba(16,185,129,0.08)' : 'rgba(239,68,68,0.08)'
      }
    }]
  }
}

function formatCurrency(value: number): string {
  const numeric = Number.isFinite(value) ? value : 0
  return `$${numeric.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function formatVolume(value: number): string {
  if (!value) return '-'
  if (value >= 1000000000) return `${(value / 1000000000).toFixed(2)}B`
  if (value >= 1000000) return `${(value / 1000000).toFixed(2)}M`
  if (value >= 1000) return `${(value / 1000).toFixed(2)}K`
  return value.toString()
}

function formatDate(value: string): string {
  return dayjs(value).format('MM/DD HH:mm')
}

function todayRange() {
  const start = dayjs().startOf('day').toISOString()
  const end = dayjs().endOf('day').toISOString()
  return { start, end }
}

async function loadAccountStats() {
  try {
    const account = await tradeApi.getAccount()
    totalAssets.value = Number((account as any)?.totalAssets ?? 0)
    availableCash.value = Number((account as any)?.cash ?? 0)
    const pnl = Number((account as any)?.unrealizedPnL ?? 0) + Number((account as any)?.realizedPnL ?? 0)
    todayPnl.value = pnl
    todayPnlPercent.value = totalAssets.value > 0 ? (pnl / totalAssets.value) * 100 : 0
  } catch {
    totalAssets.value = 0
    availableCash.value = 0
    todayPnl.value = 0
    todayPnlPercent.value = 0
  }
}

async function loadTradeStatsAndRecent() {
  const range = todayRange()
  const [todayTrades, recent] = await Promise.all([
    tradeApi.list({ page: 1, pageSize: 1000, startDate: range.start, endDate: range.end }),
    tradeApi.list({ page: 1, pageSize: 8 })
  ])

  todayTradesCount.value = todayTrades.total
  todayVolume.value = todayTrades.items.reduce((sum, row: any) => {
    const amount = Number(row?.amount ?? ((row?.filledQuantity ?? row?.quantity ?? 0) * (row?.filledPrice ?? row?.price ?? 0)))
    return sum + (Number.isFinite(amount) ? amount : 0)
  }, 0)
  recentTrades.value = recent.items
}

async function loadBoardData() {
  boardLoading.value = true
  try {
    const symbols = appStore.watchlist.map(item => item.symbol).slice(0, 12)
    if (symbols.length === 0) {
      boardItems.value = []
      return
    }

    const apiPeriod = boardPeriod.value === 'Y' ? 'M' : boardPeriod.value
    const list = await Promise.allSettled(symbols.map(async (symbol) => {
      const [rawQuote, rawKline] = await Promise.all([
        stockApi.getQuote(symbol),
        stockApi.getKline(symbol, apiPeriod, 180)
      ])

      const watch = appStore.watchlist.find(w => w.symbol === symbol)
      const quote = mapQuote(rawQuote)
      const series = normalizeSeries(rawKline as any[])

      return {
        symbol,
        name: watch?.name || symbol,
        price: quote.current,
        change: quote.change,
        changePercent: quote.changePercent,
        volume: quote.volume,
        series
      } as BoardItem
    }))

    boardItems.value = list
      .filter(x => x.status === 'fulfilled')
      .map(x => (x as PromiseFulfilledResult<BoardItem>).value)
      .filter(x => x.series.length > 1)
  } finally {
    boardLoading.value = false
  }
}

async function refreshData() {
  await Promise.all([
    appStore.fetchStrategies(),
    appStore.fetchWatchlist(),
    loadAccountStats(),
    loadTradeStatsAndRecent()
  ])
  await loadBoardData()
}

onMounted(async () => {
  await refreshData()
})
</script>

<style lang="scss" scoped>
.dashboard {
  .stat-cards {
    margin-bottom: 20px;
  }

  .stat-subtitle {
    color: #9ca3af;
    font-size: 12px;
  }

  .card {
    background: var(--qt-card-bg);
    border-radius: 8px;
    padding: 20px;
    border: 1px solid var(--qt-border);
    margin-bottom: 20px;

    h3 {
      font-size: 16px;
      font-weight: 600;
      margin: 0;
    }
  }

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 12px;
  }

  .watch-board {
    .board-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 12px;
      margin-bottom: 16px;
      flex-wrap: wrap;
    }

    .board-empty {
      color: var(--qt-text-muted);
      padding: 12px 0;
    }

    .board-grid {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 14px;
    }

    .board-item {
      border: 1px solid var(--qt-border);
      background: var(--qt-card-bg);
      border-radius: 10px;
      padding: 12px;
      cursor: pointer;
      transition: all 0.2s;

      &:hover {
        border-color: #1a56db;
        box-shadow: 0 8px 20px rgba(0, 0, 0, 0.08);
        transform: translateY(-1px);
      }
    }

    .item-header {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
      gap: 8px;
    }

    .symbol {
      font-weight: 700;
      font-size: 15px;
    }

    .name {
      color: var(--qt-text-secondary);
      font-size: 12px;
    }

    .item-price {
      font-weight: 700;
      font-size: 18px;
    }

    .item-change {
      margin-top: 4px;
      font-size: 13px;
      font-weight: 500;
    }

    .mini-chart {
      height: 90px;
      margin-top: 8px;
    }

    .item-meta {
      margin-top: 6px;
      color: var(--qt-text-secondary);
      font-size: 12px;
    }
  }
}

@media (max-width: 960px) {
  .dashboard {
    .watch-board {
      .board-grid {
        grid-template-columns: 1fr;
      }
    }
  }
}
</style>
