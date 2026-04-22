<template>
  <div class="ai-chat-page">
    <div class="page-header">
      <h1>AI Chat</h1>
      <div class="header-actions">
        <el-button plain @click="createSession">
          <el-icon><Plus /></el-icon>
          新建会话
        </el-button>
        <el-button @click="clearCurrentSession">清空当前会话</el-button>
      </div>
    </div>

    <div class="chat-shell card">
      <aside class="session-panel">
        <div class="panel-title">
          <span>历史会话</span>
          <span class="session-count">{{ sessions.length }}</span>
        </div>
        <el-scrollbar class="session-scroll">
          <div
            v-for="session in sessions"
            :key="session.id"
            :class="['session-item', { active: session.id === activeSessionId }]"
            @click="switchSession(session.id)"
          >
            <div class="session-top">
              <span class="session-name" :title="session.title">{{ session.title }}</span>
              <el-popconfirm
                title="确认删除这个会话？"
                width="180"
                @confirm="deleteSession(session.id)"
              >
                <template #reference>
                  <el-button
                    text
                    class="delete-btn"
                    :icon="Delete"
                    @click.stop
                  />
                </template>
              </el-popconfirm>
            </div>
            <div class="session-meta">
              <span>{{ formatTime(session.updatedAt) }}</span>
              <span>{{ session.messages.length }} 条</span>
            </div>
          </div>
        </el-scrollbar>
      </aside>

      <section class="chat-main">
        <div class="chat-config">
          <el-form label-width="84px" inline class="config-form">
            <el-form-item label="模型源">
              <el-select v-model="selectedProviderId" placeholder="选择模型源" style="width: 230px">
                <el-option
                  v-for="provider in providers"
                  :key="provider.id"
                  :label="provider.name"
                  :value="provider.id"
                />
              </el-select>
            </el-form-item>
            <el-form-item label="模型">
              <el-select v-model="selectedModel" placeholder="选择模型" style="width: 300px">
                <el-option
                  v-for="model in currentModels"
                  :key="model"
                  :label="model"
                  :value="model"
                />
              </el-select>
            </el-form-item>
            <el-form-item label="Skill">
              <el-select v-model="selectedSkillId" placeholder="可选：选择分析技能" clearable style="width: 230px">
                <el-option
                  v-for="skill in skillOptions"
                  :key="skill.id"
                  :label="skill.label"
                  :value="skill.id"
                />
              </el-select>
            </el-form-item>
            <el-form-item label="标的">
              <el-input v-model="symbol" placeholder="可选，如 NVDA / 600519.SH" style="width: 230px" />
            </el-form-item>
          </el-form>
          <div class="quick-prompts">
            <span class="label">快捷问题：</span>
            <el-button
              v-for="prompt in quickPrompts"
              :key="prompt"
              size="small"
              @click="applyPrompt(prompt)"
            >
              {{ prompt }}
            </el-button>
          </div>
        </div>

        <div ref="messageListRef" class="chat-messages">
          <div v-if="activeMessages.length === 0" class="empty-tip">
            你可以问：趋势判断、仓位管理、交易计划、风控、事件驱动影响、财报解读等。
          </div>

          <div
            v-for="item in activeMessages"
            :key="item.id"
            :class="['msg-item', item.role, { error: item.isError }]"
          >
            <div class="msg-meta">
              <span class="role">{{ item.role === 'user' ? '你' : 'AI' }}</span>
              <span class="time">{{ formatTime(item.time) }}</span>
              <span v-if="item.model" class="model">{{ item.model }}</span>
              <el-button
                v-if="item.role === 'assistant'"
                link
                size="small"
                class="render-toggle"
                @click="toggleRawMode(item.id)"
              >
                {{ isRawMode(item.id) ? '渲染显示' : '查看原文' }}
              </el-button>
            </div>

            <div
              v-if="item.role === 'assistant' && item.marketContext"
              class="market-context"
            >
              <span class="market-chip source">{{ item.marketContext.source }}</span>
              <span class="market-chip symbol">{{ item.marketContext.symbol }}</span>
              <span class="market-chip">行情时间 {{ formatQuoteTime(item.marketContext.quoteTime) }}</span>
              <span class="market-chip">时延 {{ formatLag(item.marketContext.lagSeconds) }}</span>
              <span :class="['market-chip', 'freshness', item.marketContext.freshness]">
                {{ freshnessLabel(item.marketContext.freshness) }}
              </span>
            </div>

            <pre v-if="item.role === 'assistant' && isRawMode(item.id)" class="msg-content raw-content">{{ item.content }}</pre>
            <div
              v-else-if="item.role === 'assistant'"
              class="msg-content markdown-content"
              v-html="renderedMessageMap[item.id]"
            />
            <div v-else class="msg-content user-content">
              {{ item.content }}
            </div>
          </div>

          <el-skeleton v-if="sending" :rows="4" animated />
        </div>

        <div class="chat-input">
          <el-input
            v-model="draftQuestion"
            type="textarea"
            :rows="4"
            placeholder="输入股票或交易相关问题，回车发送（Shift+回车换行）"
            @keydown.enter.prevent="handleEnter"
          />
          <div class="input-actions">
            <el-tooltip content="先优化提问提示词" placement="top">
              <el-button
                circle
                :icon="MagicStick"
                :loading="optimizing"
                @click="optimizeQuestion"
              />
            </el-tooltip>
            <el-button type="primary" :loading="sending" @click="sendQuestion">发送</el-button>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import dayjs from 'dayjs'
