<template>
  <div class="backtest">
    <div class="page-header">
      <h1>回测中心</h1>
      <div class="header-actions">
        <el-button type="primary" :icon="Plus" @click="showCreateDialog = true">
          新建回测
        </el-button>
      </div>
    </div>

    <el-row :gutter="20">
      <!-- 回测列表 -->
      <el-col :span="8">
        <div class="card backtest-list">
          <h3>回测记录</h3>
          <el-scrollbar height="600px">
            <div 
              v-for="item in backtests" 
              :key="item.id" 
              :class="['backtest-item', { active: selectedId === item.id }]"
              @click="selectBacktest(item.id)"
            >
              <div class="item-header">
                <span class="strategy-name">{{ item.strategyName }}</span>
                <el-tag :type="getStatusType(item.status)" size="small">
                  {{ getStatusLabel(item.status) }}
                </el-tag>
              </div>
              <div class="item-info">
                <span>{{ formatDate(item.startDate) }} ~ {{ formatDate(item.endDate) }}</span>
              </div>
              <div class="item-result">
                <span :class="item.totalReturn >= 0 ? 'price-up' : 'price-down'">
                  {{ item.totalReturn >= 0 ? '+' : '' }}{{ (item.totalReturn * 100).toFixed(2) }}%
                </span>
                <span class="drawdown">最大回撤: {{ (item.maxDrawdown * 100).toFixed(2) }}%</span>
              </div>
            </div>
            <div v-if="backtests.length === 0" class="empty-list">
              暂无回测记录
            </div>
          </el-scrollbar>
        </div>
      </el-col>

      <!-- 回测详情 -->
      <el-col :span="16">
        <template v-if="selectedBacktest">
          <!-- 统计指标 -->
          <el-row :gutter="16" class="stat-cards">
            <el-col :span="6">
              <div class="stat-card">
                <div class="stat-label">总收益</div>
                <div 
                  :class="['stat-value', selectedBacktest.totalReturn >= 0 ? 'price-up' : 'price-down']"
                >
                  {{ (selectedBacktest.totalReturn * 100).toFixed(2) }}%
                </div>
              </div>
            </el-col>
            <el-col :span="6">
              <div class="stat-card">
                <div class="stat-label">年化收益</div>
                <div class="stat-value">{{ (selectedBacktest.annualizedReturn * 100).toFixed(2) }}%</div>
              </div>
            </el-col>
            <el-col :span="6">
              <div class="stat-card">
                <div class="stat-label">最大回撤</div>
                <div class="stat-value price-down">{{ (selectedBacktest.maxDrawdown * 100).toFixed(2) }}%</div>
              </div>
            </el-col>
            <el-col :span="6">
              <div class="stat-card">
                <div class="stat-label">夏普比率</div>
                <div class="stat-value">{{ selectedBacktest.sharpeRatio.toFixed(2) }}</div>
              </div>
            </el-col>
          </el-row>

          <el-row :gutter="16" class="stat-cards">
            <el-col :span="6">
              <div class="stat-card">
                <div class="stat-label">胜率</div>
                <div class="stat-value">{{ (selectedBacktest.winRate * 100).toFixed(2) }}%</div>
              </div>
            </el-col>
            <el-col :span="6">
              <div class="stat-card">
                <div class="stat-label">总交易次数</div>
                <div class="stat-value">{{ selectedBacktest.totalTrades }}</div>
              </div>
            </el-col>
            <el-col :span="6">
              <div class="stat-card">
                <div class="stat-label">盈利次数</div>
                <div class="stat-value price-up">{{ selectedBacktest.profitTrades }}</div>
              </div>
            </el-col>
            <el-col :span="6">
              <div class="stat-card">
                <div class="stat-label">亏损次数</div>
                <div class="stat-value price-down">{{ selectedBacktest.lossTrades }}</div>
              </div>
            </el-col>
          </el-row>

          <!-- 权益曲线 -->
          <div class="card">
            <h3>权益曲线</h3>
            <div class="chart-container">
              <v-chart :option="equityChartOption" autoresize />
            </div>
          </div>

          <!-- 交易记录 -->
          <div class="card">
            <h3>交易记录</h3>
            <el-table :data="selectedBacktest.trades" style="width: 100%" max-height="300">
              <el-table-column prop="date" label="日期" width="120">
                <template #default="{ row }">
                  {{ formatDate(row.date) }}
                </template>
              </el-table-column>
              <el-table-column prop="symbol" label="股票" width="100" />
              <el-table-column prop="side" label="方向" width="80">
                <template #default="{ row }">
                  <el-tag :type="row.side === 'buy' ? 'success' : 'danger'" size="small">
                    {{ row.side === 'buy' ? '买入' : '卖出' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column prop="price" label="价格" width="100">
                <template #default="{ row }">
                  ${{ row.price.toFixed(2) }}
                </template>
              </el-table-column>
              <el-table-column prop="quantity" label="数量" width="80" />
              <el-table-column prop="profit" label="盈亏">
                <template #default="{ row }">
                  <span v-if="row.profit !== undefined" :class="row.profit >= 0 ? 'price-up' : 'price-down'">
                    {{ row.profit >= 0 ? '+' : '' }}${{ row.profit.toFixed(2) }}
                  </span>
                  <span v-else>-</span>
                </template>
              </el-table-column>
            </el-table>
          </div>
        </template>

        <div v-else class="empty-detail">
          <el-icon :size="64"><DataAnalysis /></el-icon>
          <p>选择一个回测记录查看详情</p>
        </div>
      </el-col>
    </el-row>

    <!-- 创建回测对话框 -->
    <el-dialog v-model="showCreateDialog" title="新建回测" width="500px">
      <el-form :model="createForm" label-width="100px">
        <el-form-item label="选择策略" required>
          <el-select v-model="createForm.strategyId" placeholder="选择要回测的策略" style="width: 100%">
            <el-option
              v-for="s in strategies"
              :key="s.id"
              :label="s.name"
              :value="s.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="回测时间" required>
          <el-date-picker
            v-model="createForm.dateRange"
            type="daterange"
            start-placeholder="开始日期"
            end-placeholder="结束日期"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item label="初始资金">
          <el-input-number 
            v-model="createForm.initialCapital" 
            :min="1000" 
            :step="10000"
            style="width: 100%"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showCreateDialog = false">取消</el-button>
        <el-button type="primary" :loading="creating" @click="createBacktest">开始回测</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent } from 'echarts/components'
import VChart from 'vue-echarts'
import { Plus, DataAnalysis } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { useAppStore } from '@/stores/app'
import { backtestApi } from '@/api'
import dayjs from 'dayjs'
import type { Backtest } from '@/types'

use([CanvasRenderer, LineChart, GridComponent, TooltipComponent, LegendComponent])

const appStore = useAppStore()

const backtests = ref<Backtest[]>([])
const selectedId = ref<number | null>(null)
const showCreateDialog = ref(false)
const creating = ref(false)

const createForm = ref({
  strategyId: null as number | null,
  dateRange: [] as Date[],
  initialCapital: 100000
})

const strategies = computed(() => appStore.strategies)

const selectedBacktest = computed(() => 
  backtests.value.find(b => b.id === selectedId.value)
)

const equityChartOption = computed(() => {
  if (!selectedBacktest.value?.equityCurve) return {}

  const dates = selectedBacktest.value.equityCurve.map(p => p.date)
  const equity = selectedBacktest.value.equityCurve.map(p => p.equity)
  const drawdown = selectedBacktest.value.equityCurve.map(p => p.drawdown * 100)

  return {
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'cross' }
    },
    legend: {
      data: ['权益', '回撤'],
      top: 10
    },
    grid: [
      { left: '10%', right: '10%', top: '15%', height: '50%' },
      { left: '10%', right: '10%', top: '72%', height: '20%' }
    ],
    xAxis: [
      { type: 'category', data: dates, axisLabel: { show: false } },
      { type: 'category', data: dates, gridIndex: 1 }
    ],
    yAxis: [
      { 
        type: 'value',
        axisLabel: { formatter: (v: number) => `$${(v / 1000).toFixed(0)}K` }
      },
      { 
        type: 'value',
        gridIndex: 1,
        axisLabel: { formatter: '{value}%' },
        inverse: true
      }
    ],
    series: [
      {
        name: '权益',
        type: 'line',
        data: equity,
        smooth: true,
        lineStyle: { color: '#1a56db', width: 2 },
        areaStyle: {
          color: {
            type: 'linear',
            x: 0, y: 0, x2: 0, y2: 1,
            colorStops: [
              { offset: 0, color: 'rgba(26, 86, 219, 0.3)' },
              { offset: 1, color: 'rgba(26, 86, 219, 0.05)' }
            ]
          }
        },
        symbol: 'none'
      },
      {
        name: '回撤',
        type: 'line',
        xAxisIndex: 1,
        yAxisIndex: 1,
        data: drawdown,
        smooth: true,
        lineStyle: { color: '#ef4444', width: 1 },
        areaStyle: { color: 'rgba(239, 68, 68, 0.2)' },
        symbol: 'none'
      }
    ]
  }
})

