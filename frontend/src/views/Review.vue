<template>
  <div class="review">
    <div class="page-header">
      <h1>复盘分析</h1>
      <div class="header-actions">
        <el-button type="primary" :icon="Plus" @click="showCreateDialog = true">
          新建复盘
        </el-button>
      </div>
    </div>

    <el-row :gutter="20">
      <!-- 左侧：复盘列表 -->
      <el-col :xs="24" :lg="8">
        <div class="card review-list">
          <div class="list-header">
            <el-date-picker
              v-model="selectedMonth"
              type="month"
              placeholder="选择月份"
              @change="loadReviews"
            />
          </div>
          <el-scrollbar height="600px">
            <div 
              v-for="item in reviews" 
              :key="item.id" 
              :class="['review-item', { active: selectedId === item.id }]"
              @click="selectReview(item.id)"
            >
              <div class="item-date">
                <span class="day">{{ formatDay(item.date) }}</span>
                <span class="weekday">{{ formatWeekday(item.date) }}</span>
              </div>
              <div class="item-content">
                <div class="item-summary">{{ item.marketSummary }}</div>
                <div class="item-tags">
                  <el-tag 
                    v-for="tag in item.tags.slice(0, 3)" 
                    :key="tag" 
                    size="small"
                  >
                    {{ tag }}
                  </el-tag>
                </div>
              </div>
            </div>
            <div v-if="reviews.length === 0" class="empty-list">
              暂无复盘记录
            </div>
          </el-scrollbar>
        </div>
      </el-col>

      <!-- 右侧：复盘详情 -->
      <el-col :xs="24" :lg="16">
        <template v-if="selectedReview">
          <div class="card">
            <div class="review-header">
              <h2>{{ formatFullDate(selectedReview.date) }} 复盘</h2>
              <div class="header-actions">
                <el-button size="small" @click="editReview">编辑</el-button>
                <el-button size="small" type="danger" @click="deleteReview">删除</el-button>
              </div>
            </div>

            <div class="review-section">
              <h3>市场概况</h3>
              <p>{{ selectedReview.marketSummary }}</p>
            </div>

            <div class="review-section">
              <h3>当日交易</h3>
              <div class="glass-list-view">
                <div class="gl-header">
                  <div class="gl-col" style="width: 100px">股票</div>
                  <div class="gl-col" style="width: 80px">方向</div>
                  <div class="gl-col" style="width: 80px; text-align: right">数量</div>
                  <div class="gl-col" style="width: 100px; text-align: right">价格</div>
                  <div class="gl-col" style="flex: 1; text-align: right">金额</div>
                </div>
                <div class="gl-body" v-if="selectedReview.trades?.length">
                  <div v-for="(row, idx) in selectedReview.trades" :key="idx" class="gl-row">
                    <div class="gl-col" style="width: 100px">{{ row.symbol }}</div>
                    <div class="gl-col" style="width: 80px">
                      <span :class="row.side === 'buy' ? 'price-up' : 'price-down'">
                        {{ row.side === 'buy' ? '买入' : '卖出' }}
                      </span>
                    </div>
                    <div class="gl-col number-font" style="width: 80px; text-align: right">{{ row.quantity }}</div>
                    <div class="gl-col number-font" style="width: 100px; text-align: right">${{ row.price.toFixed(2) }}</div>
                    <div class="gl-col number-font" style="flex: 1; text-align: right">${{ row.amount.toFixed(2) }}</div>
                  </div>
                </div>
                <div v-else class="gl-empty">暂无交易记录</div>
              </div>
            </div>

            <div class="review-section">
              <h3>交易笔记</h3>
              <p>{{ selectedReview.notes || '暂无笔记' }}</p>
            </div>

            <div class="review-section">
              <h3>经验教训</h3>
              <p>{{ selectedReview.lessons || '暂无总结' }}</p>
            </div>

            <div class="review-section">
              <h3>标签</h3>
              <div class="tags">
                <el-tag v-for="tag in selectedReview.tags" :key="tag">{{ tag }}</el-tag>
              </div>
            </div>
          </div>
        </template>

        <div v-else class="empty-detail">
          <el-icon :size="64"><Document /></el-icon>
          <p>选择一个复盘记录查看详情</p>
        </div>
      </el-col>
    </el-row>

    <!-- 创建/编辑复盘对话框 -->
    <el-dialog v-model="showCreateDialog" :title="editingId ? '编辑复盘' : '新建复盘'" width="700px">
      <el-form :model="reviewForm" label-width="100px">
        <el-form-item label="日期" required>
          <el-date-picker
            v-model="reviewForm.date"
            type="date"
            placeholder="选择日期"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item label="市场概况" required>
          <el-input
            v-model="reviewForm.marketSummary"
            type="textarea"
            :rows="3"
            placeholder="描述当日市场整体情况..."
          />
        </el-form-item>
        <el-form-item label="交易笔记">
          <el-input
            v-model="reviewForm.notes"
            type="textarea"
            :rows="4"
            placeholder="记录交易过程中的思考..."
          />
        </el-form-item>
        <el-form-item label="经验教训">
          <el-input
            v-model="reviewForm.lessons"
            type="textarea"
            :rows="4"
            placeholder="总结今天的经验教训..."
          />
        </el-form-item>
        <el-form-item label="标签">
          <el-select
            v-model="reviewForm.tags"
            multiple
            filterable
            allow-create
            placeholder="添加标签"
            style="width: 100%"
          >
            <el-option label="盈利" value="盈利" />
            <el-option label="亏损" value="亏损" />
            <el-option label="波动" value="波动" />
            <el-option label="震荡" value="震荡" />
            <el-option label="趋势" value="趋势" />
            <el-option label="突破" value="突破" />
            <el-option label="回调" value="回调" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showCreateDialog = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="saveReview">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { Plus, Document } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { reviewApi } from '@/api'
