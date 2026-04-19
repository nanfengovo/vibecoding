<template>
  <div class="lowcode">
    <div class="page-header">
      <h1>低代码平台</h1>
      <div class="header-actions">
        <el-button :icon="RefreshRight" @click="newWorkflow">新建流程</el-button>
        <el-upload
          :show-file-list="false"
          :auto-upload="false"
          accept=".json"
          :on-change="importTemplateFile"
        >
          <el-button :icon="Upload">导入模板</el-button>
        </el-upload>
        <el-button :icon="Download" @click="exportTemplate(workflow)">导出当前</el-button>
      </div>
    </div>

    <el-row :gutter="20">
      <el-col :lg="16" :md="24">
        <div class="card">
          <div class="card-header">
            <h3>规则流编辑器</h3>
            <el-tag type="info">拖拽排序 + 条件逻辑 + 自动交易</el-tag>
          </div>

          <el-form :model="workflow" label-width="100px" class="workflow-form">
            <el-row :gutter="12">
              <el-col :md="12" :sm="24">
                <el-form-item label="流程名称">
                  <el-input v-model="workflow.name" placeholder="如：突破回调自动买入" />
                </el-form-item>
              </el-col>
              <el-col :md="12" :sm="24">
                <el-form-item label="默认股票">
                  <el-input v-model="workflow.symbol" placeholder="AAPL.US" />
                </el-form-item>
              </el-col>
              <el-col :span="24">
                <el-form-item label="说明">
                  <el-input v-model="workflow.description" type="textarea" :rows="2" />
                </el-form-item>
              </el-col>
              <el-col :md="12" :sm="24">
                <el-form-item label="流程启用">
                  <el-switch v-model="workflow.enabled" />
                </el-form-item>
              </el-col>
              <el-col :md="12" :sm="24">
                <el-form-item label="自动执行交易">
                  <el-switch v-model="workflow.autoExecute" />
                </el-form-item>
              </el-col>
            </el-row>
          </el-form>

          <div class="step-toolbar">
            <el-button size="small" type="primary" @click="addStep('query')">+ 查询</el-button>
            <el-button size="small" @click="addStep('formula')">+ 公式</el-button>
            <el-button size="small" @click="addStep('condition')">+ 条件</el-button>
            <el-button size="small" @click="addStep('trade')">+ 交易</el-button>
            <el-button size="small" @click="addStep('backtest')">+ 回测</el-button>
            <el-button size="small" @click="addStep('notify')">+ 通知</el-button>
          </div>

          <div class="step-list">
            <div
              v-for="(step, index) in workflow.steps"
              :key="step.id"
              class="step-card"
              draggable="true"
              @dragstart="handleDragStart(index)"
              @dragover.prevent
              @drop="handleDrop(index)"
              @dragend="handleDragEnd"
            >
              <div class="step-header">
                <div class="step-title">
                  <el-icon class="drag-icon"><Rank /></el-icon>
                  <span>{{ index + 1 }}. {{ step.name }}</span>
                </div>
                <div class="step-actions">
                  <el-switch v-model="step.enabled" size="small" />
                  <el-button text :icon="Delete" @click="removeStep(index)" />
                </div>
              </div>

              <el-form label-width="90px" class="step-form">
                <el-form-item label="步骤类型">
                  <el-select v-model="step.type" style="width: 220px" @change="changeStepType(step)">
                    <el-option label="接口查询" value="query" />
                    <el-option label="公式计算" value="formula" />
                    <el-option label="条件判断" value="condition" />
                    <el-option label="自动交易" value="trade" />
                    <el-option label="回测任务" value="backtest" />
                    <el-option label="消息通知" value="notify" />
                  </el-select>
                </el-form-item>

                <template v-if="step.type === 'query'">
                  <el-form-item label="查询接口">
                    <el-select v-model="step.config.queryType" style="width: 220px">
                      <el-option label="行情 Quote" value="quote" />
                      <el-option label="K线 Kline" value="kline" />
                      <el-option label="账户 Account" value="account" />
                    </el-select>
                  </el-form-item>
                  <el-form-item label="股票代码">
                    <el-input v-model="step.config.symbol" placeholder="留空使用流程默认股票" />
                  </el-form-item>
                  <el-form-item v-if="step.config.queryType === 'kline'" label="K线参数">
                    <div class="inline-group">
                      <el-select v-model="step.config.period" style="width: 110px">
                        <el-option label="1分" value="1m" />
                        <el-option label="5分" value="5m" />
                        <el-option label="日K" value="1d" />
                        <el-option label="周K" value="1w" />
                        <el-option label="月K" value="1M" />
                      </el-select>
                      <el-input-number v-model="step.config.count" :min="10" :max="500" />
                    </div>
                  </el-form-item>
                  <el-form-item label="结果变量">
                    <el-input v-model="step.config.resultKey" placeholder="如 quoteData" />
                  </el-form-item>
                </template>

                <template v-else-if="step.type === 'formula'">
                  <el-form-item label="公式表达式">
                    <el-input
                      v-model="step.config.expression"
                      type="textarea"
                      :rows="2"
                      placeholder="如：(currentPrice - previousClose) / previousClose * 100"
                    />
                  </el-form-item>
                  <el-form-item label="结果变量">
                    <el-input v-model="step.config.resultKey" placeholder="如 changePct" />
                  </el-form-item>
                </template>

                <template v-else-if="step.type === 'condition'">
                  <el-form-item label="左侧">
                    <el-input v-model="step.config.left" placeholder="变量名或表达式，如 currentPrice" />
                  </el-form-item>
                  <el-form-item label="运算符">
                    <el-select v-model="step.config.operator" style="width: 130px">
                      <el-option label=">" value=">" />
                      <el-option label="<" value="<" />
                      <el-option label=">=" value=">=" />
                      <el-option label="<=" value="<=" />
                      <el-option label="==" value="==" />
                      <el-option label="!=" value="!=" />
                    </el-select>
                  </el-form-item>
                  <el-form-item label="右侧">
                    <el-input v-model="step.config.right" placeholder="变量名或常量，如 180" />
                  </el-form-item>
                </template>

                <template v-else-if="step.type === 'trade'">
                  <el-form-item label="交易标的">
                    <el-input v-model="step.config.symbol" placeholder="留空使用流程默认股票" />
                  </el-form-item>
                  <el-form-item label="方向">
                    <el-radio-group v-model="step.config.side">
                      <el-radio-button value="buy">买入</el-radio-button>
                      <el-radio-button value="sell">卖出</el-radio-button>
                    </el-radio-group>
                  </el-form-item>
                  <el-form-item label="订单类型">
                    <el-radio-group v-model="step.config.orderType">
                      <el-radio-button value="market">市价</el-radio-button>
                      <el-radio-button value="limit">限价</el-radio-button>
                    </el-radio-group>
                  </el-form-item>
                  <el-form-item label="数量模式">
                    <el-radio-group v-model="step.config.quantityMode">
                      <el-radio-button value="fixed">固定值</el-radio-button>
                      <el-radio-button value="expression">表达式</el-radio-button>
                    </el-radio-group>
                  </el-form-item>
                  <el-form-item v-if="step.config.quantityMode === 'fixed'" label="数量">
                    <el-input-number v-model="step.config.quantity" :min="1" />
                  </el-form-item>
                  <el-form-item v-else label="数量表达式">
                    <el-input v-model="step.config.quantityExpression" placeholder="如 cash / currentPrice * 0.1" />
                  </el-form-item>
                  <el-form-item v-if="step.config.orderType === 'limit'" label="限价表达式">
                    <el-input v-model="step.config.priceExpression" placeholder="如 currentPrice * 0.995" />
                  </el-form-item>
                </template>

                <template v-else-if="step.type === 'backtest'">
                  <el-form-item label="策略">
                    <el-select
                      v-model="step.config.strategyId"
                      filterable
                      placeholder="选择策略"
                      style="width: 100%"
                    >
                      <el-option
                        v-for="strategy in appStore.strategies"
                        :key="strategy.id"
                        :label="strategy.name"
                        :value="strategy.id"
                      />
                    </el-select>
                  </el-form-item>
                  <el-form-item label="开始日期">
                    <el-date-picker
                      v-model="step.config.startDate"
                      type="date"
                      value-format="YYYY-MM-DD"
                      placeholder="开始日期"
                    />
                  </el-form-item>
                  <el-form-item label="结束日期">
                    <el-date-picker
                      v-model="step.config.endDate"
                      type="date"
                      value-format="YYYY-MM-DD"
                      placeholder="结束日期"
                    />
                  </el-form-item>
                  <el-form-item label="初始资金">
                    <el-input-number v-model="step.config.initialCapital" :min="1000" :step="1000" />
                  </el-form-item>
                </template>

                <template v-else-if="step.type === 'notify'">
                  <el-form-item label="通知渠道">
                    <el-select v-model="step.config.channel" style="width: 160px">
                      <el-option label="站内" value="inapp" />
                      <el-option label="邮件" value="email" />
                      <el-option label="飞书" value="feishu" />
                      <el-option label="企业微信" value="wechat" />
                    </el-select>
                  </el-form-item>
                  <el-form-item label="通知内容">
                    <el-input
                      v-model="step.config.message"
                      type="textarea"
                      :rows="2"
                      placeholder="支持 {{symbol}} {{currentPrice}} 变量"
                    />
                  </el-form-item>
                </template>
              </el-form>
            </div>

            <div v-if="workflow.steps.length === 0" class="empty-text">
              暂无步骤，请点击上方按钮添加
            </div>
          </div>

          <div class="editor-actions">
            <el-button type="primary" :loading="executing" @click="executeWorkflow">执行流程</el-button>
            <el-button @click="saveTemplate">保存模板</el-button>
          </div>
        </div>
      </el-col>

      <el-col :lg="8" :md="24">
        <div class="card">
          <h3>JSON 预览</h3>
          <pre class="json-preview">{{ prettyWorkflow }}</pre>
        </div>

        <div class="card">
          <div class="card-header">
            <h3>模板库</h3>
            <el-button link type="primary" @click="loadTemplates">刷新</el-button>
          </div>

          <div v-if="templates.length === 0" class="empty-text">暂无模板</div>
          <div v-for="tpl in templates" :key="tpl.id" class="template-item">
            <div class="template-main">
              <div class="template-name">{{ tpl.name }}</div>
              <div class="template-meta">
                {{ tpl.symbol || '未指定标的' }} · {{ formatTime(tpl.updatedAt) }}
              </div>
            </div>
            <div class="template-actions">
              <el-switch v-model="tpl.enabled" size="small" @change="persistTemplates" />
              <el-button size="small" @click="applyTemplate(tpl)">应用</el-button>
              <el-button size="small" @click="exportTemplate(tpl)">导出</el-button>
              <el-button size="small" type="danger" @click="removeTemplate(tpl.id)">删除</el-button>
            </div>
          </div>
        </div>

        <div class="card">
          <h3>执行日志</h3>
          <div v-if="executionLogs.length === 0" class="empty-text">暂无执行记录</div>
          <div v-for="(log, index) in executionLogs" :key="index" class="log-item">
            {{ log }}
          </div>
        </div>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Download, Rank, RefreshRight, Upload } from '@element-plus/icons-vue'
