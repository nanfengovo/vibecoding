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

    <!-- 监控规则列表 -->
    <div class="card rules-section">
      <div class="card-header">
        <h3>监控规则</h3>
        <el-button type="primary" size="small" @click="openCreateRule">
          创建规则
        </el-button>
      </div>
      <el-table :data="monitorRules" style="width: 100%">
        <el-table-column prop="name" label="规则名称" width="180" />
        <el-table-column prop="symbols" label="监控股票">
          <template #default="{ row }">
            <el-tag v-for="s in row.symbols.slice(0, 3)" :key="s" size="small" class="symbol-tag">
              {{ s }}
            </el-tag>
            <el-tag v-if="row.symbols.length > 3" size="small" type="info">
              +{{ row.symbols.length - 3 }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="conditions" label="触发条件">
          <template #default="{ row }">
            {{ formatConditions(row.conditions) }}
          </template>
        </el-table-column>
        <el-table-column prop="isActive" label="状态" width="100">
          <template #default="{ row }">
            <el-switch 
              v-model="row.isActive" 
              @change="toggleRule(row.id, row.isActive)"
            />
          </template>
        </el-table-column>
        <el-table-column label="操作" width="120">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="editRule(row)">编辑</el-button>
            <el-button link type="danger" size="small" @click="deleteRule(row.id)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
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
      <el-table 
        v-if="viewMode === 'table'" 
        :data="filteredWatchlist" 
        style="width: 100%"
        @row-click="handleRowClick"
      >
        <el-table-column prop="symbol" label="代码" width="100">
          <template #default="{ row }">
            <el-link type="primary">{{ row.symbol }}</el-link>
          </template>
        </el-table-column>
        <el-table-column prop="name" label="名称" width="150" />
        <el-table-column prop="current" label="现价" width="100">
          <template #default="{ row }">
            <span :class="(getQuote(row.symbol)?.change ?? 0) >= 0 ? 'price-up' : 'price-down'">
              {{ formatPrice(getCurrentPrice(row)) }}
            </span>
          </template>
        </el-table-column>
        <el-table-column prop="change" label="涨跌" width="100">
          <template #default="{ row }">
            <span :class="(getQuote(row.symbol)?.change ?? 0) >= 0 ? 'price-up' : 'price-down'">
              {{ formatChange(getQuote(row.symbol)?.change) }}
            </span>
          </template>
        </el-table-column>
        <el-table-column prop="changePercent" label="涨跌幅" width="100">
          <template #default="{ row }">
            <span :class="(getQuote(row.symbol)?.changePercent ?? 0) >= 0 ? 'price-up' : 'price-down'">
              {{ formatPercent(getQuote(row.symbol)?.changePercent) }}
            </span>
          </template>
        </el-table-column>
        <el-table-column prop="volume" label="成交量" width="120">
          <template #default="{ row }">
            {{ formatVolume(getQuote(row.symbol)?.volume) }}
          </template>
        </el-table-column>
        <el-table-column prop="high" label="最高" width="100">
          <template #default="{ row }">
            {{ formatPrice(getDayHigh(row)) }}
          </template>
        </el-table-column>
        <el-table-column prop="low" label="最低" width="100">
          <template #default="{ row }">
            {{ formatPrice(getDayLow(row)) }}
          </template>
        </el-table-column>
        <el-table-column prop="notes" label="备注">
          <template #default="{ row }">
            <span class="notes">{{ row.notes || '-' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="80" fixed="right">
          <template #default="{ row }">
            <el-button 
              link 
              type="danger" 
              :icon="Delete" 
              @click.stop="removeStock(row.id)"
            />
          </template>
        </el-table-column>
      </el-table>

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
          <div class="stock-name">{{ item.name }}</div>
          <div class="stock-price">
            <span :class="(getQuote(item.symbol)?.change ?? 0) >= 0 ? 'price-up' : 'price-down'">
              {{ formatPrice(getCurrentPrice(item)) }}
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
        </div>
      </div>
    </div>

    <!-- 添加股票对话框 -->
    <el-dialog v-model="showAddDialog" title="添加关注" width="500px">
      <el-form :model="addForm" label-width="80px">
        <el-form-item label="股票代码">
          <el-autocomplete
            v-model="addForm.symbol"
            :fetch-suggestions="searchSuggestions"
            placeholder="输入股票代码或名称搜索"
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
import { useAppStore } from '@/stores/app'
import { stockApi, monitorApi } from '@/api'
import type { MonitorCondition, MonitorRule, NotificationChannel, StockQuote } from '@/types'

const router = useRouter()
const appStore = useAppStore()

const searchQuery = ref('')
const viewMode = ref<'table' | 'card'>('table')
const showAddDialog = ref(false)
const showRuleDialog = ref(false)
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
  return appStore.quotes.get(symbol)
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

function formatPrice(value?: number | null): string {
  if (value === null || value === undefined) {
    return '-'
  }

  return value.toFixed(2)
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
  if (!addForm.value.symbol) {
    ElMessage.warning('请输入股票代码')
    return
  }
  adding.value = true
  try {
    await appStore.addToWatchlist(addForm.value.symbol, addForm.value.notes)
    ElMessage.success('添加成功')
    showAddDialog.value = false
    addForm.value = { symbol: '', notes: '' }
  } catch (error) {
    ElMessage.error('添加失败')
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

  .notes {
    color: var(--qt-text-muted);
    font-size: 13px;
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
  }
}
</style>
