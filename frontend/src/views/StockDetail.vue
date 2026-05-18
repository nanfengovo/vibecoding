<template>
  <div class="stock-detail">
    <div class="page-header">
      <div class="stock-info">
        <h1>{{ displayStockName }}</h1>
        <span class="symbol-badge">{{ symbol }}</span>
        <span class="exchange-badge">{{ stock?.market || 'US' }}</span>
      </div>
      <div class="header-actions">
        <el-button
          :type="isWatched ? 'default' : 'primary'"
          :icon="isWatched ? 'StarFilled' : 'Star'"
          @click="toggleWatch"
        >
          {{ isWatched ? '已关注' : '关注' }}
        </el-button>
        <el-button :icon="Refresh" @click="refreshData(true)">刷新</el-button>
      </div>
    </div>

    <div class="price-overview">
      <div class="current-price">
        <span :class="['price', (quote?.change ?? 0) >= 0 ? 'price-up' : 'price-down']">
          {{ formatMoney(quote?.current) }}
        </span>
        <span :class="['change', (quote?.change ?? 0) >= 0 ? 'price-up' : 'price-down']">
          {{ formatChange(quote?.change) }} ({{ formatPercent(quote?.changePercent) }})
        </span>
      </div>
      <div class="price-stats">
        <div class="stat-item">
          <span class="label">开盘</span>
          <span class="value">{{ formatMoney(quote?.open) }}</span>
        </div>
        <div class="stat-item">
          <span class="label">最高</span>
          <span class="value">{{ formatMoney(quote?.high) }}</span>
        </div>
        <div class="stat-item">
          <span class="label">最低</span>
          <span class="value">{{ formatMoney(quote?.low) }}</span>
        </div>
        <div class="stat-item">
          <span class="label">昨收</span>
          <span class="value">{{ formatMoney(quote?.previousClose) }}</span>
        </div>
        <div class="stat-item">
          <span class="label">成交量</span>
          <span class="value">{{ formatVolume(quote?.volume) }}</span>
        </div>
        <div class="stat-item">
          <span class="label">成交额</span>
          <span class="value">{{ formatTurnover(quote?.turnover) }}</span>
        </div>
      </div>
    </div>

    <el-row :gutter="20" class="detail-grid">
      <el-col :xs="24" :md="16">
        <div class="card chart-section">
          <div class="card-header">
            <div class="period-controls">
              <div class="period-scroll">
                <el-radio-group v-model="klinePeriod" size="small" class="period-group">
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
              <el-date-picker
                v-model="customRange"
                type="datetimerange"
                range-separator="至"
                start-placeholder="开始时间"
                end-placeholder="结束时间"
                clearable
                class="range-picker"
              />
            </div>
            <el-checkbox-group v-model="indicators" class="indicator-select">
              <el-checkbox value="MA">MA</el-checkbox>
              <el-checkbox value="MACD">MACD</el-checkbox>
              <el-checkbox value="RSI">RSI</el-checkbox>
              <el-checkbox value="BOLL">BOLL</el-checkbox>
            </el-checkbox-group>
          </div>
          <div class="kline-chart">
            <v-chart :option="klineChartOption" autoresize />
          </div>
        </div>
      </el-col>

      <el-col :xs="24" :md="8">
        <div class="card company-info">
          <h3>公司信息</h3>
          <div class="info-grid">
            <div class="info-item">
              <span class="label">市值</span>
              <span class="value">{{ formatMarketCap(stock?.marketCap) }}</span>
            </div>
            <div class="info-item">
              <span class="label">市盈率</span>
              <span class="value">{{ formatPlain(stock?.pe) }}</span>
            </div>
            <div class="info-item">
              <span class="label">每股收益</span>
              <span class="value">{{ formatMoney(stock?.eps) }}</span>
            </div>
            <div class="info-item">
              <span class="label">股息率</span>
              <span class="value">{{ formatPercentValue(stock?.dividend) }}</span>
            </div>
            <div class="info-item">
              <span class="label">52周最高</span>
              <span class="value">{{ formatMoney(stock?.high52Week) }}</span>
            </div>
            <div class="info-item">
              <span class="label">52周最低</span>
              <span class="value">{{ formatMoney(stock?.low52Week) }}</span>
            </div>
          </div>
        </div>

        <div class="card ai-analysis">
          <div class="ai-header">
            <h3>AI 分析</h3>
            <div class="ai-actions">
              <el-button size="small" :loading="optimizingAiFocus" :disabled="!aiFocus.trim()" @click="optimizeAiFocus">
                优化提示词
              </el-button>
              <el-button size="small" :loading="aiLoading" @click="runAiAnalysis">生成分析</el-button>
              <el-button size="small" :icon="CopyDocument" :disabled="!aiResult" @click="copyAiAnalysis">
                复制
              </el-button>
              <el-button size="small" :disabled="!aiResult" @click="saveAiResultAsMemory">保存记忆</el-button>
            </div>
          </div>
          <el-input
            v-model="aiFocus"
            type="textarea"
            :rows="2"
            placeholder="可选：输入分析关注点，如“短线压力位和风险控制”"
          />
          <el-select
            v-if="aiProviders.length > 0"
            v-model="selectedAiProviderId"
            class="ai-provider-select"
            placeholder="选择模型源"
            size="small"
          >
            <el-option
              v-for="provider in aiProviders"
              :key="provider.id"
              :label="provider.name"
              :value="provider.id"
            />
          </el-select>
          <el-select
            v-if="aiModelOptions.length > 0"
            v-model="selectedAiModel"
            class="ai-provider-select"
            placeholder="选择模型"
            size="small"
          >
            <el-option
              v-for="model in aiModelOptions"
              :key="model"
              :label="model"
              :value="model"
            />
          </el-select>
          <div class="ai-kb-actions">
            <el-select v-model="selectedKbId" placeholder="可选：关联知识库" size="small" style="width: 220px">
              <el-option
                v-for="kb in knowledgeBases"
                :key="kb.id"
                :label="kb.name"
                :value="kb.id"
              />
            </el-select>
            <el-button size="small" :disabled="!aiResult || !selectedKbId" @click="importAiResultToKnowledge">
              入知识库
            </el-button>
          </div>
          <div class="ai-result">
            <el-skeleton v-if="aiLoading" :rows="6" animated />
            <template v-else-if="aiResult">
              <div class="ai-meta">模型：{{ aiResult.model }} · {{ formatDateTime(aiResult.generatedAt) }}</div>
              <div v-if="aiToc.length > 0" class="ai-toc">
                <span class="toc-label">目录：</span>
                <el-button
                  v-for="item in aiToc"
                  :key="item.id"
                  link
                  size="small"
                  class="toc-item"
                  @click="scrollToAiSection(item.id)"
                >
                  {{ item.title }}
                </el-button>
              </div>
              <div class="ai-text ai-text-full" v-html="aiHtml" />
            </template>
            <el-empty v-else description="点击“生成分析”获取 AI 观点" :image-size="72" />
          </div>
        </div>

        <div class="card quick-trade">
          <h3>快速交易</h3>
          <el-form :model="tradeForm" label-width="60px" size="small">
            <el-form-item label="方向">
              <el-radio-group v-model="tradeForm.side">
                <el-radio-button value="buy">买入</el-radio-button>
                <el-radio-button value="sell">卖出</el-radio-button>
              </el-radio-group>
            </el-form-item>
            <el-form-item label="数量">
              <el-input-number v-model="tradeForm.quantity" :min="1" :step="10" style="width: 100%" />
            </el-form-item>
            <el-form-item label="价格">
              <el-input-number
                v-model="tradeForm.price"
                :min="0"
                :precision="2"
                style="width: 100%"
              />
            </el-form-item>
            <el-form-item>
              <div class="trade-summary">
                预估金额: {{ formatMoney(tradeForm.quantity * tradeForm.price) }}
              </div>
            </el-form-item>
            <el-form-item>
              <el-button
                :type="tradeForm.side === 'buy' ? 'success' : 'danger'"
                style="width: 100%"
                @click="submitTrade"
              >
                {{ tradeForm.side === 'buy' ? '买入' : '卖出' }}
              </el-button>
            </el-form-item>
          </el-form>
        </div>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import dayjs from 'dayjs'