import { backtestApi, stockApi, tradeApi } from '@/api'
import { useAppStore } from '@/stores/app'

type StepType = 'query' | 'formula' | 'condition' | 'trade' | 'backtest' | 'notify'

interface WorkflowStep {
  id: string
  name: string
  type: StepType
  enabled: boolean
  config: Record<string, any>
}

interface WorkflowTemplate {
  id: string
  name: string
  description: string
  symbol: string
  enabled: boolean
  autoExecute: boolean
  steps: WorkflowStep[]
  updatedAt: string
}

const STORAGE_KEY = 'qt-lowcode-templates-v2'
const appStore = useAppStore()

const workflow = ref<WorkflowTemplate>(createDefaultWorkflow())
const templates = ref<WorkflowTemplate[]>([])
const executionLogs = ref<string[]>([])
const executing = ref(false)
const dragIndex = ref<number | null>(null)

function uid(prefix = 'node') {
  return `${prefix}-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`
}

function createStep(type: StepType): WorkflowStep {
  const now = dayjs()
  switch (type) {
    case 'query':
      return {
        id: uid('query'),
        name: '接口查询',
        type,
        enabled: true,
        config: {
          queryType: 'quote',
          symbol: '',
          period: '1d',
          count: 120,
          resultKey: 'quoteData'
        }
      }
    case 'formula':
      return {
        id: uid('formula'),
        name: '公式计算',
        type,
        enabled: true,
        config: {
          expression: '(currentPrice - previousClose) / previousClose * 100',
          resultKey: 'changePct'
        }
      }
    case 'condition':
      return {
        id: uid('condition'),
        name: '条件判断',
        type,
        enabled: true,
        config: {
          left: 'currentPrice',
          operator: '>',
          right: '180'
        }
      }
    case 'trade':
      return {
        id: uid('trade'),
        name: '自动交易',
        type,
        enabled: true,
        config: {
          symbol: '',
          side: 'buy',
          orderType: 'market',
          quantityMode: 'fixed',
          quantity: 100,
          quantityExpression: '100',
          priceExpression: 'currentPrice'
        }
      }
    case 'backtest':
      return {
        id: uid('backtest'),
        name: '回测任务',
        type,
        enabled: true,
        config: {
          strategyId: null,
          startDate: now.subtract(90, 'day').format('YYYY-MM-DD'),
          endDate: now.format('YYYY-MM-DD'),
          initialCapital: 100000
        }
      }
    case 'notify':
      return {
        id: uid('notify'),
        name: '消息通知',
        type,
        enabled: true,
        config: {
          channel: 'inapp',
          message: '规则触发：{{symbol}} 当前价 {{currentPrice}}'
        }
      }
  }
}

