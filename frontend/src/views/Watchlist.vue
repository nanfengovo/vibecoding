<template>
  <div class="watchlist">
    <div class="page-header">
      <h1>股票监控</h1>
      <div class="header-actions">
        <el-input
          v-model="searchQuery"
          placeholder="搜索股票代码或名称..."
          :prefix-icon="Search"
          style="width: 240px"
          @keyup.enter="searchStock"
        />
        <el-button type="primary" :icon="Plus" @click="showAddDialog = true">
          添加关注
        </el-button>
      </div>
    </div>

    <el-alert
      v-if="appStore.quoteError"
      class="quote-alert"
      type="warning"
      show-icon
      :closable="false"
      :title="`行情接口异常：${appStore.quoteError}`"
    />

    <!-- 监控规则列表 -->
    <div class="card rules-section">
      <div class="card-header">
        <h3>监控规则</h3>
        <el-button type="primary" size="small" @click="openCreateRule">
          创建规则
        </el-button>
      </div>
      <div class="glass-list-view rules-table">
        <div class="list-header">
          <div class="col-rule-name">规则名称</div>
          <div class="col-symbols">监控股票</div>
          <div class="col-conditions">触发条件</div>
          <div class="col-status">状态</div>
          <div class="col-actions" style="width: 120px">操作</div>
        </div>
        <div class="list-body" v-if="monitorRules.length > 0">
          <div v-for="row in monitorRules" :key="row.id" class="list-row">
            <div class="col-rule-name">{{ row.name }}</div>
            <div class="col-symbols">
              <span v-for="s in row.symbols.slice(0, 3)" :key="s" class="symbol-tag">{{ s }}</span>
              <span v-if="row.symbols.length > 3" class="symbol-tag text-muted">+{{ row.symbols.length - 3 }}</span>
            </div>
            <div class="col-conditions text-muted">{{ formatConditions(row.conditions) }}</div>
            <div class="col-status">
              <el-switch 
                v-model="row.isActive" 
                @change="toggleRule(row.id, row.isActive)"
              />
            </div>
            <div class="col-actions" style="width: 120px">
              <el-button link type="primary" size="small" @click="editRule(row)">编辑</el-button>
              <el-button link type="danger" size="small" @click="deleteRule(row.id)">删除</el-button>
            </div>
          </div>
        </div>
        <div v-else class="empty-state">暂无监控规则</div>
      </div>
    </div>

    <!-- 关注列表 -->
    <div class="card watchlist-section">
      <div class="card-header">
        <h3>关注列表</h3>
        <el-radio-group v-model="viewMode" size="small">
          <el-radio-button value="table">
            <el-icon><List /></el-icon>
          </el-radio-button>
          <el-radio-button value="card">
            <el-icon><Grid /></el-icon>
          </el-radio-button>
        </el-radio-group>
      </div>

      <!-- 表格视图 -->
      <div v-if="viewMode === 'table'" class="glass-list-view watch-table">
        <div class="list-header">
          <div class="col-symbol">代码</div>
          <div class="col-name">名称</div>
          <div class="col-price" style="text-align: right">现价</div>
          <div class="col-change" style="text-align: right">涨跌幅</div>
          <div class="col-volume hide-mobile" style="text-align: right">成交量</div>
          <div class="col-high hide-mobile" style="text-align: right">最高</div>
          <div class="col-low hide-mobile" style="text-align: right">最低</div>
          <div class="col-actions" style="width: 140px; justify-content: flex-end">操作</div>
        </div>
        <div class="list-body" v-if="filteredWatchlist.length > 0">
          <div v-for="row in filteredWatchlist" :key="row.id" class="list-row" @click="handleRowClick(row)">
            <div class="col-symbol text-blue">{{ row.symbol }}</div>
            <div class="col-name">{{ getDisplayName(row) }}</div>
            <div class="col-price number-font" style="text-align: right">
              <span :class="(getQuote(row.symbol)?.change ?? 0) >= 0 ? 'color-up' : 'color-down'">
                {{ formatPrice(getCurrentPrice(row), row.symbol, row.stock?.market) }}
              </span>
            </div>
            <div class="col-change number-font" style="text-align: right">
              <span :class="(getQuote(row.symbol)?.changePercent ?? 0) >= 0 ? 'color-up' : 'color-down'">
                {{ formatPercent(getQuote(row.symbol)?.changePercent) }}
              </span>
            </div>
            <div class="col-volume number-font text-muted hide-mobile" style="text-align: right">
              {{ formatVolume(getQuote(row.symbol)?.volume) }}
            </div>
            <div class="col-high number-font text-muted hide-mobile" style="text-align: right">
              {{ formatPrice(getDayHigh(row), row.symbol, row.stock?.market) }}
            </div>
            <div class="col-low number-font text-muted hide-mobile" style="text-align: right">
              {{ formatPrice(getDayLow(row), row.symbol, row.stock?.market) }}
            </div>
            <div class="col-actions" style="width: 140px; justify-content: flex-end">
              <el-button link type="primary" size="small" @click.stop="showCompanyDetail(row.symbol)">详情</el-button>
              <el-button link type="danger" :icon="Delete" @click.stop="removeStock(row.id)" />
            </div>
          </div>
        </div>
        <div v-else class="empty-state">暂无相关股票</div>
      </div>

      <!-- 卡片视图 -->
      <div v-else class="card-grid">
        <div 
          v-for="item in filteredWatchlist" 
          :key="item.id" 
          class="stock-card"
          @click="$router.push(`/stock/${item.symbol}`)"
        >
          <div class="stock-header">
            <div class="stock-symbol">{{ item.symbol }}</div>
            <el-button 
              :icon="Delete" 
              circle 
              size="small" 
              @click.stop="removeStock(item.id)"
            />
          </div>
          <div class="stock-name">{{ getDisplayName(item) }}</div>
          <div class="stock-price">
            <span :class="(getQuote(item.symbol)?.change ?? 0) >= 0 ? 'price-up' : 'price-down'">
              {{ formatPrice(getCurrentPrice(item), item.symbol, item.stock?.market) }}
            </span>
          </div>
          <div 
            :class="['stock-change', (getQuote(item.symbol)?.change ?? 0) >= 0 ? 'price-up' : 'price-down']"
          >
            {{ formatChange(getQuote(item.symbol)?.change) }}
            ({{ formatPercent(getQuote(item.symbol)?.changePercent) }})
          </div>
          <div class="stock-volume">
            成交量: {{ formatVolume(getQuote(item.symbol)?.volume) }}
          </div>
          <div class="stock-actions">
            <el-button link type="primary" size="small" @click.stop="showCompanyDetail(item.symbol)">详情</el-button>
          </div>
        </div>
      </div>
    </div>

    <el-dialog
      v-model="showDetailDialog"
      title="公司详情"
      width="760px"
      destroy-on-close
      @closed="resetCompanyProfile"
    >
      <el-skeleton v-if="detailLoading" :rows="8" animated />
      <template v-else-if="selectedStockDetail">
        <div class="glass-descriptions">
          <div class="desc-item">
            <span class="desc-label">代码</span>
            <span class="desc-value text-blue number-font">{{ selectedStockDetail.symbol }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">名称</span>
            <span class="desc-value">{{ getDisplayName(selectedStockDetail) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">市场</span>
            <span class="desc-value">{{ selectedStockDetail.market || '-' }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">现价</span>
            <span class="desc-value number-font">{{ formatPrice(selectedStockDetail.currentPrice, selectedStockDetail.symbol, selectedStockDetail.market) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">昨收</span>
            <span class="desc-value number-font">{{ formatPrice(selectedStockDetail.previousClose, selectedStockDetail.symbol, selectedStockDetail.market) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">涨跌幅</span>
            <span class="desc-value number-font" :class="(selectedStockDetail.changePercent ?? 0) >= 0 ? 'color-up' : 'color-down'">
              {{ formatPercent(selectedStockDetail.changePercent) }}
            </span>
          </div>
          <div class="desc-item">
            <span class="desc-label">开盘</span>
            <span class="desc-value number-font">{{ formatPrice(selectedStockDetail.open, selectedStockDetail.symbol, selectedStockDetail.market) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">最高</span>
            <span class="desc-value number-font">{{ formatPrice(selectedStockDetail.high, selectedStockDetail.symbol, selectedStockDetail.market) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">最低</span>
            <span class="desc-value number-font">{{ formatPrice(selectedStockDetail.low, selectedStockDetail.symbol, selectedStockDetail.market) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">成交量</span>
            <span class="desc-value number-font">{{ formatVolume(selectedStockDetail.volume) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">市值</span>
            <span class="desc-value number-font">{{ formatMarketCap(selectedStockDetail.marketCap, selectedStockDetail.symbol, selectedStockDetail.market) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">市盈率</span>
            <span class="desc-value number-font">{{ formatPlainNumber(selectedStockDetail.pe) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">每股收益</span>
            <span class="desc-value number-font">{{ formatPlainNumber(selectedStockDetail.eps) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">股息率</span>
            <span class="desc-value number-font">{{ formatPercent(selectedStockDetail.dividend) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">52周最高</span>
            <span class="desc-value number-font">{{ formatPrice(selectedStockDetail.high52Week, selectedStockDetail.symbol, selectedStockDetail.market) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">52周最低</span>
            <span class="desc-value number-font">{{ formatPrice(selectedStockDetail.low52Week, selectedStockDetail.symbol, selectedStockDetail.market) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">平均成交量</span>
            <span class="desc-value number-font">{{ formatVolume(selectedStockDetail.avgVolume) }}</span>
          </div>
          <div class="desc-item">
            <span class="desc-label">行情时间</span>
            <span class="desc-value number-font">{{ formatDateTime(selectedStockDetail.updatedAt) }}</span>
          </div>
        </div>

        <div class="company-profile-block">
          <div class="company-profile-header">
            <h4>公司信息</h4>
            <el-button size="small" :loading="companyProfileLoading" @click="loadCompanyProfile(selectedStockDetail.symbol)">
              获取公司信息
            </el-button>
          </div>
          <el-skeleton v-if="companyProfileLoading" :rows="5" animated />
          <template v-else-if="companyProfile">
            <div class="company-overview">
              {{ companyProfile.overview || '暂无公司简介。' }}
            </div>
            <div
              v-if="companyProfile.fields.length > 0"
              class="glass-descriptions company-extra-fields"
            >
              <div
                v-for="item in companyProfile.fields"
                :key="`${item.key}-${item.value}`"
                class="desc-item"
              >
                <span class="desc-label">{{ item.key }}</span>
                <span class="desc-value">{{ item.value }}</span>
              </div>
            </div>
            <div v-if="companyProfile.sourceUrl" class="company-source">
              来源：
              <el-link :href="companyProfile.sourceUrl" target="_blank" type="primary">Longbridge</el-link>
            </div>
          </template>
          <el-empty v-else description="点击“获取公司信息”拉取公司资料" :image-size="60" />
        </div>
      </template>
      <el-empty v-else description="暂无可展示的公司信息" :image-size="72" />
    </el-dialog>

    <!-- 添加股票对话框 -->
    <el-dialog v-model="showAddDialog" title="添加关注" width="500px">
      <el-form :model="addForm" label-width="80px">
        <el-form-item label="股票代码">
          <el-autocomplete
            v-model="addForm.symbol"
            :fetch-suggestions="searchSuggestions"
            placeholder="输入代码或名称（如 AAPL / 00700 / 600519.SH）"
            style="width: 100%"
            @select="handleSelectSuggestion"
          >
            <template #default="{ item }">
              <div class="suggestion-item">
                <span class="symbol">{{ item.symbol }}</span>
                <span class="name">{{ item.name }}</span>
              </div>
            </template>
          </el-autocomplete>
          <div class="symbol-hint">
            支持跨市场代码：`US`（AAPL 或 AAPL.US）、`HK`（00700 或 00700.HK）、`A股`（600519.SH / 000001.SZ）。
          </div>
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="addForm.notes" type="textarea" :rows="3" placeholder="可选备注" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showAddDialog = false">取消</el-button>
        <el-button type="primary" :loading="adding" @click="addStock">确定</el-button>
      </template>
    </el-dialog>

    <!-- 创建监控规则对话框 -->
    <el-dialog v-model="showRuleDialog" :title="ruleDialogTitle" width="600px" @closed="resetRuleForm">
      <el-form :model="ruleForm" label-width="100px">
        <el-form-item label="规则名称">
          <el-input v-model="ruleForm.name" placeholder="输入规则名称" />
        </el-form-item>
        <el-form-item label="监控股票">
          <el-select
            v-model="ruleForm.symbols"
            multiple
            filterable
            placeholder="选择要监控的股票"
            style="width: 100%"
          >
            <el-option
              v-for="item in appStore.watchlist"
              :key="item.symbol"
              :label="`${item.symbol} - ${item.name}`"
              :value="item.symbol"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="触发条件">
          <div class="conditions-editor">
            <div v-for="(cond, index) in ruleForm.conditions" :key="index" class="condition-row">
              <el-select v-model="cond.type" placeholder="条件类型" style="width: 150px">
                <el-option label="价格高于" value="price_above" />
                <el-option label="价格低于" value="price_below" />
                <el-option label="涨幅超过" value="change_above" />
                <el-option label="跌幅超过" value="change_below" />
                <el-option label="成交量超过" value="volume_above" />
              </el-select>
              <el-input-number v-model="cond.value" :min="0" style="width: 120px" />
              <el-button :icon="Delete" circle @click="ruleForm.conditions.splice(index, 1)" />
            </div>
            <el-button type="primary" link @click="addCondition">+ 添加条件</el-button>
          </div>
        </el-form-item>
        <el-form-item label="通知方式">
          <el-checkbox-group v-model="ruleForm.notifications">
            <el-checkbox value="email">邮件</el-checkbox>
            <el-checkbox value="feishu">飞书</el-checkbox>
            <el-checkbox value="wechat">企业微信</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
        <el-form-item label="检查间隔">
          <el-select v-model="ruleForm.checkInterval" style="width: 150px">
            <el-option label="1分钟" :value="60" />
            <el-option label="5分钟" :value="300" />
            <el-option label="15分钟" :value="900" />
            <el-option label="30分钟" :value="1800" />
            <el-option label="1小时" :value="3600" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showRuleDialog = false">取消</el-button>
        <el-button type="primary" :loading="savingRule" @click="saveRule">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { Search, Plus, Delete, List, Grid } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import dayjs from 'dayjs'
import { useAppStore } from '@/stores/app'
import { stockApi, monitorApi } from '@/api'
import type { CompanyProfile, MonitorCondition, MonitorRule, NotificationChannel, Stock, StockQuote } from '@/types'

const router = useRouter()
const appStore = useAppStore()

const searchQuery = ref('')
const viewMode = ref<'table' | 'card'>('table')
const showAddDialog = ref(false)
const showRuleDialog = ref(false)
const showDetailDialog = ref(false)
const detailLoading = ref(false)
const selectedStockDetail = ref<Stock | null>(null)
const companyProfileLoading = ref(false)
const companyProfile = ref<CompanyProfile | null>(null)
const adding = ref(false)
const savingRule = ref(false)
const monitorRules = ref<MonitorRule[]>([])
const editingRuleId = ref<number | null>(null)

const addForm = ref({
  symbol: '',
  notes: ''
})

const ruleForm = ref({
  name: '',
  symbols: [] as string[],
  conditions: [{ type: 'price_above', value: 0, operator: 'gt' }] as MonitorCondition[],
  notifications: ['email'] as NotificationChannel['type'][],
  checkInterval: 300
})

const ruleDialogTitle = computed(() => editingRuleId.value ? '编辑监控规则' : '创建监控规则')

const filteredWatchlist = computed(() => {
  if (!searchQuery.value) return appStore.watchlist
  const query = searchQuery.value.toLowerCase()
  return appStore.watchlist.filter(item => 
    item.symbol.toLowerCase().includes(query) ||
    item.name.toLowerCase().includes(query)
  )
})

function getQuote(symbol: string): StockQuote | undefined {
  const normalized = String(symbol || '').trim().toUpperCase()
  if (!normalized) {
    return undefined
  }

  return appStore.quotes.get(normalized)
    || appStore.quotes.get(normalized.split('.')[0])
    || appStore.quotes.get(`${normalized}.US`)
    || appStore.quotes.get(`${normalized}.HK`)
}

function normalizeSymbol(symbol?: string): string {
  return String(symbol || '').trim().toUpperCase()
}

function inferMarket(symbol?: string, explicitMarket?: string): string {
  const market = String(explicitMarket || '').trim().toUpperCase()
  if (market) {
    return market
  }

  const normalized = normalizeSymbol(symbol)
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

function resolveCurrencyCode(symbol?: string, market?: string): string {
  const marketCode = inferMarket(symbol, market)
  if (marketCode === 'SH' || marketCode === 'SZ') {
    return 'CNY'
  }
  if (marketCode === 'HK') {
    return 'HKD'
  }
  if (marketCode === 'SG') {
    return 'SGD'
  }
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

function isPoorDisplayName(name?: string, symbol?: string): boolean {
  const current = String(name || '').trim()
  if (!current) {
    return true
  }
  const normalizedSymbol = normalizeSymbol(symbol)
  const baseSymbol = normalizedSymbol.split('.')[0] || normalizedSymbol
  if (
    current.toUpperCase() === normalizedSymbol
    || current.toUpperCase() === baseSymbol
    || /^\d{4,6}$/.test(current)
  ) {
    return true
  }
  return false
}

function buildNameFallback(symbol?: string, market?: string): string {
  const normalized = normalizeSymbol(symbol)
  const baseSymbol = normalized.split('.')[0] || normalized
  const marketCode = inferMarket(symbol, market)
  if (marketCode === 'SH' || marketCode === 'SZ') {
    return `A股 ${baseSymbol}`
  }
  if (marketCode === 'HK') {
    return `港股 ${baseSymbol}`
  }
  if (marketCode === 'SG') {
    return `新加坡 ${baseSymbol}`
  }
  return baseSymbol
}

function getDisplayName(row: any): string {
  const symbol = String(row?.symbol || '').trim()
  const market = String(row?.market || row?.stock?.market || '').trim()
  const profileTitle = String(companyProfile.value?.title || '').trim()

  if (selectedStockDetail.value && row === selectedStockDetail.value && !isPoorDisplayName(profileTitle, symbol)) {
    return profileTitle
  }

  const directName = String(row?.name || row?.stock?.name || '').trim()
  if (!isPoorDisplayName(directName, symbol)) {
    return directName
  }

  return buildNameFallback(symbol, market)
}

function toNumber(value: unknown): number | null {
  const numeric = Number(value)
  return Number.isFinite(numeric) ? numeric : null
}

function getCurrentPrice(row: any): number | null {
  const realtime = getQuote(row.symbol)
  return toNumber(
    realtime?.current
    ?? row?.currentPrice
    ?? row?.stock?.currentPrice
  )
}

function getDayHigh(row: any): number | null {
  const realtime = getQuote(row.symbol)
  return toNumber(
    realtime?.high
    ?? row?.high
    ?? row?.stock?.high
  )
}

function getDayLow(row: any): number | null {
  const realtime = getQuote(row.symbol)
  return toNumber(
    realtime?.low
    ?? row?.low
    ?? row?.stock?.low
  )
}

function formatChange(value?: number): string {
  if (value === undefined) return '-'
  return (value >= 0 ? '+' : '') + value.toFixed(2)
}

function formatPercent(value?: number): string {
  if (value === undefined) return '-'
  return (value >= 0 ? '+' : '') + value.toFixed(2) + '%'
}

function formatVolume(value?: number): string {
  if (!value) return '-'
  if (value >= 1000000) return (value / 1000000).toFixed(2) + 'M'
  if (value >= 1000) return (value / 1000).toFixed(2) + 'K'
  return value.toString()
}

function formatMarketCap(value?: number, symbol?: string, market?: string): string {
  if (!value) return '-'
  const prefix = currencyPrefix(resolveCurrencyCode(symbol, market))
  if (value >= 1000000000000) return `${prefix}${(value / 1000000000000).toFixed(2)}T`
  if (value >= 1000000000) return `${prefix}${(value / 1000000000).toFixed(2)}B`
  if (value >= 1000000) return `${prefix}${(value / 1000000).toFixed(2)}M`
  return `${prefix}${value.toFixed(2)}`
}

function formatPlainNumber(value?: number): string {
  if (value === undefined || value === null || Number.isNaN(Number(value))) {
    return '-'
  }
  return Number(value).toFixed(2)
}

function formatDateTime(value?: string): string {
  if (!value) {
    return '-'
  }
  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD HH:mm:ss') : '-'
}

function formatPrice(value?: number | null, symbol?: string, market?: string): string {
  if (value === null || value === undefined) {
    return '-'
  }

  const prefix = currencyPrefix(resolveCurrencyCode(symbol, market))
  return `${prefix}${value.toFixed(2)}`
}

function formatConditions(conditions: any[]): string {
  const types: Record<string, string> = {
    price_above: '价格高于',
    price_below: '价格低于',
    change_above: '涨幅超过',
    change_below: '跌幅超过',
    volume_above: '成交量超过'
  }
  return conditions.map(c => `${types[c.type] || c.type} ${c.value}`).join(', ')
}

async function searchSuggestions(query: string, cb: (results: any[]) => void) {
  if (!query || query.length < 1) {
    cb([])
    return
  }
  try {
    const results = await stockApi.search(query)
    cb(results)
  } catch {
    cb([])
  }
}

function handleSelectSuggestion(item: any) {
  addForm.value.symbol = item.symbol
}

async function addStock() {
  const normalized = String(addForm.value.symbol || '').trim().toUpperCase()
  if (!normalized) {
    ElMessage.warning('请输入股票代码')
    return
  }

  addForm.value.symbol = normalized
  adding.value = true
  try {
    await appStore.addToWatchlist(addForm.value.symbol, addForm.value.notes)
    ElMessage.success('添加成功')
    showAddDialog.value = false
    addForm.value = { symbol: '', notes: '' }
  } catch (error) {
    const message = (error as any)?.response?.data?.message
      || (error as Error)?.message
      || '添加失败'
    ElMessage.error(message)
  } finally {
    adding.value = false
  }
}

async function removeStock(id: number) {
  try {
    await ElMessageBox.confirm('确定要移除此股票吗?', '提示')
    await appStore.removeFromWatchlist(id)
    ElMessage.success('已移除')
  } catch {}
}

async function showCompanyDetail(symbol: string) {
  const normalized = String(symbol || '').trim()
  if (!normalized) {
    return
  }

  showDetailDialog.value = true
  detailLoading.value = true
  companyProfileLoading.value = false
  companyProfile.value = null
  try {
    selectedStockDetail.value = await stockApi.getDetail(normalized)
    await loadCompanyProfile(normalized)
  } catch (error) {
    const message = (error as any)?.response?.data?.message
      || (error as Error)?.message
      || '加载公司详情失败'
    ElMessage.error(message)
    selectedStockDetail.value = null
  } finally {
    detailLoading.value = false
  }
}

async function loadCompanyProfile(symbol: string) {
  const normalized = String(symbol || '').trim()
  if (!normalized) {
    return
  }

  companyProfileLoading.value = true
  try {
    companyProfile.value = await stockApi.getCompanyProfile(normalized)
    if (selectedStockDetail.value && !isPoorDisplayName(companyProfile.value?.title, selectedStockDetail.value.symbol)) {
      selectedStockDetail.value = {
        ...selectedStockDetail.value,
        name: companyProfile.value.title
      }
    }
  } catch (error) {
    const message = (error as any)?.response?.data?.message
      || (error as Error)?.message
      || '获取公司信息失败'
    ElMessage.error(message)
  } finally {
    companyProfileLoading.value = false
  }
}

function resetCompanyProfile() {
  companyProfileLoading.value = false
  companyProfile.value = null
}

function handleRowClick(row: any) {
  router.push(`/stock/${row.symbol}`)
}

function searchStock() {
  // 搜索逻辑在 computed 中处理
}

function addCondition() {
  ruleForm.value.conditions.push({ type: 'price_above', value: 0, operator: 'gt' })
}

async function saveRule() {
  if (!ruleForm.value.name) {
    ElMessage.warning('请输入规则名称')
    return
  }
  savingRule.value = true
  try {
    const payload = {
      name: ruleForm.value.name,
      symbols: ruleForm.value.symbols,
      conditions: ruleForm.value.conditions,
      notifications: ruleForm.value.notifications.map(type => ({ type, enabled: true })),
      checkInterval: ruleForm.value.checkInterval,
      isActive: true,
      isEnabled: true
    }

    if (editingRuleId.value) {
      await monitorApi.updateRule(editingRuleId.value, payload)
      ElMessage.success('规则更新成功')
    } else {
      await monitorApi.createRule(payload)
      ElMessage.success('规则创建成功')
    }

    showRuleDialog.value = false
    resetRuleForm()
    loadRules()
  } catch {
    ElMessage.error(editingRuleId.value ? '更新失败' : '创建失败')
  } finally {
    savingRule.value = false
  }
}

async function toggleRule(id: number, isActive: boolean) {
  try {
    await monitorApi.toggleRule(id, isActive)
  } catch {
    const target = monitorRules.value.find(item => item.id === id)
    if (target) {
      target.isActive = !isActive
      ;(target as any).isEnabled = target.isActive
    }
    ElMessage.error('操作失败')
  }
}

function openCreateRule() {
  editingRuleId.value = null
  resetRuleForm()
  showRuleDialog.value = true
}

function editRule(rule: MonitorRule & Record<string, any>) {
  editingRuleId.value = rule.id
  ruleForm.value.name = rule.name || ''
  ruleForm.value.symbols = Array.isArray(rule.symbols) ? [...rule.symbols] : []

  const conditions = Array.isArray(rule.conditions) ? rule.conditions : []
  ruleForm.value.conditions = conditions.length > 0
    ? conditions.map((item: any) => ({
      type: item?.type || 'price_above',
      value: Number(item?.value ?? 0),
      operator: item?.operator || 'gt',
      logicalOperator: item?.logicalOperator || 'AND',
      id: item?.id
    }))
    : [{ type: 'price_above', value: 0, operator: 'gt' }]

  const notifications = Array.isArray(rule.notifications) ? rule.notifications : []
  ruleForm.value.notifications = notifications
    .map((item: any) => {
      if (typeof item === 'string') return item
      return item?.type
    })
    .filter((item: any) => typeof item === 'string')
    .map((item: string) => item as NotificationChannel['type'])

  if (ruleForm.value.notifications.length === 0) {
    ruleForm.value.notifications = ['email']
  }

  ruleForm.value.checkInterval = Number(rule.checkInterval || rule.checkIntervalSeconds || 300)
  showRuleDialog.value = true
}

function resetRuleForm() {
  ruleForm.value = {
    name: '',
    symbols: [],
    conditions: [{ type: 'price_above', value: 0, operator: 'gt' }],
    notifications: ['email'],
    checkInterval: 300
  }
  editingRuleId.value = null
}

async function deleteRule(id: number) {
  try {
    await ElMessageBox.confirm('确定要删除此规则吗?', '提示')
    await monitorApi.deleteRule(id)
    loadRules()
    ElMessage.success('已删除')
  } catch {}
}

async function loadRules() {
  try {
    const rules = await monitorApi.listRules()
    monitorRules.value = rules.map((rule: any) => ({
      ...rule,
      isActive: Boolean(rule?.isActive ?? rule?.isEnabled),
      conditions: Array.isArray(rule?.conditions) ? rule.conditions : []
    }))
  } catch (error) {
    console.error('Failed to load rules:', error)
  }
}

onMounted(async () => {
  await Promise.allSettled([
    appStore.fetchWatchlist(),
    loadRules()
  ])
})
</script>

<style lang="scss" scoped>
.watchlist {
  .quote-alert {
    margin-bottom: 16px;
  }

  .rules-section {
    margin-bottom: 20px;
  }

  .symbol-tag {
    margin-right: 4px;
  }

  .card-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
    gap: 16px;
  }

  .stock-card {
    background: var(--qt-card-bg);
    border: 1px solid var(--qt-border);
    border-radius: 8px;
    padding: 16px;
    cursor: pointer;
    transition: all 0.2s;

    &:hover {
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
      transform: translateY(-2px);
    }

    .stock-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 8px;
    }

    .stock-symbol {
      font-weight: 600;
      font-size: 16px;
      color: #1a56db;
    }

    .stock-name {
      color: var(--qt-text-secondary);
      font-size: 13px;
      margin-bottom: 12px;
    }

    .stock-price {
      font-size: 24px;
      font-weight: 600;
      margin-bottom: 4px;
    }

    .stock-change {
      font-size: 14px;
      margin-bottom: 8px;
    }

    .stock-volume {
      font-size: 12px;
      color: var(--qt-text-muted);
    }

    .stock-actions {
      margin-top: 8px;
      display: flex;
      justify-content: flex-end;
    }
  }

  .conditions-editor {
    .condition-row {
      display: flex;
      gap: 8px;
      margin-bottom: 8px;
      align-items: center;
    }
  }

  .suggestion-item {
    display: flex;
    gap: 12px;

    .symbol {
      font-weight: 600;
      color: #1a56db;
    }

    .name {
      color: var(--qt-text-secondary);
    }
  }

  .symbol-hint {
    margin-top: 6px;
    font-size: 12px;
    color: var(--qt-text-muted);
    line-height: 1.5;
  }

  .notes {
    color: var(--qt-text-muted);
    font-size: 13px;
  }

  .company-profile-block {
    margin-top: 16px;
  }

  .company-profile-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 12px;

    h4 {
      margin: 0;
      font-size: 15px;
      color: var(--qt-text-primary);
    }
  }

  .company-overview {
    margin-bottom: 12px;
    color: var(--qt-text-secondary);
    line-height: 1.7;
    white-space: pre-wrap;
  }

  .company-extra-fields {
    margin-bottom: 10px;
  }

  .company-source {
    color: var(--qt-text-muted);
    font-size: 12px;
  }

  /* Glass List View for Watchlist */
  .glass-list-view {
    background: transparent;
    border: 1px solid var(--qt-border);
    border-radius: 8px;
    overflow: hidden;

    .list-header {
      display: flex;
      align-items: center;
      padding: 12px 16px;
      border-bottom: 1px solid var(--qt-border);
      font-size: 13px;
      font-weight: 600;
      color: var(--qt-text-secondary);
      background: rgba(0, 0, 0, 0.15);
    }

    .list-row {
      display: flex;
      align-items: center;
      padding: 14px 16px;
      border-bottom: 1px solid var(--qt-border);
      font-size: 14px;
      color: var(--qt-text);
      transition: all 0.2s ease;
      cursor: pointer;

      &:last-child {
        border-bottom: none;
      }

      &:hover {
        background: color-mix(in srgb, #3b82f6 10%, transparent 90%);
      }
    }

    /* Rules Table Columns */
    &.rules-table {
      .col-rule-name { width: 160px; font-weight: 500; }
      .col-symbols { flex: 1; display: flex; gap: 4px; flex-wrap: wrap; }
      .col-conditions { flex: 2; font-size: 13px; }
      .col-status { width: 100px; }
    }

    /* Watch Table Columns */
    &.watch-table {
      .col-symbol { width: 100px; font-weight: 500; }
      .col-name { flex: 1.5; color: var(--qt-text-secondary); }
      .col-price { width: 100px; }
      .col-change { width: 100px; }
      .col-volume { width: 100px; }
      .col-high { width: 100px; }
      .col-low { width: 100px; }
    }

    .col-actions { display: flex; gap: 8px; }

    .text-blue { color: #3b82f6; }
    .text-muted { color: var(--qt-text-muted); }
    .number-font { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }

    .symbol-tag {
      padding: 2px 8px;
      border-radius: 12px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid var(--qt-border);
      font-size: 12px;
    }

    .empty-state {
      padding: 40px;
      text-align: center;
      color: var(--qt-text-muted);
    }
  }

  /* Glass Descriptions for Details */
  .glass-descriptions {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    gap: 12px;
    background: transparent;
    border: 1px solid var(--qt-border);
    border-radius: 8px;
    padding: 16px;

    .desc-item {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 8px 12px;
      background: rgba(255, 255, 255, 0.02);
      border-radius: 4px;
      border: 1px solid rgba(255, 255, 255, 0.05);

      .desc-label {
        font-size: 13px;
        color: var(--qt-text-secondary);
      }

      .desc-value {
        font-size: 14px;
        color: var(--qt-text);
        text-align: right;
      }
    }
  }
}

@media (max-width: 960px) {
  .watchlist {
    .card-grid {
      grid-template-columns: 1fr;
    }

    .conditions-editor {
      .condition-row {
        flex-wrap: wrap;
      }
    }

    .glass-list-view {
      .hide-mobile {
        display: none !important;
      }
      .col-name { flex: 1; }
      .list-row {
        padding: 12px;
      }
    }

    .glass-descriptions {
      grid-template-columns: 1fr;
    }
  }
}
</style>
