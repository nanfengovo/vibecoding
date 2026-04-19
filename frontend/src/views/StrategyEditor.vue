<template>
  <div class="strategy-editor">
    <div class="page-header">
      <h1>{{ isEdit ? '编辑策略' : '创建策略' }}</h1>
      <div class="header-actions">
        <el-button @click="$router.back()">取消</el-button>
        <el-button type="primary" :loading="saving" @click="saveStrategy">
          保存策略
        </el-button>
      </div>
    </div>

    <el-row :gutter="20">
      <!-- 左侧：基本信息和条件 -->
      <el-col :span="16">
        <!-- 基本信息 -->
        <div class="card">
          <h3>基本信息</h3>
          <el-form :model="form" label-width="100px">
            <el-form-item label="策略名称" required>
              <el-input v-model="form.name" placeholder="输入策略名称" />
            </el-form-item>
            <el-form-item label="策略描述">
              <el-input 
                v-model="form.description" 
                type="textarea" 
                :rows="3" 
                placeholder="描述策略的目的和逻辑"
              />
            </el-form-item>
            <el-form-item label="目标股票" required>
              <el-select
                v-model="form.targetSymbols"
                multiple
                filterable
                allow-create
                placeholder="选择或输入股票代码"
                style="width: 100%"
              >
                <el-option
                  v-for="item in watchlistOptions"
                  :key="item.symbol"
                  :label="`${item.symbol} - ${item.name}`"
                  :value="item.symbol"
                />
              </el-select>
            </el-form-item>
            <el-form-item label="检查间隔">
              <el-select v-model="form.checkInterval" style="width: 200px">
                <el-option label="1分钟" :value="60" />
                <el-option label="5分钟" :value="300" />
                <el-option label="15分钟" :value="900" />
                <el-option label="30分钟" :value="1800" />
                <el-option label="1小时" :value="3600" />
              </el-select>
            </el-form-item>
          </el-form>
        </div>

        <!-- 触发条件 -->
        <div class="card">
          <div class="card-header">
            <h3>触发条件</h3>
            <el-button type="primary" size="small" :icon="Plus" @click="addCondition">
              添加条件
            </el-button>
          </div>
          <div class="conditions-list">
            <div 
              v-for="(condition, index) in form.conditions" 
              :key="condition.id" 
              class="condition-item"
            >
              <div class="condition-header">
                <span class="condition-index">条件 {{ index + 1 }}</span>
                <div class="condition-actions">
                  <el-select 
                    v-if="index > 0" 
                    v-model="condition.operator" 
                    size="small" 
                    style="width: 80px"
                  >
                    <el-option label="并且" value="and" />
                    <el-option label="或者" value="or" />
                  </el-select>
                  <el-button 
                    :icon="Delete" 
                    circle 
                    size="small" 
                    @click="removeCondition(index)"
                  />
                </div>
              </div>
              <div class="condition-body">
                <el-select 
                  v-model="condition.type" 
                  placeholder="选择条件类型" 
                  style="width: 180px"
                >
                  <el-option-group label="价格条件">
                    <el-option label="价格高于" value="price_above" />
                    <el-option label="价格低于" value="price_below" />
                    <el-option label="涨跌幅超过" value="price_change_percent" />
                  </el-option-group>
                  <el-option-group label="成交量条件">
                    <el-option label="成交量高于" value="volume_above" />
                    <el-option label="成交量变化超过" value="volume_change_percent" />
                  </el-option-group>
                  <el-option-group label="均线条件">
                    <el-option label="均线金叉" value="ma_cross_up" />
                    <el-option label="均线死叉" value="ma_cross_down" />
                  </el-option-group>
                  <el-option-group label="技术指标">
                    <el-option label="RSI超买" value="rsi_above" />
                    <el-option label="RSI超卖" value="rsi_below" />
                    <el-option label="MACD金叉" value="macd_cross_up" />
                    <el-option label="MACD死叉" value="macd_cross_down" />
                    <el-option label="KDJ金叉" value="kdj_golden_cross" />
                    <el-option label="KDJ死叉" value="kdj_death_cross" />
                    <el-option label="突破布林上轨" value="boll_upper_break" />
                    <el-option label="跌破布林下轨" value="boll_lower_break" />
                  </el-option-group>
                </el-select>
                
                <!-- 参数配置 -->
                <template v-if="getConditionParams(condition.type).length > 0">
                  <div 
                    v-for="param in getConditionParams(condition.type)" 
                    :key="param.key"
                    class="param-input"
                  >
                    <span class="param-label">{{ param.label }}</span>
                    <el-input-number 
                      v-model="condition.params[param.key]" 
                      :min="param.min" 
                      :max="param.max"
                      :step="param.step || 1"
                      :precision="param.precision || 0"
                      size="small"
                    />
                    <span v-if="param.suffix" class="param-suffix">{{ param.suffix }}</span>
                  </div>
                </template>
              </div>
            </div>

            <div v-if="form.conditions.length === 0" class="empty-conditions">
              <el-icon :size="48"><Warning /></el-icon>
              <p>还没有添加任何条件</p>
              <el-button type="primary" @click="addCondition">添加第一个条件</el-button>
            </div>
          </div>
        </div>

        <!-- 执行动作 -->
        <div class="card">
          <div class="card-header">
            <h3>执行动作</h3>
            <el-button type="primary" size="small" :icon="Plus" @click="addAction">
              添加动作
            </el-button>
          </div>
          <div class="actions-list">
            <div 
              v-for="(action, index) in form.actions" 
              :key="action.id" 
              class="action-item"
            >
              <div class="action-header">
                <span class="action-index">动作 {{ index + 1 }}</span>
                <el-button 
                  :icon="Delete" 
                  circle 
                  size="small" 
                  @click="removeAction(index)"
                />
              </div>
              <div class="action-body">
                <el-select 
                  v-model="action.type" 
                  placeholder="选择动作类型" 
                  style="width: 150px"
                >
                  <el-option-group label="交易操作">
                    <el-option label="买入" value="buy" />
                    <el-option label="卖出" value="sell" />
                  </el-option-group>
                  <el-option-group label="通知">
                    <el-option label="发送邮件" value="notify_email" />
                    <el-option label="飞书通知" value="notify_feishu" />
                    <el-option label="企业微信通知" value="notify_wechat" />
                  </el-option-group>
                  <el-option-group label="其他">
                    <el-option label="记录日志" value="log" />
                  </el-option-group>
                </el-select>

                <!-- 交易参数 -->
                <template v-if="action.type === 'buy' || action.type === 'sell'">
                  <div class="param-input">
                    <span class="param-label">数量</span>
                    <el-input-number 
                      v-model="action.params.quantity" 
                      :min="1" 
                      size="small"
                    />
                    <span class="param-suffix">股</span>
                  </div>
                  <div class="param-input">
                    <span class="param-label">价格</span>
                    <el-select v-model="action.params.priceType" size="small" style="width: 100px">
                      <el-option label="市价" value="market" />
                      <el-option label="限价" value="limit" />
                    </el-select>
                    <el-input-number 
                      v-if="action.params.priceType === 'limit'"
                      v-model="action.params.limitPrice" 
                      :min="0" 
                      :precision="2"
                      size="small"
                    />
                  </div>
                </template>

                <!-- 通知参数 -->
                <template v-if="action.type?.startsWith('notify_')">
                  <div class="param-input">
                    <span class="param-label">消息内容</span>
                    <el-input 
                      v-model="action.params.message" 
                      placeholder="留空使用默认模板"
                      size="small"
                      style="width: 300px"
                    />
                  </div>
                </template>
              </div>
            </div>

            <div v-if="form.actions.length === 0" class="empty-actions">
              <el-icon :size="48"><Warning /></el-icon>
              <p>还没有添加任何动作</p>
              <el-button type="primary" @click="addAction">添加第一个动作</el-button>
            </div>
          </div>
        </div>
      </el-col>

      <!-- 右侧：预览和帮助 -->
      <el-col :span="8">
        <!-- 策略预览 -->
        <div class="card strategy-preview">
          <h3>策略预览</h3>
          <div class="preview-content">
            <div class="preview-section">
              <div class="preview-label">监控股票</div>
              <div class="preview-value">
                <el-tag 
                  v-for="s in form.targetSymbols" 
                  :key="s" 
                  size="small" 
                  class="symbol-tag"
                >
                  {{ s }}
                </el-tag>
                <span v-if="form.targetSymbols.length === 0" class="empty-text">未设置</span>
              </div>
            </div>
            <div class="preview-section">
              <div class="preview-label">触发条件</div>
              <div class="preview-value">
                <template v-if="form.conditions.length > 0">
                  <div 
                    v-for="(c, i) in form.conditions" 
                    :key="c.id" 
                    class="condition-preview"
                  >
                    <span v-if="i > 0" class="operator">{{ c.operator === 'and' ? '且' : '或' }}</span>
                    <span class="condition-tag">{{ formatCondition(c) }}</span>
                  </div>
                </template>
                <span v-else class="empty-text">未设置</span>
              </div>
            </div>
            <div class="preview-section">
              <div class="preview-label">执行动作</div>
              <div class="preview-value">
                <template v-if="form.actions.length > 0">
                  <span 
                    v-for="a in form.actions" 
                    :key="a.id" 
                    class="action-tag"
                  >
                    {{ formatAction(a) }}
                  </span>
                </template>
                <span v-else class="empty-text">未设置</span>
              </div>
            </div>
          </div>
        </div>

        <!-- 帮助信息 -->
        <div class="card help-info">
          <h3>使用帮助</h3>
          <el-collapse accordion>
            <el-collapse-item title="条件类型说明">
              <ul>
                <li><strong>价格条件</strong>：基于股票当前价格触发</li>
                <li><strong>成交量条件</strong>：基于成交量变化触发</li>
                <li><strong>均线条件</strong>：基于MA均线交叉触发</li>
                <li><strong>技术指标</strong>：基于RSI、MACD、KDJ等指标触发</li>
              </ul>
            </el-collapse-item>
            <el-collapse-item title="动作类型说明">
              <ul>
                <li><strong>买入/卖出</strong>：自动下单交易</li>
                <li><strong>通知</strong>：通过邮件、飞书或微信发送提醒</li>
                <li><strong>日志</strong>：记录策略触发日志</li>
              </ul>
            </el-collapse-item>
            <el-collapse-item title="热重载说明">
              <p>策略保存后可以随时修改并热重载，无需重启系统即可生效。</p>
            </el-collapse-item>
          </el-collapse>
        </div>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Plus, Delete, Warning } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { useAppStore } from '@/stores/app'