function cloneTemplate(template: WorkflowTemplate): WorkflowTemplate {
  return JSON.parse(JSON.stringify(template))
}

function createDefaultWorkflow(): WorkflowTemplate {
  return {
    id: uid('workflow'),
    name: '新建策略流',
    description: '',
    symbol: '',
    enabled: true,
    autoExecute: false,
    steps: [createStep('query'), createStep('condition'), createStep('trade')],
    updatedAt: new Date().toISOString()
  }
}

function normalizeStep(raw: any): WorkflowStep {
  const stepType: StepType = ['query', 'formula', 'condition', 'trade', 'backtest', 'notify'].includes(raw?.type)
    ? raw.type
    : 'query'
  const base = createStep(stepType)
  return {
    ...base,
    id: raw?.id ? String(raw.id) : base.id,
    name: raw?.name ? String(raw.name) : base.name,
    enabled: raw?.enabled !== false,
    config: {
      ...base.config,
      ...(raw?.config ?? {})
    }
  }
}

function normalizeTemplate(raw: any): WorkflowTemplate | null {
  if (!raw || typeof raw !== 'object') {
    return null
  }

  const name = String(raw?.name ?? '').trim()
  if (!name) {
    return null
  }

  const steps = Array.isArray(raw?.steps) ? raw.steps.map(normalizeStep) : []

  return {
    id: raw?.id ? String(raw.id) : uid('workflow'),
    name,
    description: String(raw?.description ?? ''),
    symbol: String(raw?.symbol ?? ''),
    enabled: raw?.enabled !== false,
    autoExecute: Boolean(raw?.autoExecute),
    steps,
    updatedAt: raw?.updatedAt ? String(raw.updatedAt) : new Date().toISOString()
  }
}

