<template>
  <div class="trades">
    <div class="page-header">
      <h1>交易记录</h1>
      <div class="header-actions">
        <el-date-picker
          v-model="dateRange"
          type="daterange"
          start-placeholder="开始日期"
          end-placeholder="结束日期"
          clearable
          @change="applyFilters"
        />
        <el-button :icon="Download" @click="exportTrades">导出</el-button>
      </div>
    </div>

    <!-- 交易统计 -->
    <el-row :gutter="20" class="stat-cards">
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-label">总交易次数</div>
          <div class="stat-value">{{ stats.totalTrades }}</div>
        </div>
      </el-col>
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-label">总盈亏</div>
          <div :class="['stat-value', stats.totalPnl >= 0 ? 'price-up' : 'price-down']">
            {{ formatCurrency(stats.totalPnl) }}
          </div>
        </div>
      </el-col>
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-label">胜率</div>
          <div class="stat-value">{{ (stats.winRate * 100).toFixed(1) }}%</div>
        </div>
      </el-col>
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-label">总成交额</div>
          <div class="stat-value">{{ formatCurrency(stats.totalVolume) }}</div>
        </div>
      </el-col>
    </el-row>

    <!-- 交易列表 -->
    <div class="card">
      <div class="card-header">
        <div class="filters">
          <el-input
            v-model="searchQuery"
            placeholder="搜索股票代码..."
            :prefix-icon="Search"
            clearable
            style="width: 200px"
            @keyup.enter="applyFilters"
            @clear="applyFilters"
          />
          <el-select
            v-model="sideFilter"
            placeholder="交易方向"
            clearable
            style="width: 120px"
            @change="applyFilters"
          >
            <el-option label="全部" value="" />
            <el-option label="买入" value="buy" />
            <el-option label="卖出" value="sell" />
          </el-select>
          <el-select
            v-model="statusFilter"
            placeholder="状态"
            clearable
            style="width: 120px"
            @change="applyFilters"
          >
            <el-option label="全部" value="" />
            <el-option label="已成交" value="filled" />
            <el-option label="待成交" value="pending" />
            <el-option label="已取消" value="cancelled" />
            <el-option label="已拒绝" value="rejected" />
          </el-select>
        </div>
      </div>

      <div class="glass-list-view" v-loading="loading">
        <div class="list-header">
          <div class="col-time" style="width: 160px">时间</div>
          <div class="col-symbol" style="width: 100px">股票</div>
          <div class="col-name hide-mobile" style="width: 120px">名称</div>
          <div class="col-side" style="width: 60px; text-align: center">方向</div>
          <div class="col-quantity" style="width: 80px; text-align: right">数量</div>
          <div class="col-price" style="width: 100px; text-align: right">价格</div>
          <div class="col-amount hide-mobile" style="width: 120px; text-align: right">金额</div>
          <div class="col-comm hide-mobile" style="width: 80px; text-align: right">佣金</div>
          <div class="col-status" style="width: 100px; justify-content: center; display: flex">状态</div>
          <div class="col-strategy hide-mobile" style="flex: 1; padding-left: 16px">策略</div>
        </div>
        <div class="list-body" v-if="trades.length > 0">
          <div v-for="row in trades" :key="row.id" class="list-row">
            <div class="col-time number-font text-muted" style="width: 160px">
              {{ formatDateTime(row.executedAt) }}
            </div>
            <div class="col-symbol" style="width: 100px">
              <el-link type="primary" @click="$router.push(`/stock/${row.symbol}`)">
                {{ row.symbol }}
              </el-link>
            </div>
            <div class="col-name hide-mobile" style="width: 120px">{{ row.stockName }}</div>
            <div class="col-side" style="width: 60px; text-align: center">
              <span :class="row.side === 'buy' ? 'price-up' : 'price-down'">
                {{ row.side === 'buy' ? '买入' : '卖出' }}
              </span>
            </div>
            <div class="col-quantity number-font" style="width: 80px; text-align: right">{{ row.quantity }}</div>
            <div class="col-price number-font" style="width: 100px; text-align: right">{{ formatCurrency(row.price) }}</div>
            <div class="col-amount hide-mobile number-font" style="width: 120px; text-align: right">{{ formatCurrency(row.amount) }}</div>
            <div class="col-comm hide-mobile number-font text-muted" style="width: 80px; text-align: right">{{ formatCurrency(row.commission) }}</div>
            <div class="col-status" style="width: 100px; justify-content: center; display: flex">
              <el-tag :type="getStatusType(row.status)" size="small" class="glass-tag">
                {{ getStatusLabel(row.status) }}
              </el-tag>
            </div>
            <div class="col-strategy hide-mobile text-muted" style="flex: 1; padding-left: 16px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">
              {{ row.strategyName || '手动交易' }}
            </div>
          </div>
        </div>
        <div v-else class="empty-state">暂无交易记录</div>
      </div>

      <div class="pagination">
        <el-pagination
          v-model:current-page="pagination.page"
          v-model:page-size="pagination.pageSize"
          :page-sizes="[20, 50, 100]"
          :total="pagination.total"
          layout="total, sizes, prev, pager, next, jumper"
          @size-change="loadTrades"
          @current-change="loadTrades"
        />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { Search, Download } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { tradeApi } from '@/api'