import { strategyApi } from '@/api'

const route = useRoute()
const router = useRouter()
const appStore = useAppStore()

const isEdit = computed(() => !!route.params.id)
const strategyId = computed(() => Number(route.params.id))
const saving = ref(false)

const form = ref({
  name: '',
  description: '',
  targetSymbols: [] as string[],
  checkInterval: 300,
  conditions: [] as any[],
  actions: [] as any[]
})

const watchlistOptions = computed(() => appStore.watchlist)

const conditionParamsMap: Record<string, any[]> = {
  price_above: [{ key: 'value', label: '价格', min: 0, precision: 2, suffix: '$' }],
  price_below: [{ key: 'value', label: '价格', min: 0, precision: 2, suffix: '$' }],
  price_change_percent: [{ key: 'value', label: '百分比', min: -100, max: 100, precision: 2, suffix: '%' }],
  volume_above: [{ key: 'value', label: '成交量', min: 0, step: 1000 }],
  volume_change_percent: [{ key: 'value', label: '百分比', min: 0, precision: 2, suffix: '%' }],
  ma_cross_up: [
    { key: 'shortPeriod', label: '短期', min: 1, max: 60 },
    { key: 'longPeriod', label: '长期', min: 1, max: 120 }
  ],
  ma_cross_down: [
    { key: 'shortPeriod', label: '短期', min: 1, max: 60 },
    { key: 'longPeriod', label: '长期', min: 1, max: 120 }
  ],
  rsi_above: [
    { key: 'period', label: '周期', min: 1, max: 30 },
    { key: 'value', label: '阈值', min: 0, max: 100 }
  ],
  rsi_below: [
    { key: 'period', label: '周期', min: 1, max: 30 },
    { key: 'value', label: '阈值', min: 0, max: 100 }
  ],
  macd_cross_up: [],
  macd_cross_down: [],
  kdj_golden_cross: [],
  kdj_death_cross: [],
  boll_upper_break: [{ key: 'period', label: '周期', min: 1, max: 30 }],
  boll_lower_break: [{ key: 'period', label: '周期', min: 1, max: 30 }]
}