import { useRoute } from 'vue-router'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { CandlestickChart, LineChart, BarChart } from 'echarts/charts'
import {
  DataZoomComponent,
  GridComponent,
  LegendComponent,
  TooltipComponent
} from 'echarts/components'
import VChart from 'vue-echarts'
import { CopyDocument, Refresh } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { aiApi, configApi, knowledgeApi, stockApi } from '@/api'
import { useAppStore } from '@/stores/app'
import type { AiProviderConfig, Candlestick, KnowledgeBase, Stock, StockAnalysisResult, StockQuote } from '@/types'
import { normalizeAiProviders, parseModelCandidates, resolvePreferredProviderId } from '@/lib/ai/providerModel'

use([
  CanvasRenderer,
  CandlestickChart,
  LineChart,
  BarChart,
  GridComponent,
  TooltipComponent,
  LegendComponent,
  DataZoomComponent
])

const route = useRoute()
const appStore = useAppStore()

const symbol = computed(() => route.params.symbol as string)
const stock = ref<Stock | null>(null)
const quote = ref<StockQuote | null>(null)
const klineData = ref<Candlestick[]>([])
const klinePeriod = ref('D')
const customRange = ref<Date[]>([])
const indicators = ref(['MA'])
const aiLoading = ref(false)
const optimizingAiFocus = ref(false)
const aiFocus = ref('')
const aiResult = ref<StockAnalysisResult | null>(null)
const aiProviders = ref<AiProviderConfig[]>([])
const selectedAiProviderId = ref('')
const selectedAiModel = ref('')
const knowledgeBases = ref<KnowledgeBase[]>([])
const selectedKbId = ref<number | null>(null)
const requestSeed = ref(0)

