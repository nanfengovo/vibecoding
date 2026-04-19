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

      <el-table 
        :data="trades" 
        style="width: 100%"
        v-loading="loading"
        table-layout="fixed"
        @sort-change="handleSortChange"
      >
        <el-table-column prop="executedAt" label="时间" width="160" sortable="custom">
          <template #default="{ row }">
            {{ formatDateTime(row.executedAt) }}
          </template>
        </el-table-column>
        <el-table-column prop="symbol" label="股票" width="100">
          <template #default="{ row }">
            <el-link type="primary" @click="$router.push(`/stock/${row.symbol}`)">
              {{ row.symbol }}
            </el-link>
          </template>
        </el-table-column>
        <el-table-column prop="stockName" label="名称" width="120" />
        <el-table-column prop="side" label="方向" width="80">
          <template #default="{ row }">
            <el-tag :type="row.side === 'buy' ? 'success' : 'danger'" size="small">
              {{ row.side === 'buy' ? '买入' : '卖出' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="quantity" label="数量" width="80" align="right" />
        <el-table-column prop="price" label="价格" width="100" align="right">
          <template #default="{ row }">
            {{ formatCurrency(row.price) }}
          </template>
        </el-table-column>
        <el-table-column prop="amount" label="金额" width="120" align="right" sortable="custom">
          <template #default="{ row }">
            {{ formatCurrency(row.amount) }}
          </template>
        </el-table-column>
        <el-table-column prop="commission" label="佣金" width="80" align="right">
          <template #default="{ row }">
            {{ formatCurrency(row.commission) }}
          </template>
        </el-table-column>
        <el-table-column prop="status" label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="getStatusType(row.status)" size="small">
              {{ getStatusLabel(row.status) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="strategyName" label="策略" min-width="140" show-overflow-tooltip>
          <template #default="{ row }">
            {{ row.strategyName || '手动交易' }}
          </template>
        </el-table-column>
      </el-table>

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

function handleSortChange(_: unknown) {
  // 实现排序逻辑
  loadTrades()
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

  :deep(.el-table td.el-table__cell),
  :deep(.el-table th.el-table__cell) {
    white-space: nowrap;
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
  }
}
</style>