import { Delete, MagicStick, Plus } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { aiApi, configApi } from '@/api'
import type { AiChatMarketContext, AiProviderConfig, SystemConfig } from '@/types'
import { useAiChatStore } from '@/stores/aiChat'
import { parseAiMarkdown } from '@/utils/aiMarkdown'

const chatStore = useAiChatStore()
const providers = ref<AiProviderConfig[]>([])
const defaultProviderId = ref('')
const rawModeMap = ref<Record<string, boolean>>({})
const messageListRef = ref<HTMLElement | null>(null)
const optimizing = ref(false)

const quickPrompts = [
  '帮我看下这只股票短中期趋势',
  '给一个保守型交易计划（含止损）',
  '这只股票有哪些关键风险点',
  '如果我只做波段，仓位怎么分配'
]

const skillOptions = [
  { id: 'cross-market-selection', label: '跨市场选股' },
  { id: 'technical-diagnosis', label: '技术面诊断' },
  { id: 'financial-research', label: '财报研究' },
  { id: 'smart-money-tracking', label: '聪明钱追踪' },
  { id: 'advanced-order', label: '进阶下单' },
  { id: 'position-review', label: '持仓复盘' }
]

const sessions = computed(() => chatStore.sessions)
const activeSession = computed(() => chatStore.activeSession)
const activeSessionId = computed(() => chatStore.activeSessionId)
const activeMessages = computed(() => activeSession.value?.messages || [])
const sending = computed(() => chatStore.sending)

const draftQuestion = computed({
  get: () => chatStore.draftQuestion,
  set: (value: string) => chatStore.setDraftQuestion(value)
})

const selectedProviderId = computed({
  get: () => activeSession.value?.providerId || '',
  set: (value: string) => chatStore.updateSessionMeta({ providerId: value })
})

const selectedModel = computed({
  get: () => activeSession.value?.model || '',
  set: (value: string) => chatStore.updateSessionMeta({ model: value })
})

const selectedSkillId = computed({
  get: () => activeSession.value?.skillId || '',
  set: (value: string) => chatStore.updateSessionMeta({ skillId: value || '' })
})

const symbol = computed({
  get: () => activeSession.value?.symbol || '',
  set: (value: string) => chatStore.updateSessionMeta({ symbol: value })
})