function getConditionParams(type: string): any[] {
  return conditionParamsMap[type] || []
}

function addCondition() {
  form.value.conditions.push({
    id: globalThis.crypto.randomUUID(),
    type: '',
    params: {},
    operator: 'and'
  })
}

function removeCondition(index: number) {
  form.value.conditions.splice(index, 1)
}

function addAction() {
  form.value.actions.push({
    id: globalThis.crypto.randomUUID(),
    type: '',
    params: {}
  })
}

function removeAction(index: number) {
  form.value.actions.splice(index, 1)
}

function formatCondition(condition: any): string {
  const labels: Record<string, string> = {
    price_above: '价格 >',
    price_below: '价格 <',
    price_change_percent: '涨跌幅',
    volume_above: '成交量 >',
    volume_change_percent: '成交量变化',
    ma_cross_up: '均线金叉',
    ma_cross_down: '均线死叉',
    rsi_above: 'RSI >',
    rsi_below: 'RSI <',
    macd_cross_up: 'MACD金叉',
    macd_cross_down: 'MACD死叉',
    kdj_golden_cross: 'KDJ金叉',
    kdj_death_cross: 'KDJ死叉',
    boll_upper_break: '突破布林上轨',
    boll_lower_break: '跌破布林下轨'
  }
  const label = labels[condition.type] || condition.type
  const value = condition.params?.value
  return value !== undefined ? `${label} ${value}` : label
}