const tradeForm = ref({
  side: 'buy',
  quantity: 100,
  price: 0
})

const isWatched = computed(() =>
  appStore.watchlist.some(item => item.symbol === symbol.value)
)

function inferMarketFromSymbol(symbolValue?: string): string {
  const normalized = String(symbolValue || '').trim().toUpperCase()
  if (normalized.includes('.')) {
    const suffix = normalized.split('.').pop() || ''
    if (suffix) {
      return suffix.toUpperCase()
    }
  }

  if (/^\d{6}$/.test(normalized)) {
    return normalized.startsWith('6') || normalized.startsWith('9') || normalized.startsWith('5') ? 'SH' : 'SZ'
  }

  if (/^\d{5}$/.test(normalized)) {
    return 'HK'
  }

  return 'US'
}

function resolveCurrencyCode(): string {
  const explicit = String(stock.value?.currency || '').trim().toUpperCase()
  if (explicit) {
    return explicit
  }

  const market = String(stock.value?.market || inferMarketFromSymbol(symbol.value)).trim().toUpperCase()
  if (market === 'SH' || market === 'SZ') return 'CNY'
  if (market === 'HK') return 'HKD'
  if (market === 'SG') return 'SGD'
  return 'USD'
}

function currencyPrefix(code: string): string {
  switch (code) {
    case 'CNY':
      return '¥'
    case 'HKD':
      return 'HK$'
    case 'SGD':
      return 'S$'
    default:
      return '$'
  }
}

function isPoorDisplayName(name?: string): boolean {
  const current = String(name || '').trim()
  if (!current) {
    return true
  }

  const normalizedSymbol = String(symbol.value || '').trim().toUpperCase()
  const baseSymbol = normalizedSymbol.split('.')[0] || normalizedSymbol
  return current.toUpperCase() === normalizedSymbol
    || current.toUpperCase() === baseSymbol
    || /^\d{4,6}$/.test(current)
}

const displayStockName = computed(() => {
  const directName = String(stock.value?.name || '').trim()
  if (!isPoorDisplayName(directName)) {
    return directName
  }

  const normalizedSymbol = String(symbol.value || '').trim().toUpperCase()
  const baseSymbol = normalizedSymbol.split('.')[0] || normalizedSymbol
  const market = String(stock.value?.market || inferMarketFromSymbol(normalizedSymbol)).toUpperCase()
  if (market === 'SH' || market === 'SZ') {
    return `A股 ${baseSymbol}`
  }
  if (market === 'HK') {
    return `港股 ${baseSymbol}`
  }
  return baseSymbol || symbol.value
})

type AiHeading = {
  id: string
  title: string
  level: number
}

const aiParsed = computed(() => parseAiMarkdown(aiResult.value?.analysis || ''))
const aiHtml = computed(() => aiParsed.value.html)
const aiToc = computed(() => aiParsed.value.toc.filter(item => item.level <= 3))

const currentAiProvider = computed(() => {
  return aiProviders.value.find((item) => item.id === selectedAiProviderId.value) || aiProviders.value[0] || null
})

const aiModelOptions = computed(() => {
  const list = parseModelCandidates(currentAiProvider.value?.model || '')
  return list.length > 0 ? list : ['gpt-5-mini']
})

watch(currentAiProvider, () => {
  if (!aiModelOptions.value.includes(selectedAiModel.value)) {
    selectedAiModel.value = aiModelOptions.value[0] || ''
  }
})

async function loadAiProviders() {
  try {
    const config = await configApi.get()
    aiProviders.value = normalizeAiProviders(config?.openAi)
    selectedAiProviderId.value = resolvePreferredProviderId(aiProviders.value, config?.openAi?.activeProviderId)
    selectedAiModel.value = aiModelOptions.value[0] || ''
  } catch {
    aiProviders.value = []
    selectedAiProviderId.value = ''
    selectedAiModel.value = ''
  }
}

async function loadKnowledgeBases() {
  try {
    knowledgeBases.value = await knowledgeApi.list()
    if (knowledgeBases.value.length > 0 && !selectedKbId.value) {
      selectedKbId.value = knowledgeBases.value[0].id
    }
  } catch {
    knowledgeBases.value = []
    selectedKbId.value = null
  }
}