function pushLog(message: string) {
  executionLogs.value.unshift(`${dayjs().format('HH:mm:ss')} ${message}`)
  executionLogs.value = executionLogs.value.slice(0, 30)
}

function addStep(type: StepType) {
  workflow.value.steps.push(createStep(type))
}

function removeStep(index: number) {
  workflow.value.steps.splice(index, 1)
}

function changeStepType(step: WorkflowStep) {
  const replacement = createStep(step.type)
  step.name = replacement.name
  step.config = replacement.config
}

function handleDragStart(index: number) {
  dragIndex.value = index
}

function handleDrop(index: number) {
  if (dragIndex.value === null || dragIndex.value === index) {
    return
  }
  const next = [...workflow.value.steps]
  const [moved] = next.splice(dragIndex.value, 1)
  next.splice(index, 0, moved)
  workflow.value.steps = next
  dragIndex.value = null
}

function handleDragEnd() {
  dragIndex.value = null
}

function persistTemplates() {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(templates.value))
}

function loadTemplates() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    const parsed = raw ? JSON.parse(raw) : []
    const next = Array.isArray(parsed)
      ? parsed.map(normalizeTemplate).filter(Boolean) as WorkflowTemplate[]
      : []
    templates.value = next
  } catch {
    templates.value = []
  }
}