function formatAction(action: any): string {
  const labels: Record<string, string> = {
    buy: '买入',
    sell: '卖出',
    notify_email: '邮件通知',
    notify_feishu: '飞书通知',
    notify_wechat: '微信通知',
    log: '记录日志'
  }
  return labels[action.type] || action.type
}

async function saveStrategy() {
  if (!form.value.name) {
    ElMessage.warning('请输入策略名称')
    return
  }
  if (form.value.targetSymbols.length === 0) {
    ElMessage.warning('请选择目标股票')
    return
  }
  if (form.value.conditions.length === 0) {
    ElMessage.warning('请添加至少一个触发条件')
    return
  }
  if (form.value.actions.length === 0) {
    ElMessage.warning('请添加至少一个执行动作')
    return
  }

  saving.value = true
  try {
    const data = {
      name: form.value.name,
      description: form.value.description,
      config: {
        conditions: form.value.conditions,
        actions: form.value.actions,
        targetSymbols: form.value.targetSymbols,
        checkInterval: form.value.checkInterval
      },
      isActive: false
    }

    if (isEdit.value) {
      await strategyApi.update(strategyId.value, data)
      ElMessage.success('策略已更新')
    } else {
      await strategyApi.create(data)
      ElMessage.success('策略已创建')
    }

    await appStore.fetchStrategies()
    router.push('/strategies')
  } catch (error) {
    ElMessage.error('保存失败')
  } finally {
    saving.value = false
  }
}

async function loadStrategy() {
  if (!isEdit.value) return

  try {
    const strategy = await strategyApi.get(strategyId.value)
    form.value = {
      name: strategy.name,
      description: strategy.description || '',
      targetSymbols: strategy.config?.targetSymbols || [],
      checkInterval: strategy.config?.checkInterval || 300,
      conditions: strategy.config?.conditions || [],
      actions: strategy.config?.actions || []
    }
  } catch {
    ElMessage.error('加载策略失败')
    router.push('/strategies')
  }
}

onMounted(() => {
  loadStrategy()
})
</script>

<style lang="scss" scoped>
.strategy-editor {
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

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 16px;

    h3 {
      margin: 0;
    }
  }

  .condition-item,
  .action-item {
    background: #f9fafb;
    border-radius: 8px;
    padding: 16px;
    margin-bottom: 12px;

    .condition-header,
    .action-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 12px;
    }

    .condition-index,
    .action-index {
      font-weight: 500;
      color: #374151;
    }

    .condition-actions {
      display: flex;
      gap: 8px;
      align-items: center;
    }

    .condition-body,
    .action-body {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: center;
    }

    .param-input {
      display: flex;
      align-items: center;
      gap: 8px;

      .param-label {
        font-size: 13px;
        color: #6b7280;
      }

      .param-suffix {
        font-size: 13px;
        color: #9ca3af;
      }
    }
  }

  .empty-conditions,
  .empty-actions {
    text-align: center;
    padding: 40px;
    color: #9ca3af;

    p {
      margin: 12px 0 16px;
    }
  }

  .strategy-preview {
    position: sticky;
    top: 20px;

    .preview-content {
      .preview-section {
        margin-bottom: 16px;
        padding-bottom: 16px;
        border-bottom: 1px solid #f3f4f6;

        &:last-child {
          border-bottom: none;
          margin-bottom: 0;
          padding-bottom: 0;
        }
      }

      .preview-label {
        font-size: 12px;
        color: #9ca3af;
        margin-bottom: 8px;
      }

      .preview-value {
        display: flex;
        flex-wrap: wrap;
        gap: 6px;
      }

      .symbol-tag {
        margin: 0;
      }

      .condition-preview {
        display: flex;
        align-items: center;
        gap: 4px;
        margin-bottom: 4px;

        .operator {
          font-size: 12px;
          color: #9ca3af;
        }
      }

      .condition-tag {
        background: #dbeafe;
        color: #1e40af;
        padding: 2px 8px;
        border-radius: 4px;
        font-size: 12px;
      }

      .action-tag {
        background: #fef3c7;
        color: #92400e;
        padding: 2px 8px;
        border-radius: 4px;
        font-size: 12px;
      }

      .empty-text {
        color: #9ca3af;
        font-style: italic;
      }
    }
  }

  .help-info {
    :deep(.el-collapse) {
      border: none;
    }

    :deep(.el-collapse-item__header) {
      background: transparent;
      font-size: 14px;
    }

    :deep(.el-collapse-item__content) {
      padding-bottom: 0;
    }

    ul {
      padding-left: 20px;
      margin: 0;

      li {
        margin-bottom: 8px;
        font-size: 13px;
        color: #6b7280;
      }
    }

    p {
      font-size: 13px;
      color: #6b7280;
      margin: 0;
    }
  }
}
</style>
