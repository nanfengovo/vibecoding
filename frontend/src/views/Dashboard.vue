<template>
  <div class="dashboard">
    <div class="page-header">
      <h1>仪表盘</h1>
      <div class="header-actions">
        <el-button type="primary" :icon="Refresh" @click="refreshData">刷新数据</el-button>
      </div>
    </div>

    <el-alert
      v-if="isDemoMode"
      type="warning"
      show-icon
      :closable="false"
      class="demo-source-alert"
      title="当前为前端独立模式：总资产、今日交易、最近交易来自浏览器本地演示数据。"
      description="行情会优先尝试调用你在“系统设置-长桥API”里配置的接口；若接口不可用将显示为“-”。"
    />
    <el-alert
      v-if="appStore.quoteError"
      type="error"
      show-icon
      :closable="false"
      class="quote-error-alert"
      :title="`行情接口异常：${appStore.quoteError}`"
    />

    <el-row :gutter="20" class="stat-cards">
      <el-col :span="6" :xs="24" :sm="12" :md="6">
        <MetricCard
          title="今日盈亏"
          :value="formatCurrency(todayPnl)"
          :change="todayPnlPercent"
          icon="Money"
          :valueColor="todayPnl >= 0 ? 'up' : 'down'"
        />
      </el-col>
      <el-col :span="6" :xs="24" :sm="12" :md="6">
        <MetricCard
          title="总资产"
          :value="formatCurrency(totalAssets)"
          :subtitle="`可用现金: ${formatCurrency(availableCash)}`"
          icon="Wallet"
        />
      </el-col>
      <el-col :span="6" :xs="24" :sm="12" :md="6">
        <MetricCard
          title="运行中策略"
          :value="activeStrategiesCount"
          :subtitle="`共 ${totalStrategiesCount} 个策略`"
          icon="Cpu"
        />
      </el-col>
      <el-col :span="6" :xs="24" :sm="12" :md="6">
        <MetricCard
          title="今日交易"
          :value="todayTradesCount"
          :subtitle="`成交金额: ${formatCurrency(todayVolume)}`"
          icon="DataLine"
        />
      </el-col>
    </el-row>

    <div class="fin-terminal-card watch-board">
      <div class="board-header">
        <h3>多股同列（关注列表）</h3>
        <TimeframeTabs
          v-model="boardPeriod"
          :options="[
            { label: '1分', value: '1' },
            { label: '5分', value: '5' },
            { label: '15分', value: '15' },
            { label: '60分', value: '60' },
            { label: '日K', value: 'D' },
            { label: '周K', value: 'W' },
            { label: '月K', value: 'M' },
            { label: '年K', value: 'Y' }
          ]"
          @change="loadBoardData"
        />
      </div>

      <el-skeleton v-if="boardLoading" :rows="6" animated />
      <div v-else-if="boardItems.length === 0" class="board-empty">
        还没有关注股票，先去“股票监控”添加自选吧。
      </div>
      <div v-else class="board-grid">
        <StockCard
          v-for="item in boardItems"
          :key="item.symbol"
          :item="item"
          @click="$router.push(`/stock/${item.symbol}`)"
        />
      </div>
    </div>

    <el-row :gutter="20" class="bottom-content">
      <el-col :span="12" :xs="24" :md="12">
        <div class="fin-terminal-card">
          <div class="card-header">
            <h3>关注列表</h3>
            <el-button type="primary" link @click="$router.push('/watchlist')">
              查看全部
            </el-button>
          </div>
          <div class="glass-list-view">
            <div class="list-header">
              <div class="col-symbol">代码</div>
              <div class="col-name">名称</div>
              <div class="col-price" style="text-align: right">现价</div>
              <div class="col-change" style="text-align: right">涨跌幅</div>
            </div>
            <div class="list-body" v-if="watchlistData.length > 0">
              <div v-for="row in watchlistData.slice(0, 5)" :key="row.symbol" class="list-row" @click="$router.push(`/stock/${row.symbol}`)">
                <div class="col-symbol text-blue">{{ row.symbol }}</div>
                <div class="col-name">{{ row.name }}</div>
                <div class="col-price number-font" style="text-align: right">{{ row.current?.toFixed(2) || '-' }}</div>
                <div class="col-change number-font" style="text-align: right" :class="(row.changePercent ?? 0) >= 0 ? 'color-up' : 'color-down'">
                  {{ (row.changePercent ?? 0) >= 0 ? '+' : '' }}{{ (row.changePercent ?? 0).toFixed(2) }}%
                </div>
              </div>
            </div>
            <div v-else class="empty-state">暂无关注</div>
          </div>
        </div>
      </el-col>

      <el-col :span="12" :xs="24" :md="12">
        <div class="fin-terminal-card">
          <div class="card-header">
            <h3>最近交易</h3>
            <el-button type="primary" link @click="$router.push('/trades')">
              查看全部
            </el-button>
          </div>
          <div class="glass-list-view">
            <div class="list-header">
              <div class="col-symbol">代码</div>
              <div class="col-side">方向</div>
              <div class="col-quantity">数量</div>
              <div class="col-price" style="text-align: right">价格</div>
              <div class="col-time" style="text-align: right">时间</div>
            </div>
            <div class="list-body" v-if="recentTrades.length > 0">
              <div v-for="row in recentTrades.slice(0, 5)" :key="row.id || row.executedAt" class="list-row">
                <div class="col-symbol text-blue">{{ row.symbol }}</div>
                <div class="col-side">
                  <span class="format-tag" :class="row.side === 'buy' ? 'bg-success' : 'bg-danger'">
                    {{ row.side === 'buy' ? '买入' : '卖出' }}
                  </span>
                </div>
                <div class="col-quantity number-font">{{ row.quantity }}</div>
                <div class="col-price number-font" style="text-align: right">{{ Number(row.price || (row as any).filledPrice || 0).toFixed(2) }}</div>
                <div class="col-time number-font text-muted" style="text-align: right">{{ formatDate(row.executedAt || (row as any).filledAt || row.createdAt) }}</div>
              </div>
            </div>
            <div v-else class="empty-state">暂无交易</div>
          </div>
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
import { Refresh } from '@element-plus/icons-vue'
import MetricCard from '@/components/dashboard/MetricCard.vue'
import StockCard from '@/components/dashboard/StockCard.vue'
import TimeframeTabs from '@/components/dashboard/TimeframeTabs.vue'
import { stockApi, tradeApi } from '@/api'
import { shouldUseDemoApi } from '@/api/demo'
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
const isDemoMode = shouldUseDemoApi()

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