function newWorkflow() {
  workflow.value = createDefaultWorkflow()
}

async function saveTemplate() {
  try {
    const { value } = await ElMessageBox.prompt('请输入模板名称', '保存模板', {
      confirmButtonText: '保存',
      cancelButtonText: '取消',
      inputValue: workflow.value.name,
      inputPattern: /.+/,
      inputErrorMessage: '模板名称不能为空'
    })

    const saved = cloneTemplate(workflow.value)
    saved.name = value.trim()
    saved.updatedAt = new Date().toISOString()

    const index = templates.value.findIndex(t => t.id === saved.id)
    if (index >= 0) {
      templates.value.splice(index, 1, saved)
    } else {
      templates.value.unshift(saved)
    }

    workflow.value = cloneTemplate(saved)
    persistTemplates()
    ElMessage.success('模板已保存')
  } catch {
    // ignore cancel
  }
}

function applyTemplate(template: WorkflowTemplate) {
  workflow.value = cloneTemplate(template)
}

async function removeTemplate(id: string) {
  try {
    await ElMessageBox.confirm('确认删除该模板？', '提示', {
      type: 'warning'
    })
    templates.value = templates.value.filter(item => item.id !== id)
    persistTemplates()
    ElMessage.success('模板已删除')
  } catch {
    // ignore cancel
  }
}