const klineChartOption = computed(() => {
  const dates = klineData.value.map(k => k.time)
  const ohlc = klineData.value.map(k => [k.open, k.close, k.low, k.high])
  const volumes = klineData.value.map(k => k.volume)

  return {
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'cross' }
    },
    legend: {
      data: ['K线', 'MA5', 'MA10', 'MA20', '成交量'],
      top: 10
    },
    grid: [
      { left: '10%', right: '8%', height: '50%' },
      { left: '10%', right: '8%', top: '68%', height: '16%' }
    ],
    xAxis: [
      {
        type: 'category',
        data: dates,
        axisLine: { lineStyle: { color: '#8392A5' } }
      },
      {
        type: 'category',
        gridIndex: 1,
        data: dates,
        axisLabel: { show: false }
      }
    ],
    yAxis: [
      {
        scale: true,
        axisLine: { lineStyle: { color: '#8392A5' } },
        splitLine: { show: false }
      },
      {
        scale: true,
        gridIndex: 1,
        splitNumber: 2,
        axisLabel: { show: false },
        axisLine: { show: false },
        axisTick: { show: false },
        splitLine: { show: false }
      }
    ],
    dataZoom: [
      { type: 'inside', xAxisIndex: [0, 1], start: 70, end: 100 },
      { show: true, xAxisIndex: [0, 1], type: 'slider', top: '88%', start: 70, end: 100 }
    ],
    series: [
      {
        name: 'K线',
        type: 'candlestick',
        data: ohlc,
        itemStyle: {
          color: '#ef4444',
          color0: '#10b981',
          borderColor: '#ef4444',
          borderColor0: '#10b981'
        }
      },
      ...(indicators.value.includes('MA') ? [
        {
          name: 'MA5',
          type: 'line',
          data: calculateMA(5),
          smooth: true,
          lineStyle: { opacity: 0.8, width: 1 },
          symbol: 'none'
        },
        {
          name: 'MA10',
          type: 'line',
          data: calculateMA(10),
          smooth: true,
          lineStyle: { opacity: 0.8, width: 1 },
          symbol: 'none'
        },
        {
          name: 'MA20',
          type: 'line',
          data: calculateMA(20),
          smooth: true,
          lineStyle: { opacity: 0.8, width: 1 },
          symbol: 'none'
        }
      ] : []),
      {
        name: '成交量',
        type: 'bar',
        xAxisIndex: 1,
        yAxisIndex: 1,
        data: volumes,
        itemStyle: {
          color: (params: any) => {
            const idx = params.dataIndex
            if (idx === 0) return '#9CA3AF'
            return klineData.value[idx].close >= klineData.value[idx - 1].close ? '#ef4444' : '#10b981'
          }
        }
      }
    ]
  }
})

function calculateMA(period: number): (number | null)[] {
  const result: (number | null)[] = []
  for (let i = 0; i < klineData.value.length; i++) {
    if (i < period - 1) {
      result.push(null)
      continue
    }
    let sum = 0
    for (let j = 0; j < period; j++) {
      sum += klineData.value[i - j].close
    }
    result.push(sum / period)
  }
  return result
}

function normalizeQuote(rawQuote: any): StockQuote {
  const current = Number(rawQuote?.current ?? rawQuote?.price ?? 0)
  const previousClose = Number(rawQuote?.previousClose ?? rawQuote?.prevClose ?? current)
  const change = Number(rawQuote?.change ?? (current - previousClose))
  const changePercent = Number(
    rawQuote?.changePercent
    ?? rawQuote?.change_rate
    ?? (previousClose ? (change / previousClose) * 100 : 0)
  )

  return {
    symbol: rawQuote?.symbol || symbol.value,
    name: rawQuote?.name || stock.value?.name || symbol.value,
    current,
    previousClose,
    change,
    changePercent,
    high: Number(rawQuote?.high ?? current),
    low: Number(rawQuote?.low ?? current),
    open: Number(rawQuote?.open ?? current),
    volume: Number(rawQuote?.volume ?? 0),
    turnover: Number(rawQuote?.turnover ?? 0),
    timestamp: rawQuote?.timestamp || new Date().toISOString()
  }
}

function normalizeKlines(raw: any[]): Candlestick[] {
  const normalized = raw
    .map((item: any) => {
      const time = item?.time || item?.timestamp || item?.date
      return {
        time: typeof time === 'string' ? time : new Date(time).toISOString(),
        open: Number(item?.open ?? 0),
        high: Number(item?.high ?? 0),
        low: Number(item?.low ?? 0),
        close: Number(item?.close ?? item?.price ?? 0),
        volume: Number(item?.volume ?? 0)
      } as Candlestick
    })
    .filter(item => !Number.isNaN(new Date(item.time).getTime()))
    .sort((a, b) => new Date(a.time).getTime() - new Date(b.time).getTime())

  if (klinePeriod.value !== 'Y') {
    return normalized
  }

  return aggregateByYear(normalized)
}

