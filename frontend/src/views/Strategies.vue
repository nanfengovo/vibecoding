<template>
  <div class="strategies">
    <div class="page-header">
      <h1>策略中心</h1>
      <div class="header-actions">
        <el-button type="primary" :icon="Plus" @click="$router.push('/strategy/create')">
          创建策略
        </el-button>
      </div>
    </div>

    <!-- 策略统计 -->
    <el-row :gutter="20" class="stat-cards">
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-label">总策略数</div>
          <div class="stat-value total-value">{{ strategies.length }}</div>
        </div>
      </el-col>
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-label">运行中</div>
          <div class="stat-value running-value">{{ activeCount }}</div>
        </div>
      </el-col>
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-label">已暂停</div>
          <div class="stat-value paused-value">{{ pausedCount }}</div>
        </div>
      </el-col>
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-label">今日执行</div>
          <div class="stat-value total-value">{{ todayExecutions }}</div>
        </div>
      </el-col>
    </el-row>

    <!-- 策略列表 -->
    <div class="card">
      <div class="card-header">
        <el-input
          v-model="searchQuery"
          placeholder="搜索策略..."
          :prefix-icon="Search"
          style="width: 240px"
        />
        <el-radio-group v-model="statusFilter">
          <el-radio-button value="">全部</el-radio-button>
          <el-radio-button value="active">运行中</el-radio-button>
          <el-radio-button value="paused">已暂停</el-radio-button>
        </el-radio-group>
      </div>

      <el-table :data="filteredStrategies" style="width: 100%">
        <el-table-column prop="name" label="策略名称" width="200">
          <template #default="{ row }">
            <div class="strategy-name">
              <el-icon :class="['status-dot', row.isActive ? 'active' : 'paused']">
                <component :is="row.isActive ? 'VideoPause' : 'VideoPlay'" />
              </el-icon>
              <span>{{ row.name }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="description" label="描述" show-overflow-tooltip />
        <el-table-column prop="targetSymbols" label="目标股票" width="180">
          <template #default="{ row }">
            <div class="symbol-tags">
              <el-tag 
                v-for="s in (row.config?.targetSymbols || []).slice(0, 3)" 
                :key="s" 
                size="small"
              >
                {{ s }}
              </el-tag>
              <el-tag 
                v-if="(row.config?.targetSymbols || []).length > 3" 
                size="small" 
                type="info"
              >
                +{{ row.config.targetSymbols.length - 3 }}
              </el-tag>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="conditions" label="条件数" width="80" align="center">
          <template #default="{ row }">
            {{ row.config?.conditions?.length || 0 }}
          </template>
        </el-table-column>
        <el-table-column prop="lastExecutedAt" label="最后执行" width="160">
          <template #default="{ row }">
            {{ row.lastExecutedAt ? formatDate(row.lastExecutedAt) : '-' }}
          </template>
        </el-table-column>
        <el-table-column prop="isActive" label="状态" width="100">
          <template #default="{ row }">
            <el-switch 
              v-model="row.isActive" 
              :loading="toggling === row.id"
              @change="toggleStrategy(row)"
            />
          </template>
        </el-table-column>
        <el-table-column label="操作" width="200" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="executeStrategy(row.id)">
              执行
            </el-button>
            <el-button link type="primary" size="small" @click="reloadStrategy(row.id)">
              热重载
            </el-button>
            <el-button link type="primary" size="small" @click="editStrategy(row.id)">
              编辑
            </el-button>
            <el-button link type="danger" size="small" @click="deleteStrategy(row.id)">
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { Plus, Search } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useAppStore } from '@/stores/app'
import { strategyApi } from '@/api'
import dayjs from 'dayjs'

const router = useRouter()
const appStore = useAppStore()

const searchQuery = ref('')
const statusFilter = ref('')
const toggling = ref<number | null>(null)

const strategies = computed(() => appStore.strategies)

const activeCount = computed(() => 
  strategies.value.filter(s => s.isActive).length
)

const pausedCount = computed(() => 
  strategies.value.filter(s => !s.isActive).length
)

const todayExecutions = computed(() =>
  strategies.value.filter(s => s.lastExecutedAt && dayjs(s.lastExecutedAt).isSame(dayjs(), 'day')).length
)

const filteredStrategies = computed(() => {
  let result = strategies.value

  if (searchQuery.value) {
    const query = searchQuery.value.toLowerCase()
    result = result.filter(s => 
      s.name.toLowerCase().includes(query) ||
      s.description?.toLowerCase().includes(query)
    )
  }

  if (statusFilter.value === 'active') {
    result = result.filter(s => s.isActive)
  } else if (statusFilter.value === 'paused') {
    result = result.filter(s => !s.isActive)
  }

  return result
})

function formatDate(date: string): string {
  return dayjs(date).format('MM/DD HH:mm:ss')
}

async function toggleStrategy(strategy: any) {
  toggling.value = strategy.id
  try {
    await strategyApi.toggle(strategy.id, strategy.isActive)
    ElMessage.success(strategy.isActive ? '策略已启动' : '策略已暂停')
  } catch {
    strategy.isActive = !strategy.isActive
    ElMessage.error('操作失败')
  } finally {
    toggling.value = null
  }
}

async function executeStrategy(id: number) {
  try {
    await strategyApi.execute(id)
    ElMessage.success('策略执行已触发')
  } catch {
    ElMessage.error('执行失败')
  }
}

async function reloadStrategy(id: number) {
  try {
    await strategyApi.reload(id)
    ElMessage.success('策略已热重载')
  } catch {
    ElMessage.error('热重载失败')
  }
}

function editStrategy(id: number) {
  router.push(`/strategy/${id}/edit`)
}

async function deleteStrategy(id: number) {
  try {
    await ElMessageBox.confirm('确定要删除此策略吗? 此操作不可恢复', '警告', {
      type: 'warning'
    })
    await strategyApi.delete(id)
    await appStore.fetchStrategies()
    ElMessage.success('策略已删除')
  } catch {}
}

onMounted(() => {
  appStore.fetchStrategies()
})
</script>

<style lang="scss" scoped>
.strategies {
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
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 10px;
    flex-wrap: wrap;
    margin-bottom: 16px;
  }

  .strategy-name {
    display: flex;
    align-items: center;
    gap: 8px;

    .status-dot {
      font-size: 14px;

      &.active {
        color: #10b981;
      }

      &.paused {
        color: #f59e0b;
      }
    }
  }

  .symbol-tags {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }

  .total-value {
    color: var(--qt-text-primary);
  }

  .running-value {
    color: #10b981;
  }

  .paused-value {
    color: #f59e0b;
  }
}

@media (max-width: 960px) {
  .strategies {
    .card {
      padding: 12px;
    }

    .stat-cards {
      :deep(.el-col) {
        max-width: 50%;
        flex: 0 0 50%;
      }
    }
  }
}
</style>