const currentProvider = computed(() => {
  const selected = selectedProviderId.value
  if (selected) {
    const matched = providers.value.find((item) => item.id === selected)
    if (matched) {
      return matched
    }
  }
  return providers.value[0] || null
})

const currentModels = computed(() => {
  const raw = currentProvider.value?.model || ''
  const list = parseModelCandidates(raw)
  return list.length > 0 ? list : ['gpt-5-mini']
})

const renderedMessageMap = computed<Record<string, string>>(() => {
  const rows: Record<string, string> = {}
  for (const item of activeMessages.value) {
    if (item.role !== 'assistant') {
      continue
    }
    rows[item.id] = parseAiMarkdown(item.content || '', `chat-${item.id}`).html
  }
  return rows
})

function parseModelCandidates(raw: string): string[] {
  const list = String(raw || '')
    .split(/[\n,;|]+/g)
    .map((item) => item.trim())
    .filter(Boolean)
  return Array.from(new Set(list))
}

function createProvider(seed?: Partial<AiProviderConfig>, index = 0): AiProviderConfig {
  return {
    id: String(seed?.id || `provider-${index + 1}`),
    name: String(seed?.name || `模型源 ${index + 1}`),
    apiKey: String(seed?.apiKey || ''),
    baseUrl: String(seed?.baseUrl || '').trim() || 'https://api.openai.com/v1',
    model: String(seed?.model || '').trim() || 'gpt-5-mini'
  }
}

function normalizeProviders(openAi?: SystemConfig['openAi']): AiProviderConfig[] {
  const rows = Array.isArray(openAi?.providers)
    ? openAi.providers.map((item, index) => createProvider(item, index))
    : []
  if (rows.length > 0) {
    return rows
  }

  return [
    createProvider({
      id: 'default',
      name: '默认模型源',
      apiKey: String(openAi?.apiKey || ''),
      baseUrl: String(openAi?.baseUrl || '').trim() || 'https://api.openai.com/v1',
      model: String(openAi?.model || '').trim() || 'gpt-5-mini'
    })
  ]
}

function ensureSessionDefaults() {
  const session = activeSession.value
  if (!session || providers.value.length === 0) {
    return
  }

  const providerId = providers.value.some((item) => item.id === session.providerId)
    ? session.providerId
    : (defaultProviderId.value || providers.value[0].id)

  const provider = providers.value.find((item) => item.id === providerId) || providers.value[0]
  const candidateModels = parseModelCandidates(provider?.model || '')
  const model = candidateModels.includes(session.model)
    ? session.model
    : (candidateModels[0] || 'gpt-5-mini')

  chatStore.updateSessionMeta({
    providerId,
    model
  })
}

async function loadProviders() {
  try {
    const config = await configApi.get()
    const rows = normalizeProviders(config?.openAi)
    providers.value = rows
    const preferred = String(config?.openAi?.activeProviderId || '').trim()
    defaultProviderId.value = rows.some((item) => item.id === preferred) ? preferred : (rows[0]?.id || '')
    ensureSessionDefaults()
  } catch {
    providers.value = [createProvider(undefined, 0)]
    defaultProviderId.value = providers.value[0].id
    ensureSessionDefaults()
  }
}

function createSession() {
  chatStore.createNewSession()
  ensureSessionDefaults()
}

function switchSession(sessionId: string) {
  chatStore.switchSession(sessionId)
  ensureSessionDefaults()
}

function deleteSession(sessionId: string) {
  chatStore.removeSession(sessionId)
  ensureSessionDefaults()
}

function clearCurrentSession() {
  chatStore.clearActiveSessionMessages()
  ElMessage.success('当前会话已清空')
}

function applyPrompt(value: string) {
  draftQuestion.value = value
}

function formatTime(value: string) {
  return dayjs(value).format('MM-DD HH:mm:ss')
}