import dayjs from 'dayjs'
import type { ReviewRecord } from '@/types'

const reviews = ref<ReviewRecord[]>([])
const selectedId = ref<number | null>(null)
const selectedMonth = ref<Date>(new Date())
const showCreateDialog = ref(false)
const editingId = ref<number | null>(null)
const saving = ref(false)

const reviewForm = ref({
  date: new Date(),
  marketSummary: '',
  notes: '',
  lessons: '',
  tags: [] as string[]
})

const selectedReview = computed(() => 
  reviews.value.find(r => r.id === selectedId.value)
)

function formatDay(date: string) {
  return dayjs(date).format('DD')
}

function formatWeekday(date: string) {
  const weekdays = ['周日', '周一', '周二', '周三', '周四', '周五', '周六']
  return weekdays[dayjs(date).day()]
}

function formatFullDate(date: string) {
  return dayjs(date).format('YYYY年MM月DD日')
}

function selectReview(id: number) {
  selectedId.value = id
}

async function loadReviews() {
  try {
    const startDate = dayjs(selectedMonth.value).startOf('month').format('YYYY-MM-DD')
    const endDate = dayjs(selectedMonth.value).endOf('month').format('YYYY-MM-DD')
    reviews.value = await reviewApi.list({ startDate, endDate })
    
    if (reviews.value.length > 0 && !selectedId.value) {
      selectedId.value = reviews.value[0].id
    }
  } catch (error) {
    console.error('Failed to load reviews:', error)
  }
}

function editReview() {
  if (!selectedReview.value) return
  
  editingId.value = selectedReview.value.id
  reviewForm.value = {
    date: new Date(selectedReview.value.date),
    marketSummary: selectedReview.value.marketSummary,
    notes: selectedReview.value.notes,
    lessons: selectedReview.value.lessons,
    tags: [...selectedReview.value.tags]
  }
  showCreateDialog.value = true
}

async function deleteReview() {
  if (!selectedId.value) return
  
  try {
    await ElMessageBox.confirm('确定要删除此复盘记录吗?', '提示')
    await reviewApi.delete(selectedId.value)
    selectedId.value = null
    loadReviews()
    ElMessage.success('已删除')
  } catch {}
}