function aggregateByYear(data: Candlestick[]): Candlestick[] {
  const groups = new Map<number, Candlestick[]>()
  data.forEach((item) => {
    const year = new Date(item.time).getUTCFullYear()
    const bucket = groups.get(year) || []
    bucket.push(item)
    groups.set(year, bucket)
  })

  return Array.from(groups.entries())
    .sort((a, b) => a[0] - b[0])
    .map(([year, bucket]) => {
      const sorted = [...bucket].sort((a, b) => new Date(a.time).getTime() - new Date(b.time).getTime())
      const open = sorted[0].open
      const close = sorted[sorted.length - 1].close
      const high = Math.max(...sorted.map(x => x.high))
      const low = Math.min(...sorted.map(x => x.low))
      const volume = sorted.reduce((sum, row) => sum + row.volume, 0)
      return {
        time: `${year}-01-01T00:00:00.000Z`,
        open,
        high,
        low,
        close,
        volume
      }
    })
}

async function loadStock(seed: number) {
  const result = await stockApi.getDetail(symbol.value)
  if (seed === requestSeed.value) {
    stock.value = result
  }
}

async function loadQuote(seed: number) {
  const rawQuote = await stockApi.getQuote(symbol.value) as any
  const normalized = normalizeQuote(rawQuote)
  if (seed === requestSeed.value) {
    quote.value = normalized
    tradeForm.value.price = normalized.current
  }
}

async function loadKline(seed: number) {
  const periodForApi = klinePeriod.value === 'Y' ? 'M' : klinePeriod.value
  const range = customRange.value?.length === 2
    ? {
      start: customRange.value[0].toISOString(),
      end: customRange.value[1].toISOString()
    }
    : undefined

  const raw = await stockApi.getKline(symbol.value, periodForApi, 1000, range) as any[]
  const normalized = normalizeKlines(raw)
  if (seed === requestSeed.value) {
    klineData.value = normalized
  }
}

async function refreshData(showMessage = false) {
  if (!symbol.value) {
    return
  }

  requestSeed.value += 1
  const seed = requestSeed.value

  try {
    await Promise.all([loadStock(seed), loadQuote(seed), loadKline(seed)])
    if (showMessage) {
      ElMessage.success('数据已刷新')
    }
  } catch (error) {
    console.error('Failed to refresh stock detail:', error)
    if (showMessage) {
      ElMessage.error('刷新失败，请稍后重试')
    }
  }
}

async function runAiAnalysis() {
  if (!symbol.value) {
    return
  }

  aiLoading.value = true
  try {
    const range = customRange.value?.length === 2
      ? {
        start: customRange.value[0].toISOString(),
        end: customRange.value[1].toISOString()
      }
      : undefined

    aiResult.value = await aiApi.analyzeStock(symbol.value, {
      period: klinePeriod.value,
      start: range?.start,
      end: range?.end,
      count: 240,
      focus: aiFocus.value,
      providerId: selectedAiProviderId.value || undefined,
      model: selectedAiModel.value || undefined
    })
  } catch (error: any) {
    const message = error?.response?.data?.message || error?.message || 'AI 分析失败，请检查配置'
    const modelTip = selectedAiModel.value ? `（当前模型：${selectedAiModel.value}）` : ''
    ElMessage.error(`${message}${modelTip}`)
  } finally {
    aiLoading.value = false
  }
}

async function optimizeAiFocus() {
  const focus = String(aiFocus.value || '').trim()
  if (!focus) {
    ElMessage.warning('请先输入分析关注点')
    return
  }
  if (optimizingAiFocus.value) {
    return
  }

  optimizingAiFocus.value = true
  try {
    const result = await aiApi.optimizePrompt({
      question: focus,
      symbol: symbol.value,
      providerId: selectedAiProviderId.value || undefined,
      model: selectedAiModel.value || undefined,
      scene: 'stock_analysis'
    })
    const optimized = String(result?.optimizedPrompt || '').trim()
    if (!optimized) {
      throw new Error('优化结果为空')
    }
    aiFocus.value = optimized
    ElMessage.success('提示词已优化')
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || error?.message || '提示词优化失败')
  } finally {
    optimizingAiFocus.value = false
  }
}

async function saveAiResultAsMemory() {
  if (!aiResult.value?.analysis?.trim()) {
    ElMessage.warning('暂无可保存的 AI 分析')
    return
  }

  try {
    await aiApi.createMemory({
      type: 'stock_analysis',
      title: `个股分析：${symbol.value}`,
      content: aiResult.value.analysis.trim(),
      symbol: symbol.value,
      tags: 'stock,analysis',
      priority: 2,
      sourceType: 'stock_analysis',
      sourceUrl: `stock://${symbol.value}`,
      sourceRef: `stock:${symbol.value}:${aiResult.value.generatedAt}`,
      providerId: selectedAiProviderId.value || undefined,
      model: selectedAiModel.value || aiResult.value.model || undefined,
      knowledgeBaseId: selectedKbId.value || undefined
    })
    ElMessage.success('已保存到 AI 记忆')
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || '保存记忆失败')
  }
}