function formatQuoteTime(value: string) {
  const parsed = dayjs(value)
  if (!parsed.isValid()) {
    return value
  }
  return parsed.format('MM-DD HH:mm:ss')
}

function formatLag(value: number) {
  const seconds = Number(value || 0)
  if (seconds < 60) {
    return `${seconds}s`
  }
  if (seconds < 3600) {
    return `${Math.round(seconds / 60)}m`
  }
  return `${(seconds / 3600).toFixed(1)}h`
}

function freshnessLabel(freshness: AiChatMarketContext['freshness']) {
  if (freshness === 'realtime') {
    return '实时'
  }
  if (freshness === 'delayed_close') {
    return '闭市延迟'
  }
  return '过期'
}

function isRawMode(messageId: string) {
  return Boolean(rawModeMap.value[messageId])
}

function toggleRawMode(messageId: string) {
  rawModeMap.value = {
    ...rawModeMap.value,
    [messageId]: !rawModeMap.value[messageId]
  }
}

function handleEnter(event: Event | KeyboardEvent) {
  if (!(event instanceof KeyboardEvent)) {
    return
  }

  if (event.shiftKey) {
    draftQuestion.value += '\n'
    return
  }
  void sendQuestion()
}

async function scrollToBottom() {
  await nextTick()
  const element = messageListRef.value
  if (!element) {
    return
  }
  element.scrollTop = element.scrollHeight
}

async function sendQuestion() {
  const text = String(draftQuestion.value || '').trim()
  if (!text) {
    ElMessage.warning('请输入问题')
    return
  }

  await chatStore.sendMessage({
    question: text,
    symbol: symbol.value || undefined,
    skillId: selectedSkillId.value || undefined,
    providerId: selectedProviderId.value || undefined,
    model: selectedModel.value || undefined
  })

  await scrollToBottom()
}

async function optimizeQuestion() {
  const text = String(draftQuestion.value || '').trim()
  if (!text) {
    ElMessage.warning('请先输入问题再优化')
    return
  }

  if (optimizing.value) {
    return
  }

  optimizing.value = true
  try {
    const result = await aiApi.optimizePrompt({
      question: text,
      symbol: symbol.value || undefined,
      providerId: selectedProviderId.value || undefined,
      model: selectedModel.value || undefined
    })
    const nextPrompt = String(result?.optimizedPrompt || '').trim()
    if (!nextPrompt) {
      throw new Error('优化结果为空')
    }
    draftQuestion.value = nextPrompt
    ElMessage.success('提示词已优化')
  } catch (error: any) {
    const message = error?.response?.data?.message || error?.message || '提示词优化失败'
    ElMessage.error(message)
  } finally {
    optimizing.value = false
  }
}

watch(
  () => [activeSession.value?.id, providers.value.length],
  () => {
    ensureSessionDefaults()
    void scrollToBottom()
  },
  { immediate: true }
)

watch(
  () => activeMessages.value.length,
  () => {
    void scrollToBottom()
  }
)

onMounted(() => {
  chatStore.ensureHydrated()
  loadProviders()
})
</script>