function getStatusType(status: string) {
  switch (status) {
    case 'completed': return 'success'
    case 'running': return 'primary'
    case 'pending': return 'info'
    case 'failed': return 'danger'
    default: return 'info'
  }
}

function getStatusLabel(status: string) {
  switch (status) {
    case 'completed': return '已完成'
    case 'running': return '运行中'
    case 'pending': return '等待中'
    case 'failed': return '失败'
    default: return status
  }
}

function formatDate(date: string) {
  return dayjs(date).format('YYYY/MM/DD')
}

function selectBacktest(id: number) {
  selectedId.value = id
}

async function loadBacktests() {
  try {
    backtests.value = await backtestApi.list()
    if (backtests.value.length > 0 && !selectedId.value) {
      selectedId.value = backtests.value[0].id
    }
  } catch (error) {
    console.error('Failed to load backtests:', error)
  }
}

async function createBacktest() {
  if (!createForm.value.strategyId) {
    ElMessage.warning('请选择策略')
    return
  }
  if (!createForm.value.dateRange || createForm.value.dateRange.length !== 2) {
    ElMessage.warning('请选择回测时间范围')
    return
  }

  creating.value = true
  try {
    await backtestApi.create({
      strategyId: createForm.value.strategyId,
      startDate: dayjs(createForm.value.dateRange[0]).format('YYYY-MM-DD'),
      endDate: dayjs(createForm.value.dateRange[1]).format('YYYY-MM-DD'),
      initialCapital: createForm.value.initialCapital
    })
    ElMessage.success('回测任务已创建')
    showCreateDialog.value = false
    loadBacktests()
  } catch {
    ElMessage.error('创建失败')
  } finally {
    creating.value = false
  }
}