async function importAiResultToKnowledge() {
  if (!aiResult.value?.analysis?.trim() || !selectedKbId.value) {
    ElMessage.warning('请先生成分析并选择知识库')
    return
  }

  const markdown = `# 个股 AI 分析：${symbol.value}\n\n${aiResult.value.analysis}\n\n- 模型：${aiResult.value.model}\n- 生成时间：${aiResult.value.generatedAt}`
  await knowledgeApi.importMarkdown(selectedKbId.value, {
    title: `${symbol.value} - AI 分析`,
    markdown,
    sourceType: 'stock_analysis',
    sourceUrl: `stock://${symbol.value}`
  })
  ElMessage.success('已导入知识库')
}

async function toggleWatch() {
  if (isWatched.value) {
    const item = appStore.watchlist.find(w => w.symbol === symbol.value)
    if (item) {
      await appStore.removeFromWatchlist(item.id)
      ElMessage.success('已取消关注')
    }
  } else {
    await appStore.addToWatchlist(symbol.value)
    ElMessage.success('已添加关注')
  }
}

function submitTrade() {
  ElMessage.info('交易功能开发中...')
}

function isValidNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

function formatMoney(value?: number): string {
  if (!isValidNumber(value)) return '-'
  return `${currencyPrefix(resolveCurrencyCode())}${value.toFixed(2)}`
}

function formatPlain(value?: number): string {
  if (!isValidNumber(value)) return '-'
  return value.toFixed(2)
}

function formatPercentValue(value?: number): string {
  if (!isValidNumber(value)) return '-'
  return `${value.toFixed(2)}%`
}

function formatChange(value?: number): string {
  if (!isValidNumber(value)) return '-'
  return (value >= 0 ? '+' : '') + value.toFixed(2)
}

function formatPercent(value?: number): string {
  if (!isValidNumber(value)) return '-'
  return (value >= 0 ? '+' : '') + value.toFixed(2) + '%'
}

function formatVolume(value?: number): string {
  if (!value) return '-'
  if (value >= 1000000000) return (value / 1000000000).toFixed(2) + 'B'
  if (value >= 1000000) return (value / 1000000).toFixed(2) + 'M'
  if (value >= 1000) return (value / 1000).toFixed(2) + 'K'
  return value.toString()
}

function formatTurnover(value?: number): string {
  if (!value) return '-'
  const prefix = currencyPrefix(resolveCurrencyCode())
  if (value >= 1000000000) return `${prefix}${(value / 1000000000).toFixed(2)}B`
  if (value >= 1000000) return `${prefix}${(value / 1000000).toFixed(2)}M`
  return `${prefix}${value.toFixed(2)}`
}

function formatMarketCap(value?: number): string {
  if (!value) return '-'
  const prefix = currencyPrefix(resolveCurrencyCode())
  if (value >= 1000000000000) return `${prefix}${(value / 1000000000000).toFixed(2)}T`
  if (value >= 1000000000) return `${prefix}${(value / 1000000000).toFixed(2)}B`
  if (value >= 1000000) return `${prefix}${(value / 1000000).toFixed(2)}M`
  return `${prefix}${value.toFixed(2)}`
}

function formatDateTime(value?: string): string {
  if (!value) return '-'
  return dayjs(value).format('MM-DD HH:mm')
}