<style lang="scss" scoped>
.ai-chat-page {
  .header-actions {
    display: flex;
    align-items: center;
    gap: 10px;
  }

  .chat-shell {
    display: grid;
    grid-template-columns: 260px minmax(0, 1fr);
    gap: 14px;
    padding: 14px;
    min-height: calc(100vh - 210px);
  }

  .session-panel {
    border: 1px solid var(--qt-border);
    border-radius: 10px;
    background: color-mix(in srgb, var(--qt-card-bg) 92%, #334155 8%);
    overflow: hidden;
    display: flex;
    flex-direction: column;
  }

  .panel-title {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 12px;
    border-bottom: 1px solid var(--qt-border);
    font-weight: 600;
    color: var(--qt-text-primary);

    .session-count {
      font-size: 12px;
      color: var(--qt-text-muted);
      font-weight: 500;
    }
  }

  .session-scroll {
    flex: 1;
    padding: 8px;
  }

  .session-item {
    border: 1px solid var(--qt-border);
    border-radius: 10px;
    padding: 9px 10px;
    margin-bottom: 8px;
    cursor: pointer;
    transition: all 0.2s ease;

    &:hover {
      border-color: color-mix(in srgb, #3b82f6 55%, var(--qt-border) 45%);
      background: color-mix(in srgb, var(--qt-card-bg) 90%, #3b82f6 10%);
    }

    &.active {
      border-color: #3b82f6;
      background: color-mix(in srgb, var(--qt-card-bg) 87%, #3b82f6 13%);
      box-shadow: 0 0 0 1px color-mix(in srgb, #3b82f6 40%, transparent 60%);
    }
  }

  .session-top {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
  }

  .session-name {
    font-size: 13px;
    font-weight: 600;
    color: var(--qt-text-primary);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    flex: 1;
  }

  .delete-btn {
    color: #ef4444;
    padding: 0;
    min-height: auto;
  }

  .session-meta {
    margin-top: 6px;
    display: flex;
    justify-content: space-between;
    font-size: 12px;
    color: var(--qt-text-muted);
  }

  .chat-main {
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .chat-config {
    border: 1px solid var(--qt-border);
    border-radius: 10px;
    background: linear-gradient(
      180deg,
      color-mix(in srgb, var(--qt-card-bg) 95%, #64748b 5%) 0%,
      color-mix(in srgb, var(--qt-card-bg) 100%, #64748b 0%) 100%
    );
    padding: 12px;
  }

  .config-form {
    margin-bottom: 6px;
  }

  .quick-prompts {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    align-items: center;

    .label {
      font-size: 12px;
      color: var(--qt-text-muted);
      margin-right: 2px;
    }
  }

  .chat-messages {
    flex: 1;
    min-height: 380px;
    max-height: calc(100vh - 420px);
    overflow-y: auto;
    border: 1px solid var(--qt-border);
    border-radius: 10px;
    padding: 12px;
    background: color-mix(in srgb, var(--qt-card-bg) 95%, #64748b 5%);
  }

  .empty-tip {
    color: var(--qt-text-muted);
    font-size: 13px;
    padding: 8px 4px;
  }

  .msg-item {
    border: 1px solid var(--qt-border);
    border-radius: 10px;
    padding: 12px;
    margin-bottom: 12px;
    background: var(--qt-card-bg);

    &.user {
      border-color: color-mix(in srgb, #3b82f6 35%, var(--qt-border) 65%);
    }

    &.assistant {
      border-color: color-mix(in srgb, #64748b 20%, var(--qt-border) 80%);
    }

    &.error {
      border-color: color-mix(in srgb, #ef4444 45%, var(--qt-border) 55%);
      background: color-mix(in srgb, var(--qt-card-bg) 90%, #ef4444 10%);
    }
  }

  .msg-meta {
    display: flex;
    gap: 8px;
    align-items: center;
    flex-wrap: wrap;
    font-size: 12px;
    color: var(--qt-text-muted);
    margin-bottom: 8px;

    .role {
      font-weight: 700;
      color: var(--qt-text-primary);
    }

    .model {
      color: #2563eb;
    }

    .render-toggle {
      padding: 0;
      font-size: 12px;
      margin-left: auto;
    }
  }

  .market-context {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    margin-bottom: 8px;
  }

  .market-chip {
    display: inline-flex;
    align-items: center;
    padding: 2px 8px;
    border-radius: 999px;
    font-size: 12px;
    color: var(--qt-text-secondary);
    border: 1px solid color-mix(in srgb, var(--qt-border) 88%, #94a3b8 12%);
    background: color-mix(in srgb, var(--qt-card-bg) 94%, #64748b 6%);

    &.source {
      color: #334155;
      border-color: color-mix(in srgb, #64748b 32%, var(--qt-border) 68%);
    }

    &.symbol {
      color: #1d4ed8;
      border-color: color-mix(in srgb, #3b82f6 45%, var(--qt-border) 55%);
    }

    &.freshness.realtime {
      color: #0f766e;
      border-color: color-mix(in srgb, #14b8a6 48%, var(--qt-border) 52%);
      background: color-mix(in srgb, #14b8a6 16%, var(--qt-card-bg) 84%);
    }

    &.freshness.delayed_close {
      color: #92400e;
      border-color: color-mix(in srgb, #f59e0b 50%, var(--qt-border) 50%);
      background: color-mix(in srgb, #f59e0b 16%, var(--qt-card-bg) 84%);
    }

    &.freshness.stale {
      color: #991b1b;
      border-color: color-mix(in srgb, #ef4444 55%, var(--qt-border) 45%);
      background: color-mix(in srgb, #ef4444 14%, var(--qt-card-bg) 86%);
    }
  }

  .msg-content {
    margin: 0;
    font-size: 14px;
    line-height: 1.75;
    color: var(--qt-text-primary);
    overflow-wrap: anywhere;
    word-break: break-word;
  }

  .user-content {
    white-space: pre-wrap;
  }

  .raw-content {
    white-space: pre-wrap;
    font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
    background: color-mix(in srgb, var(--qt-card-bg) 87%, #0f172a 13%);
    border-radius: 8px;
    border: 1px solid var(--qt-border);
    padding: 10px;
  }

  .markdown-content {
    :deep(h1),
    :deep(h2),
    :deep(h3),
    :deep(h4) {
      margin: 12px 0 8px;
      line-height: 1.45;
      color: var(--qt-text-primary);
    }

    :deep(h1) {
      font-size: 20px;
    }

    :deep(h2) {
      font-size: 17px;
    }

    :deep(h3),
    :deep(h4) {
      font-size: 15px;
    }

    :deep(p) {
      margin: 0 0 9px;
    }

    :deep(ul),
    :deep(ol) {
      margin: 0 0 10px 18px;
      padding: 0;
    }

    :deep(li) {
      margin-bottom: 4px;
    }

    :deep(a) {
      color: #2563eb;
      text-decoration: none;
      border-bottom: 1px dashed color-mix(in srgb, #2563eb 60%, transparent 40%);
    }

    :deep(blockquote) {
      margin: 0 0 10px;
      padding: 8px 10px;
      border-left: 3px solid #3b82f6;
      background: color-mix(in srgb, var(--qt-card-bg) 88%, #3b82f6 12%);
      border-radius: 0 8px 8px 0;
    }

    :deep(pre) {
      margin: 8px 0 10px;
      padding: 10px;
      border-radius: 8px;
      border: 1px solid var(--qt-border);
      background: color-mix(in srgb, var(--qt-card-bg) 84%, #0f172a 16%);
      overflow-x: auto;
    }

    :deep(code) {
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
      background: color-mix(in srgb, var(--qt-card-bg) 86%, #0f172a 14%);
      border: 1px solid var(--qt-border);
      border-radius: 4px;
      padding: 1px 6px;
      font-size: 12px;
    }
  }

  .chat-input {
    border: 1px solid var(--qt-border);
    border-radius: 10px;
    padding: 12px;
    background: color-mix(in srgb, var(--qt-card-bg) 97%, #64748b 3%);
  }

  .input-actions {
    margin-top: 10px;
    display: flex;
    justify-content: flex-end;
    gap: 8px;
  }
}

@media (max-width: 1200px) {
  .ai-chat-page {
    .chat-shell {
      grid-template-columns: 220px minmax(0, 1fr);
    }
  }
}

@media (max-width: 960px) {
  .ai-chat-page {
    .chat-shell {
      grid-template-columns: 1fr;
      min-height: auto;
    }

    .session-panel {
      max-height: 220px;
    }

    .chat-messages {
      min-height: 300px;
      max-height: none;
    }
  }
}
</style>