import dayjs from 'dayjs'
import type { Trade } from '@/types'

const trades = ref<Trade[]>([])
const loading = ref(false)
const dateRange = ref<[Date, Date] | []>([])
const searchQuery = ref('')
const sideFilter = ref('')
const statusFilter = ref('')

const pagination = ref({
  page: 1,
  pageSize: 20,
  total: 0
})

const stats = ref({
  totalTrades: 0,
  totalPnl: 0,
  winRate: 0,
  totalVolume: 0
})

function getStatusType(status: string) {
  switch (status) {
    case 'filled': return 'success'
    case 'pending': return 'warning'
    case 'cancelled': return 'info'
    case 'rejected': return 'danger'
    default: return 'info'
  }
}

function getStatusLabel(status: string) {
  switch (status) {
    case 'filled': return '已成交'
    case 'pending': return '待成交'
    case 'cancelled': return '已取消'
    case 'rejected': return '已拒绝'
    default: return status
  }
}

function formatDateTime(date: string) {
  const value = dayjs(date)
  return value.isValid() ? value.format('YYYY/MM/DD HH:mm:ss') : '-'
}

function toNumber(value: unknown) {
  const numeric = Number(value)
  return Number.isFinite(numeric) ? numeric : 0
}

function formatCurrency(value: number) {
  const numeric = toNumber(value)
  const prefix = numeric >= 0 ? '' : '-'
  return `${prefix}$${Math.abs(numeric).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function buildDateParams() {
  if (!Array.isArray(dateRange.value) || dateRange.value.length !== 2) {
    return { startDate: undefined, endDate: undefined }
  }

  return {
    startDate: dayjs(dateRange.value[0]).startOf('day').toISOString(),
    endDate: dayjs(dateRange.value[1]).endOf('day').toISOString()
  }
}

async function loadTrades() {
  loading.value = true
  try {
    const { startDate, endDate } = buildDateParams()
    const result = await tradeApi.list({
      page: pagination.value.page,
      pageSize: pagination.value.pageSize,
      symbol: searchQuery.value.trim() || undefined,
      side: sideFilter.value || undefined,
      status: statusFilter.value || undefined,
      startDate,
      endDate
    })
    trades.value = result.items
    pagination.value.total = result.total
  } catch (error) {
    console.error('Failed to load trades:', error)
  } finally {
    loading.value = false
  }
}

async function loadStats() {
  try {
    const { startDate, endDate } = buildDateParams()
    const result = await tradeApi.getStats(startDate, endDate)
    stats.value = result
  } catch (error) {
    console.error('Failed to load stats:', error)
  }
}

async function applyFilters() {
  pagination.value.page = 1
  await loadTrades()
  await loadStats()
}


function exportTrades() {
  ElMessage.info('导出功能开发中...')
}

onMounted(() => {
  applyFilters()
})
</script>

<style lang="scss" scoped>
.trades {
  .stat-cards {
    margin-bottom: 20px;
  }

  .card {
    background: var(--qt-card-bg);
    border-radius: 8px;
    padding: 20px;
    border: 1px solid var(--qt-border);
  }

  .card-header {
    margin-bottom: 16px;

    .filters {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
    }
  }

  .pagination {
    margin-top: 20px;
    display: flex;
    justify-content: flex-end;
  }

  .header-actions {
    display: flex;
    gap: 8px;
    flex-wrap: wrap;
    justify-content: flex-end;
  }

  /* Glass List View for Trades */
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

      &:last-child {
        border-bottom: none;
      }

      &:hover {
        background: color-mix(in srgb, #3b82f6 10%, transparent 90%);
      }
    }

    .text-muted { color: var(--qt-text-muted); }
    .number-font { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }

    .price-up { color: #f6465d; font-weight: 600; }
    .price-down { color: #0ecb81; font-weight: 600; }

    .glass-tag {
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid var(--qt-border);
    }

    .empty-state {
      padding: 40px;
      text-align: center;
      color: var(--qt-text-muted);
    }
  }
}

@media (max-width: 960px) {
  .trades {
    .stat-cards {
      :deep(.el-col) {
        max-width: 100%;
        flex: 0 0 100%;
      }
    }

    .card {
      padding: 12px;
    }

    .pagination {
      justify-content: flex-start;
      overflow-x: auto;
    }

    .glass-list-view {
      .hide-mobile {
        display: none !important;
      }
    }
  }
}
</style>