onMounted(() => {
  loadBacktests()
  appStore.fetchStrategies()
})
</script>

<style lang="scss" scoped>
.backtest {
  .card {
    background: #fff;
    border-radius: 8px;
    padding: 20px;
    border: 1px solid #e5e7eb;
    margin-bottom: 20px;

    h3 {
      font-size: 16px;
      font-weight: 600;
      margin: 0 0 16px;
    }
  }

  .stat-cards {
    margin-bottom: 16px;
  }

  .backtest-list {
    .backtest-item {
      padding: 16px;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      margin-bottom: 12px;
      cursor: pointer;
      transition: all 0.2s;

      &:hover {
        border-color: #1a56db;
      }

      &.active {
        border-color: #1a56db;
        background: #eff6ff;
      }

      .item-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 8px;

        .strategy-name {
          font-weight: 600;
        }
      }

      .item-info {
        font-size: 12px;
        color: #9ca3af;
        margin-bottom: 8px;
      }

      .item-result {
        display: flex;
        justify-content: space-between;
        font-size: 14px;

        .drawdown {
          color: #6b7280;
        }
      }
    }

    .empty-list {
      text-align: center;
      color: #9ca3af;
      padding: 40px;
    }
  }

  .chart-container {
    height: 350px;
  }

  .empty-detail {
    height: 400px;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    color: #9ca3af;

    p {
      margin-top: 16px;
    }
  }
}
</style>