async function saveReview() {
  if (!reviewForm.value.marketSummary) {
    ElMessage.warning('请填写市场概况')
    return
  }

  saving.value = true
  try {
    const data = {
      date: dayjs(reviewForm.value.date).format('YYYY-MM-DD'),
      marketSummary: reviewForm.value.marketSummary,
      notes: reviewForm.value.notes,
      lessons: reviewForm.value.lessons,
      tags: reviewForm.value.tags,
      trades: []
    }

    if (editingId.value) {
      await reviewApi.update(editingId.value, data)
      ElMessage.success('已更新')
    } else {
      await reviewApi.create(data)
      ElMessage.success('已创建')
    }

    showCreateDialog.value = false
    editingId.value = null
    reviewForm.value = {
      date: new Date(),
      marketSummary: '',
      notes: '',
      lessons: '',
      tags: []
    }
    loadReviews()
  } catch {
    ElMessage.error('保存失败')
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  loadReviews()
})
</script>

<style lang="scss" scoped>
.review {
  .card {
    background: var(--qt-card-bg);
    border-radius: 8px;
    padding: 20px;
    border: 1px solid var(--qt-border);
    margin-bottom: 20px;
  }

  .review-list {
    .list-header {
      margin-bottom: 16px;
    }

    .review-item {
      display: flex;
      gap: 16px;
      padding: 16px;
      border: 1px solid var(--qt-border);
      border-radius: 8px;
      margin-bottom: 12px;
      cursor: pointer;
      transition: all 0.2s;

      &:hover {
        border-color: #1a56db;
      }

      &.active {
        border-color: #1a56db;
        background: rgba(26, 86, 219, 0.12);
      }

      .item-date {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        width: 50px;

        .day {
          font-size: 24px;
          font-weight: 700;
          color: #1a56db;
        }

        .weekday {
          font-size: 12px;
          color: var(--qt-text-muted);
        }
      }

      .item-content {
        flex: 1;
        min-width: 0;

        .item-summary {
          font-size: 14px;
          color: var(--qt-text-primary);
          margin-bottom: 8px;
          display: -webkit-box;
          -webkit-line-clamp: 2;
          -webkit-box-orient: vertical;
          overflow: hidden;
        }

        .item-tags {
          display: flex;
          gap: 4px;
        }
      }
    }

    .empty-list {
      text-align: center;
      color: var(--qt-text-muted);
      padding: 40px;
    }
  }

  .review-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 24px;
    gap: 10px;
    flex-wrap: wrap;

    h2 {
      margin: 0;
      font-size: 20px;
    }
  }

  .review-section {
    margin-bottom: 24px;

    h3 {
      font-size: 16px;
      font-weight: 600;
      margin: 0 0 12px;
      color: var(--qt-text-primary);
    }

    p {
      color: var(--qt-text-secondary);
      line-height: 1.6;
      margin: 0;
    }

    .tags {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }
  }

  .empty-detail {
    height: 400px;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    color: var(--qt-text-muted);

    p {
      margin-top: 16px;
    }
  }

  /* Glass List View */
  .glass-list-view {
    background: transparent;
    border: 1px solid var(--qt-border);
    border-radius: 8px;
    overflow: hidden;

    .gl-header {
      display: flex;
      align-items: center;
      padding: 12px 16px;
      border-bottom: 1px solid var(--qt-border);
      font-size: 13px;
      font-weight: 600;
      color: var(--qt-text-secondary);
      background: rgba(0, 0, 0, 0.15);
    }

    .gl-row {
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

    .gl-empty {
      padding: 40px;
      text-align: center;
      color: var(--qt-text-muted);
    }
  }
}

@media (max-width: 960px) {
  .review {
    .card {
      padding: 12px;
    }

    .review-list {
      margin-bottom: 12px;
    }

    .review-item {
      padding: 12px;
    }
  }
}
</style>