function formatCurrency(value: number): string {
  const numeric = Number.isFinite(value) ? value : 0
  return `$${numeric.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
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
  .demo-source-alert {
    margin-bottom: 16px;
  }

  .quote-error-alert {
    margin-bottom: 16px;
  }

  .stat-cards {
    margin-bottom: 24px;
  }

  .bottom-content {
    margin-top: 24px;
  }

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 16px;
  }

  .watch-board {
    .board-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 12px;
      margin-bottom: 20px;
      flex-wrap: wrap;
    }

    .board-empty {
      color: var(--qt-text-muted);
      padding: 24px 0;
      text-align: center;
    }

    .board-grid {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 16px;
    }
  }
}

@media (max-width: 1200px) {
  .dashboard {
    .watch-board {
      .board-grid {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }
  }
}

@media (max-width: 768px) {
  .dashboard {
    .watch-board {
      .board-grid {
        grid-template-columns: 1fr;
      }
    }
    
    .bottom-content {
      margin-top: 16px;
      
      .el-col {
        margin-bottom: 16px;
      }
    }
  }
}

/* Glass List View for Dashboard */
.glass-list-view {
  background: transparent;
  border: 1px solid var(--qt-border);
  border-radius: 8px;
  overflow: hidden;

  .list-header {
    display: flex;
    align-items: center;
    padding: 10px 16px;
    border-bottom: 1px solid var(--qt-border);
    font-size: 12px;
    font-weight: 600;
    color: var(--qt-text-secondary);
    background: rgba(0, 0, 0, 0.1);
  }

  .list-row {
    display: flex;
    align-items: center;
    padding: 12px 16px;
    border-bottom: 1px solid var(--qt-border);
    font-size: 13px;
    color: var(--qt-text);
    transition: all 0.2s ease;
    cursor: pointer;

    &:last-child {
      border-bottom: none;
    }

    &:hover {
      background: color-mix(in srgb, #3b82f6 8%, transparent 92%);
    }
  }

  .col-symbol { flex: 1; font-weight: 500; }
  .col-name { flex: 1.5; color: var(--qt-text-secondary); }
  .col-price { width: 80px; }
  .col-change { width: 90px; }
  
  .col-side { width: 60px; }
  .col-quantity { width: 60px; }
  .col-time { width: 140px; }

  .text-blue { color: #3b82f6; }
  .text-muted { color: var(--qt-text-muted); }
  .number-font { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }

  .format-tag {
    padding: 2px 6px;
    border-radius: 4px;
    font-size: 11px;
    color: #fff;
    &.bg-success { background: color-mix(in srgb, var(--qt-success) 60%, transparent 40%); }
    &.bg-danger { background: color-mix(in srgb, var(--qt-danger) 60%, transparent 40%); }
  }

  .empty-state {
    padding: 30px;
    text-align: center;
    color: var(--qt-text-muted);
    font-size: 13px;
  }
}
</style>