function exportTemplate(template: WorkflowTemplate) {
  const payload = JSON.stringify(template, null, 2)
  const blob = new Blob([payload], { type: 'application/json;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `${template.name || 'lowcode-template'}.json`
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)
}

async function importTemplateFile(file: any) {
  const rawFile = file?.raw as File | undefined
  if (!rawFile) {
    return
  }

  try {
    const text = await rawFile.text()
    const parsed = JSON.parse(text)
    const source = Array.isArray(parsed) ? parsed : [parsed]
    const normalized = source
      .map(normalizeTemplate)
      .filter(Boolean) as WorkflowTemplate[]

    if (normalized.length === 0) {
      ElMessage.warning('未识别到有效模板')
      return
    }

    normalized.forEach((item) => {
      const existing = templates.value.findIndex(t => t.id === item.id)
      const next = {
        ...item,
        id: existing >= 0 ? uid('workflow') : item.id,
        updatedAt: new Date().toISOString()
      }
      templates.value.unshift(next)
    })
    persistTemplates()
    ElMessage.success(`已导入 ${normalized.length} 个模板`)
  } catch {
    ElMessage.error('模板导入失败，请检查 JSON 格式')
  }
}

function formatTime(value: string) {
  const time = dayjs(value)
  return time.isValid() ? time.format('MM-DD HH:mm') : '-'
}

function evaluateExpression(expression: string, context: Record<string, any>) {
  const source = String(expression ?? '').trim()
  if (!source) {
    return Number.NaN
  }

  const keys = Object.keys(context).filter((key) => /^[a-zA-Z_][a-zA-Z0-9_]*$/.test(key))
  const values = keys.map((key) => context[key])

  try {
    const runner = new Function(...keys, `return (${source});`)
    const result = runner(...values)
    const numeric = Number(result)
    return Number.isFinite(numeric) ? numeric : Number.NaN
  } catch {
    return Number.NaN
  }
}

function resolveOperand(value: any, context: Record<string, any>) {
  if (typeof value === 'number') {
    return value
  }

  if (typeof value === 'string') {
    const source = value.trim()
    const direct = Number(source)
    if (Number.isFinite(direct)) {
      return direct
    }

    const fromContext = Number(context[source])
    if (Number.isFinite(fromContext)) {
      return fromContext
    }

    return evaluateExpression(source, context)
  }

  return Number.NaN
}

function compareNumbers(left: number, operator: string, right: number) {
  switch (operator) {
    case '>':
      return left > right
    case '<':
      return left < right
    case '>=':
      return left >= right
    case '<=':
      return left <= right
    case '==':
      return Math.abs(left - right) < 0.000001
    case '!=':
      return Math.abs(left - right) >= 0.000001
    default:
      return false
  }
}

function renderMessage(template: string, context: Record<string, any>) {
  return String(template ?? '').replace(/\{\{\s*([a-zA-Z0-9_]+)\s*\}\}/g, (_, key: string) => {
    const value = context[key]
    if (value === undefined || value === null) {
      return '-'
    }
    return String(value)
  })
}

async function executeWorkflow() {
  if (!workflow.value.enabled) {
    ElMessage.warning('当前流程已禁用，请先启用后再执行')
    return
  }

  const enabledSteps = workflow.value.steps.filter(step => step.enabled)
  if (enabledSteps.length === 0) {
    ElMessage.warning('当前流程没有启用的步骤')
    return
  }

  executing.value = true
  const context: Record<string, any> = {
    symbol: workflow.value.symbol?.trim().toUpperCase() || '',
    now: new Date().toISOString()
  }

  try {
    for (const step of enabledSteps) {
      if (step.type === 'query') {
        const queryType = String(step.config.queryType || 'quote')
        const symbol = String(step.config.symbol || workflow.value.symbol || '').trim().toUpperCase()
        const resultKey = String(step.config.resultKey || 'queryResult')

        if (queryType !== 'account' && !symbol) {
          throw new Error(`步骤「${step.name}」缺少股票代码`)
        }

        if (queryType === 'quote') {
          const quote: any = await stockApi.getQuote(symbol)
          const currentPrice = Number(quote?.current ?? quote?.price ?? 0)
          const previousClose = Number(quote?.previousClose ?? quote?.prevClose ?? 0)
          const quoteData = {
            symbol,
            currentPrice,
            previousClose,
            high: Number(quote?.high ?? 0),
            low: Number(quote?.low ?? 0),
            volume: Number(quote?.volume ?? 0),
            turnover: Number(quote?.turnover ?? 0)
          }

          context.symbol = symbol
          context.currentPrice = currentPrice
          context.previousClose = previousClose
          context[resultKey] = quoteData
          pushLog(`[查询] ${symbol} 当前价 ${currentPrice.toFixed(2)}`)
        } else if (queryType === 'kline') {
          const period = String(step.config.period || '1d')
          const count = Math.max(10, Number(step.config.count || 120))
          const lines: any[] = await stockApi.getKline(symbol, period, count)
          context.symbol = symbol
          context[resultKey] = lines
          if (Array.isArray(lines) && lines.length > 0) {
            const last = lines[lines.length - 1]
            context.lastClose = Number(last?.close ?? 0)
          }
          pushLog(`[查询] ${symbol} K线 ${Array.isArray(lines) ? lines.length : 0} 条`)
        } else {
          const account: any = await tradeApi.getAccount()
          context[resultKey] = account
          context.cash = Number(account?.cash ?? 0)
          context.totalAssets = Number(account?.totalAssets ?? 0)
          pushLog('[查询] 账户信息已更新')
        }
      } else if (step.type === 'formula') {
        const expression = String(step.config.expression || '')
        const resultKey = String(step.config.resultKey || 'formulaResult')
        const result = evaluateExpression(expression, context)
        if (!Number.isFinite(result)) {
          throw new Error(`步骤「${step.name}」公式计算失败`)
        }
        context[resultKey] = result
        pushLog(`[公式] ${resultKey} = ${result.toFixed(4)}`)
      } else if (step.type === 'condition') {
        const left = resolveOperand(step.config.left, context)
        const right = resolveOperand(step.config.right, context)
        const operator = String(step.config.operator || '>')
        if (!Number.isFinite(left) || !Number.isFinite(right)) {
          throw new Error(`步骤「${step.name}」条件参数无效`)
        }

        const passed = compareNumbers(left, operator, right)
        context.lastCondition = passed
        pushLog(`[条件] ${left} ${operator} ${right} => ${passed ? '通过' : '不通过'}`)

        if (!passed) {
          ElMessage.warning(`流程终止：步骤「${step.name}」条件不满足`)
          return
        }
      } else if (step.type === 'trade') {
        if (!workflow.value.autoExecute) {
          pushLog('[交易] 已跳过（自动交易开关关闭）')
          continue
        }

        const symbol = String(step.config.symbol || workflow.value.symbol || context.symbol || '').trim().toUpperCase()
        if (!symbol) {
          throw new Error(`步骤「${step.name}」缺少交易标的`)
        }

        const side = String(step.config.side || 'buy') === 'sell' ? 'sell' : 'buy'
        const orderType = String(step.config.orderType || 'market') === 'limit' ? 'limit' : 'market'
        const quantityMode = String(step.config.quantityMode || 'fixed')
        const quantity = quantityMode === 'expression'
          ? evaluateExpression(String(step.config.quantityExpression || ''), context)
          : Number(step.config.quantity || 0)

        if (!Number.isFinite(quantity) || quantity <= 0) {
          throw new Error(`步骤「${step.name}」交易数量无效`)
        }

        let price: number | undefined
        if (orderType === 'limit') {
          const evaluatedPrice = evaluateExpression(String(step.config.priceExpression || 'currentPrice'), context)
          if (!Number.isFinite(evaluatedPrice) || evaluatedPrice <= 0) {
            throw new Error(`步骤「${step.name}」限价表达式无效`)
          }
          price = evaluatedPrice
        }

        const order = await tradeApi.placeOrder({
          symbol,
          side: side as 'buy' | 'sell',
          orderType: orderType as 'market' | 'limit',
          quantity: Number(quantity.toFixed(4)),
          price
        })

        context.lastOrder = order
        pushLog(`[交易] 已提交 ${side.toUpperCase()} ${symbol} x${Number(quantity.toFixed(4))}`)
      } else if (step.type === 'backtest') {
        const strategyId = Number(step.config.strategyId || 0)
        const startDate = String(step.config.startDate || '')
        const endDate = String(step.config.endDate || '')
        const initialCapital = Number(step.config.initialCapital || 100000)

        if (!strategyId || !startDate || !endDate) {
          throw new Error(`步骤「${step.name}」回测参数不完整`)
        }

        const result = await backtestApi.create({
          strategyId,
          startDate,
          endDate,
          initialCapital
        })
        context.lastBacktest = result
        pushLog(`[回测] 已创建任务（策略 ${strategyId}）`)
      } else if (step.type === 'notify') {
        const channel = String(step.config.channel || 'inapp')
        const message = renderMessage(String(step.config.message || ''), context)
        appStore.addNotification({
          type: 'info',
          title: `低代码通知(${channel})`,
          message
        })
        pushLog(`[通知] ${message}`)
      }
    }

    ElMessage.success('流程执行完成')
  } catch (error: any) {
    const message = error?.response?.data?.message || error?.message || '执行失败'
    pushLog(`[错误] ${message}`)
    ElMessage.error(message)
  } finally {
    executing.value = false
  }
}

const prettyWorkflow = computed(() =>
  JSON.stringify(workflow.value, null, 2)
)

onMounted(async () => {
  loadTemplates()
  await Promise.all([appStore.fetchWatchlist(), appStore.fetchStrategies()])
})
</script>

<style lang="scss" scoped>
.lowcode {
  .card {
    background: var(--qt-card-bg);
    border: 1px solid var(--qt-border);
    border-radius: 8px;
    padding: 20px;
    margin-bottom: 20px;
  }

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 10px;
    margin-bottom: 16px;
  }

  .header-actions {
    display: flex;
    gap: 8px;
    flex-wrap: wrap;
    justify-content: flex-end;
  }

  .workflow-form {
    margin-bottom: 12px;
  }

  .step-toolbar {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    margin-bottom: 12px;
  }

  .step-list {
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .step-card {
    border: 1px dashed var(--qt-border);
    border-radius: 8px;
    padding: 12px;
    background: color-mix(in srgb, var(--qt-card-bg) 90%, #1a56db 10%);
  }

  .step-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 8px;
    gap: 8px;
  }

  .step-title {
    display: flex;
    align-items: center;
    gap: 8px;
    font-weight: 600;
    color: var(--qt-text-primary);
  }

  .drag-icon {
    color: var(--qt-text-secondary);
    cursor: grab;
  }

  .step-actions {
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .step-form {
    :deep(.el-form-item:last-child) {
      margin-bottom: 0;
    }
  }

  .inline-group {
    display: flex;
    gap: 8px;
    align-items: center;
    flex-wrap: wrap;
  }

  .editor-actions {
    margin-top: 16px;
    display: flex;
    gap: 10px;
    flex-wrap: wrap;
  }

  .json-preview {
    max-height: 320px;
    overflow: auto;
    background: #0f172a;
    color: #e2e8f0;
    border-radius: 8px;
    padding: 12px;
    line-height: 1.5;
    font-size: 12px;
    white-space: pre-wrap;
  }

  .template-item {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 10px;
    border: 1px solid var(--qt-border);
    border-radius: 8px;
    padding: 10px;
    margin-bottom: 10px;
  }

  .template-main {
    min-width: 0;
  }

  .template-name {
    font-weight: 600;
    color: var(--qt-text-primary);
  }

  .template-meta {
    color: var(--qt-text-secondary);
    font-size: 12px;
    margin-top: 2px;
  }

  .template-actions {
    display: flex;
    align-items: center;
    gap: 6px;
    flex-wrap: wrap;
    justify-content: flex-end;
  }

  .log-item {
    font-size: 12px;
    padding: 6px 0;
    border-bottom: 1px dashed var(--qt-border);
    color: var(--qt-text-secondary);
  }

  .empty-text {
    color: var(--qt-text-muted);
    font-size: 13px;
  }
}

@media (max-width: 960px) {
  .lowcode {
    .card {
      padding: 12px;
    }

    .template-item {
      flex-direction: column;
      align-items: flex-start;
    }

    .template-actions {
      width: 100%;
      justify-content: flex-start;
    }
  }
}
</style>