function escapeHtml(raw: string): string {
  return raw
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

function formatInlineMarkdown(raw: string): string {
  const escaped = escapeHtml(raw)
  return escaped
    .replace(/`([^`]+?)`/g, '<code>$1</code>')
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/\*(.+?)\*/g, '<em>$1</em>')
}

function headingId(title: string, index: number): string {
  const normalized = title
    .toLowerCase()
    .replace(/[^\w\u4e00-\u9fa5-]+/g, '-')
    .replace(/-{2,}/g, '-')
    .replace(/^-+|-+$/g, '')

  return `ai-${normalized || 'section'}-${index}`
}

function parseAiMarkdown(raw: string): { html: string; toc: AiHeading[] } {
  if (!raw.trim()) {
    return { html: '', toc: [] }
  }

  const lines = raw.split(/\r?\n/)
  const toc: AiHeading[] = []
  const htmlParts: string[] = []
  let inUnorderedList = false
  let inOrderedList = false
  let headingIndex = 0

  const closeLists = () => {
    if (inUnorderedList) {
      htmlParts.push('</ul>')
      inUnorderedList = false
    }

    if (inOrderedList) {
      htmlParts.push('</ol>')
      inOrderedList = false
    }
  }

  for (const line of lines) {
    const trimmed = line.trim()
    if (!trimmed) {
      closeLists()
      continue
    }

    const headingMatch = trimmed.match(/^(#{1,6})\s+(.+)$/)
    if (headingMatch) {
      closeLists()
      const level = headingMatch[1].length
      const title = headingMatch[2].trim()
      const id = headingId(title, headingIndex++)
      toc.push({ id, title, level })
      htmlParts.push(`<h${level} id="${id}">${formatInlineMarkdown(title)}</h${level}>`)
      continue
    }

    const unorderedMatch = trimmed.match(/^[-*]\s+(.+)$/)
    if (unorderedMatch) {
      if (inOrderedList) {
        htmlParts.push('</ol>')
        inOrderedList = false
      }

      if (!inUnorderedList) {
        htmlParts.push('<ul>')
        inUnorderedList = true
      }

      htmlParts.push(`<li>${formatInlineMarkdown(unorderedMatch[1])}</li>`)
      continue
    }

    const orderedMatch = trimmed.match(/^\d+\.\s+(.+)$/)
    if (orderedMatch) {
      if (inUnorderedList) {
        htmlParts.push('</ul>')
        inUnorderedList = false
      }

      if (!inOrderedList) {
        htmlParts.push('<ol>')
        inOrderedList = true
      }

      htmlParts.push(`<li>${formatInlineMarkdown(orderedMatch[1])}</li>`)
      continue
    }

    closeLists()
    htmlParts.push(`<p>${formatInlineMarkdown(trimmed)}</p>`)
  }

  closeLists()
  return { html: htmlParts.join(''), toc }
}

async function copyAiAnalysis() {
  const content = aiResult.value?.analysis?.trim()
  if (!content) {
    return
  }

  try {
    await navigator.clipboard.writeText(content)
    ElMessage.success('AI 分析已复制')
  } catch (error) {
    console.error('Failed to copy AI analysis:', error)
    ElMessage.error('复制失败，请手动复制')
  }
}

function scrollToAiSection(id: string) {
  const element = document.getElementById(id)
  if (!element) {
    return
  }

  element.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

watch(symbol, () => {
  if (customRange.value.length > 0) {
    customRange.value = []
  }
  aiResult.value = null
  loadAiProviders().catch(() => undefined)
  loadKnowledgeBases().catch(() => undefined)
  refreshData(false)
}, { immediate: true })

watch([klinePeriod, customRange], () => {
  if (symbol.value) {
    requestSeed.value += 1
    const seed = requestSeed.value
    loadKline(seed).catch((error) => {
      console.error('Failed to load kline:', error)
    })
  }
})
</script>

<style lang="scss" scoped>
.stock-detail {
  .page-header {
    align-items: flex-start;
  }

  .detail-grid {
    margin-bottom: 0;
  }

  .stock-info {
    display: flex;
    align-items: center;
    gap: 12px;
    flex-wrap: wrap;

    h1 {
      margin: 0;
      font-size: 36px;
      line-height: 1.15;
    }

    .symbol-badge {
      background: #1a56db;
      color: #fff;
      padding: 4px 12px;
      border-radius: 4px;
      font-weight: 600;
    }

    .exchange-badge {
      background: color-mix(in srgb, var(--qt-border) 70%, #f8fafc 30%);
      color: var(--qt-text-secondary);
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 12px;
    }
  }

  .header-actions {
    display: flex;
    align-items: center;
    gap: 10px;
  }

  .price-overview {
    background: var(--qt-card-bg);
    border-radius: 8px;
    padding: 24px;
    margin-bottom: 20px;
    border: 1px solid var(--qt-border);

    .current-price {
      margin-bottom: 20px;

      .price {
        font-size: 36px;
        font-weight: 700;
        margin-right: 16px;
      }

      .change {
        font-size: 18px;
      }
    }

    .price-stats {
      display: grid;
      grid-template-columns: repeat(6, minmax(0, 1fr));
      gap: 16px;

      .stat-item {
        display: flex;
        flex-direction: column;
        gap: 4px;

        .label {
          font-size: 12px;
          color: #9ca3af;
        }

        .value {
          font-size: 15px;
          font-weight: 500;
        }
      }
    }
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
      margin: 0 0 16px;
    }
  }

  .chart-section {
    .card-header {
      display: flex;
      flex-direction: column;
      gap: 12px;
      margin-bottom: 16px;
    }

    .period-controls {
      display: flex;
      align-items: center;
      gap: 12px;
      flex-wrap: wrap;
    }

    .period-scroll {
      flex: 1;
      min-width: 0;
      overflow-x: auto;
      scrollbar-width: thin;
    }

    .period-group {
      width: max-content;
      white-space: nowrap;
    }

    .range-picker {
      width: 340px;
      max-width: 100%;
      min-width: 280px;
    }

    .indicator-select {
      display: flex;
      flex-wrap: wrap;
      row-gap: 8px;

      :deep(.el-checkbox) {
        margin-right: 12px;
      }
    }

    .kline-chart {
      height: 500px;
    }
  }

  .company-info {
    .info-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 16px;

      .info-item {
        display: flex;
        flex-direction: column;
        gap: 4px;

        .label {
          font-size: 12px;
          color: #9ca3af;
        }

        .value {
          font-size: 15px;
          font-weight: 500;
        }
      }
    }
  }

  .ai-analysis {
    .ai-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 12px;
    }

    .ai-actions {
      display: flex;
      gap: 8px;
      align-items: center;
    }

    .ai-provider-select {
      width: 100%;
      margin-top: 10px;
    }

    .ai-kb-actions {
      margin-top: 10px;
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      align-items: center;
    }

    .ai-result {
      margin-top: 12px;
    }

    .ai-meta {
      font-size: 12px;
      color: var(--qt-text-secondary);
      margin-bottom: 8px;
    }

    .ai-toc {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 4px;
      margin-bottom: 8px;
      padding: 6px 10px;
      border-radius: 8px;
      border: 1px solid var(--qt-border);
      background: color-mix(in srgb, var(--qt-card-bg) 90%, #64748b 10%);
    }

    .toc-label {
      font-size: 12px;
      color: var(--qt-text-secondary);
      margin-right: 2px;
    }

    .toc-item {
      font-size: 12px;
      max-width: 220px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .ai-text {
      font-size: 13px;
      line-height: 1.6;
      color: var(--qt-text-primary);
      padding: 10px;
      border-radius: 8px;
      background: color-mix(in srgb, var(--qt-card-bg) 85%, #64748b 15%);
      border: 1px solid var(--qt-border);
      overflow-wrap: anywhere;
      word-break: break-word;

      :deep(h1),
      :deep(h2),
      :deep(h3),
      :deep(h4) {
        margin: 12px 0 8px;
        line-height: 1.4;
      }

      :deep(h1) {
        font-size: 18px;
      }

      :deep(h2) {
        font-size: 16px;
      }

      :deep(h3),
      :deep(h4) {
        font-size: 14px;
      }

      :deep(p) {
        margin: 0 0 8px;
      }

      :deep(ul),
      :deep(ol) {
        margin: 0 0 10px 18px;
        padding: 0;
      }

      :deep(li) {
        margin-bottom: 4px;
      }

      :deep(code) {
        font-family: ui-monospace, SFMono-Regular, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
        background: color-mix(in srgb, var(--qt-card-bg) 82%, #0f172a 18%);
        border: 1px solid var(--qt-border);
        border-radius: 4px;
        padding: 1px 5px;
        font-size: 12px;
      }
    }

    .ai-text-full {
      margin-top: 6px;
    }
  }

  .quick-trade {
    .trade-summary {
      font-size: 14px;
      color: var(--qt-text-secondary);
      padding: 8px;
      background: color-mix(in srgb, var(--qt-card-bg) 90%, #64748b 10%);
      border-radius: 4px;
      text-align: center;
    }
  }
}

@media (max-width: 960px) {
  .stock-detail {
    .stock-info h1 {
      font-size: 30px;
    }

    .header-actions {
      width: 100%;
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 8px;

      :deep(.el-button) {
        width: 100%;
      }
    }

    .price-overview {
      padding: 16px;
      margin-bottom: 14px;

      .current-price {
        margin-bottom: 14px;

        .price {
          display: inline-block;
          font-size: 42px;
          margin-right: 10px;
          line-height: 1;
        }

        .change {
          font-size: 28px;
          font-weight: 600;
          line-height: 1;
          vertical-align: baseline;
        }
      }

      .price-stats {
        grid-template-columns: repeat(3, minmax(0, 1fr));
        gap: 12px;
      }
    }

    .chart-section {
      .period-controls {
        flex-direction: column;
        align-items: stretch;
      }

      .period-scroll {
        width: 100%;
      }

      .period-group {
        display: inline-flex;
        width: max-content;
      }

      .range-picker {
        width: 100%;
        min-width: 0;
      }

      .indicator-select {
        :deep(.el-checkbox) {
          margin-right: 8px;
        }
      }

      .kline-chart {
        height: 420px;
      }
    }

    .company-info {
      .info-grid {
        gap: 12px;
      }
    }
  }
}

@media (max-width: 640px) {
  .stock-detail {
    .stock-info h1 {
      font-size: 28px;
    }

    .price-overview {
      .current-price {
        .price {
          font-size: 40px;
        }

        .change {
          display: block;
          margin-top: 8px;
          font-size: 18px;
          line-height: 1.2;
        }
      }

      .price-stats {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }

    .chart-section {
      .kline-chart {
        height: 360px;
      }
    }

    .company-info {
      .info-grid {
        grid-template-columns: 1fr;
      }
    }

    .ai-analysis {
      .ai-header {
        flex-wrap: wrap;
        gap: 8px;
      }
    }
  }
}
</style>
