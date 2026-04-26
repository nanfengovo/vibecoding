<template>
  <div class="ai-chat-page">
    <div class="chat-shell">
      <aside class="session-panel">
        <div class="panel-title">
          <span>会话 ({{ sessions.length }})</span>
          <div class="title-actions">
            <el-tooltip content="新建会话" placement="top">
              <el-button link class="action-btn" @click="createSession">
                <el-icon><Plus /></el-icon>
              </el-button>
            </el-tooltip>
            <el-tooltip content="清空当前" placement="top">
              <el-button link class="action-btn" @click="clearCurrentSession">
                <el-icon><Delete /></el-icon>
              </el-button>
            </el-tooltip>
          </div>
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
              <el-button
                v-if="session.id === activeSessionId"
                text
                class="delete-btn"
                :icon="Delete"
                @click.stop="deleteSession(session.id)"
              />
            </div>
            <div class="session-meta">
              <span>{{ formatTime(session.updatedAt) }}</span>
              <span>{{ session.messages.length }} 条</span>
            </div>
          </div>
        </el-scrollbar>
      </aside>

      <section class="chat-main">
        <div class="chat-config glass-panel">
          <div class="inline-toolbar">
            <div class="toolbar-item">
              <span class="toolbar-label">模型源</span>
              <el-select v-model="selectedProviderId" class="glass-select" placeholder="选择模型源">
                <el-option v-for="provider in providers" :key="provider.id" :label="provider.name" :value="provider.id" />
              </el-select>
            </div>
            <div class="toolbar-item">
              <span class="toolbar-label">模型</span>
              <el-select v-model="selectedModel" class="glass-select" placeholder="选择模型">
                <el-option v-for="model in currentModels" :key="model" :label="model" :value="model" />
              </el-select>
            </div>
            <div class="toolbar-item">
              <span class="toolbar-label">技能</span>
              <el-select v-model="selectedSkillId" class="glass-select" placeholder="分析技能" clearable>
                <el-option v-for="skill in skillOptions" :key="skill.id" :label="skill.label" :value="skill.id" />
              </el-select>
            </div>
            <div class="toolbar-item">
              <span class="toolbar-label">标的</span>
              <el-input v-model="symbol" class="glass-input-sm" placeholder="NVDA" />
            </div>
          </div>
          <div class="quick-prompts">
            <el-button v-for="prompt in quickPrompts" :key="prompt" class="glass-tag-btn" @click="applyPrompt(prompt)">
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
            :class="['msg-wrapper', item.role]"
          >
            <div :class="['msg-bubble', { error: item.isError }]">
              <div class="msg-meta">
                <span class="time">{{ formatTime(item.time) }}</span>
                <span v-if="item.model" class="model">{{ item.model }}</span>
                <el-button
                  v-if="item.role === 'assistant'"
                  link
                  class="action-link"
                  :loading="isSavingMemory(item.id)"
                  @click="saveAssistantMemory(item)"
                >
                  <el-icon><Star /></el-icon>
                </el-button>
                <el-button
                  v-if="item.role === 'assistant'"
                  link
                  class="action-link"
                  @click="toggleRawMode(item.id)"
                >
                  <el-icon><Document /></el-icon>
                </el-button>
              </div>

              <div
                v-if="item.role === 'assistant' && item.marketContext"
                class="market-context"
              >
                <span class="market-chip source">{{ item.marketContext.source }}</span>
                <span class="market-chip symbol">{{ item.marketContext.symbol }}</span>
                <span class="market-chip">时间: {{ formatQuoteTime(item.marketContext.quoteTime) }}</span>
                <span class="market-chip">延时: {{ formatLag(item.marketContext.lagSeconds) }}</span>
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
          </div>

          <div v-if="sending" class="msg-wrapper assistant">
            <div class="msg-bubble typing-indicator">
              <span></span><span></span><span></span>
            </div>
          </div>
        </div>

        <div class="chat-input-wrapper">
          <div class="chat-input-glow">
            <el-input
              v-model="draftQuestion"
              type="textarea"
              :rows="3"
              class="glass-chat-textarea"
              placeholder="发送消息... (Enter 发送，Shift+Enter 换行)"
              @keydown.enter.prevent="handleEnter"
            />
            <div class="input-actions">
              <el-tooltip content="优化提示词" placement="top">
                <el-button
                  circle
                  class="optimize-btn"
                  :loading="optimizing"
                  @click="optimizeQuestion"
                >
                  <el-icon><MagicStick /></el-icon>
                </el-button>
              </el-tooltip>
              <el-button type="primary" class="send-btn" :loading="sending" @click="sendQuestion">
                <el-icon><Promotion /></el-icon>
              </el-button>
            </div>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import dayjs from 'dayjs'
import { Delete, Document, MagicStick, Plus, Promotion, Star } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { aiApi, configApi } from '@/api'
import type { AiChatMarketContext, AiProviderConfig } from '@/types'
import { useAiChatStore } from '@/stores/aiChat'
import type { AiChatMessage } from '@/stores/aiChat'
import { parseAiMarkdown } from '@/utils/aiMarkdown'
import {
  createAiProvider,
  normalizeAiProviders,
  parseModelCandidates
} from '@/lib/ai/providerModel'

const chatStore = useAiChatStore()
const providers = ref<AiProviderConfig[]>([])
const defaultProviderId = ref('')
const rawModeMap = ref<Record<string, boolean>>({})
const savingMemoryMap = ref<Record<string, boolean>>({})
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
    const rows = normalizeAiProviders(config?.openAi)
    providers.value = rows
    const preferred = String(config?.openAi?.activeProviderId || '').trim()
    defaultProviderId.value = rows.some((item) => item.id === preferred) ? preferred : (rows[0]?.id || '')
    ensureSessionDefaults()
  } catch {
    providers.value = [createAiProvider(undefined, 0)]
    defaultProviderId.value = providers.value[0].id
    ensureSessionDefaults()
  }
}

async function createSession() {
  await chatStore.createNewSession()
  ensureSessionDefaults()
}

async function switchSession(sessionId: string) {
  await chatStore.switchSession(sessionId)
  ensureSessionDefaults()
}

async function deleteSession(sessionId: string) {
  await chatStore.removeSession(sessionId)
  ensureSessionDefaults()
}

async function clearCurrentSession() {
  const id = activeSessionId.value
  if (id) {
    await chatStore.removeSession(id)
  } else {
    chatStore.clearActiveSessionMessages()
  }
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

function isSavingMemory(messageId: string) {
  return Boolean(savingMemoryMap.value[messageId])
}

async function saveAssistantMemory(item: AiChatMessage) {
  if (item.role !== 'assistant' || !String(item.content || '').trim()) {
    ElMessage.warning('当前消息没有可保存内容')
    return
  }

  const messageId = String(item.id || '')
  if (isSavingMemory(messageId)) {
    return
  }

  savingMemoryMap.value = {
    ...savingMemoryMap.value,
    [messageId]: true
  }

  try {
    await aiApi.createMemory({
      type: 'ai_chat',
      title: `AI Chat：${activeSession.value?.title || '会话消息'}`,
      content: String(item.content || '').trim(),
      symbol: symbol.value || undefined,
      tags: 'ai_chat',
      priority: 2,
      sourceType: 'ai_chat',
      sourceRef: `session:${activeSessionId.value || ''}:message:${messageId}`,
      providerId: selectedProviderId.value || undefined,
      model: item.model || selectedModel.value || undefined
    })
    ElMessage.success('已保存为记忆')
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || '保存记忆失败')
  } finally {
    savingMemoryMap.value = {
      ...savingMemoryMap.value,
      [messageId]: false
    }
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

onMounted(async () => {
  await chatStore.loadSessions()
  await loadProviders()
})
</script>

<style lang="scss" scoped>
.ai-chat-page {
  height: calc(100vh - 60px);
  padding: 16px;
  box-sizing: border-box;

  .chat-shell {
    display: flex;
    height: 100%;
    gap: 16px;
  }

  .session-panel {
    width: 260px;
    background: transparent;
    display: flex;
    flex-direction: column;
    border-right: 1px solid var(--qt-border);
    padding-right: 16px;
  }

  .panel-title {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 12px 12px 8px;
    font-size: 13px;
    font-weight: 600;
    color: var(--qt-text-secondary);
    text-transform: uppercase;
    letter-spacing: 0.5px;
    border-bottom: 1px solid rgba(255,255,255,0.05);
    margin-bottom: 8px;
    
    .title-actions {
      display: flex;
      gap: 4px;
    }

    .action-btn {
      padding: 4px;
      height: auto;
      color: var(--qt-text-muted);
      
      &:hover { color: #3b82f6; }
    }
  }

  .session-scroll {
    flex: 1;
  }

  .session-item {
    padding: 10px 12px;
    border-radius: 8px;
    margin-bottom: 4px;
    cursor: pointer;
    transition: all 0.2s ease;
    border: 1px solid transparent;

    &:hover {
      background: color-mix(in srgb, var(--qt-card-bg) 80%, rgba(255,255,255,0.05) 20%);
    }

    &.active {
      background: color-mix(in srgb, #3b82f6 15%, transparent 85%);
      border-color: color-mix(in srgb, #3b82f6 30%, transparent 70%);
      
      .session-name {
        color: #fff;
        font-weight: 500;
      }
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
    color: var(--qt-text-secondary);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    flex: 1;
    transition: color 0.2s;
  }

  .delete-btn {
    color: var(--qt-text-muted);
    padding: 4px;
    height: auto;
    &:hover { color: #ef4444; }
  }

  .session-meta {
    margin-top: 4px;
    font-size: 11px;
    color: var(--qt-text-muted);
  }

  /* Chat Main */
  .chat-main {
    flex: 1;
    display: flex;
    flex-direction: column;
    min-width: 0;
    position: relative;
  }

  .chat-config {
    margin-bottom: 16px;
    padding: 12px 16px;
    border-radius: 12px;
    background: var(--qt-surface-glass);
    border: 1px solid var(--qt-border);
    backdrop-filter: blur(12px);

    .inline-toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      margin-bottom: 12px;
    }

    .toolbar-item {
      display: flex;
      align-items: center;
      gap: 8px;

      .toolbar-label {
        font-size: 12px;
        color: var(--qt-text-secondary);
        font-weight: 500;
      }
    }

    .glass-select, .glass-input-sm {
      :deep(.el-input__wrapper) {
        background: rgba(0, 0, 0, 0.2);
        box-shadow: 0 0 0 1px var(--qt-border);
        border-radius: 6px;
        
        &:hover, &.is-focus {
          box-shadow: 0 0 0 1px #3b82f6;
        }
      }
      :deep(input) {
        color: var(--qt-text);
        font-size: 13px;
      }
    }

    .glass-select { width: 140px; }
    .glass-input-sm { width: 100px; }

    .quick-prompts {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }

    .glass-tag-btn {
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid var(--qt-border);
      color: var(--qt-text-secondary);
      border-radius: 6px;
      padding: 4px 12px;
      height: auto;
      font-size: 12px;
      transition: all 0.2s;
      
      &:hover {
        background: rgba(255, 255, 255, 0.1);
        color: var(--qt-text);
        border-color: color-mix(in srgb, var(--qt-border) 50%, #fff 50%);
      }
    }
  }

  .chat-messages {
    flex: 1;
    overflow-y: auto;
    padding-right: 12px;
    margin-bottom: 24px;
    
    /* Scrollbar minimal */
    &::-webkit-scrollbar { width: 6px; }
    &::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.1); border-radius: 3px; }
  }

  .empty-tip {
    text-align: center;
    margin-top: 100px;
    color: var(--qt-text-muted);
    font-size: 14px;
  }

  /* Chat Bubbles (Linear/Cursor style) */
  .msg-wrapper {
    display: flex;
    margin-bottom: 24px;
    
    &.user {
      justify-content: flex-end;
      .msg-bubble {
        background: color-mix(in srgb, #3b82f6 20%, transparent 80%);
        border: 1px solid color-mix(in srgb, #3b82f6 30%, transparent 70%);
        color: #e2e8f0;
        border-radius: 16px 16px 4px 16px;
        max-width: 75%;
      }
    }
    
    &.assistant {
      justify-content: flex-start;
      .msg-bubble {
        background: transparent;
        color: var(--qt-text);
        max-width: 90%;
      }
    }
  }

  .msg-bubble {
    padding: 12px 16px;
    font-size: 14px;
    line-height: 1.6;
    position: relative;

    &.error {
      background: rgba(239, 68, 68, 0.1);
      border: 1px solid rgba(239, 68, 68, 0.3);
      border-radius: 12px;
    }
  }

  .msg-meta {
    display: flex;
    align-items: center;
    gap: 12px;
    margin-bottom: 8px;
    font-size: 11px;
    color: var(--qt-text-muted);

    .model {
      color: #3b82f6;
      font-weight: 500;
    }

    .action-link {
      padding: 0;
      height: auto;
      color: var(--qt-text-muted);
      &:hover { color: #3b82f6; }
    }
  }

  .market-context {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    margin-bottom: 12px;
  }

  .market-chip {
    padding: 2px 8px;
    border-radius: 4px;
    font-size: 11px;
    background: rgba(255, 255, 255, 0.05);
    border: 1px solid var(--qt-border);
    color: var(--qt-text-secondary);

    &.symbol { color: #60a5fa; border-color: rgba(96, 165, 250, 0.3); }
    &.freshness.realtime { color: #34d399; }
    &.freshness.stale { color: #f87171; }
  }

  .msg-content {
    word-break: break-word;
    
    &.raw-content {
      font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
      font-size: 13px;
      background: rgba(0,0,0,0.3);
      padding: 12px;
      border-radius: 8px;
      white-space: pre-wrap;
    }

    &.user-content {
      white-space: pre-wrap;
    }
  }

  /* Typing Indicator */
  .typing-indicator {
    display: flex;
    gap: 4px;
    align-items: center;
    padding: 12px 16px;
    
    span {
      display: block;
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: var(--qt-text-muted);
      animation: typing 1.4s infinite ease-in-out both;
      
      &:nth-child(1) { animation-delay: -0.32s; }
      &:nth-child(2) { animation-delay: -0.16s; }
    }
  }

  @keyframes typing {
    0%, 80%, 100% { transform: scale(0); }
    40% { transform: scale(1); }
  }

  /* Floating Chat Input */
  .chat-input-wrapper {
    position: relative;
    padding: 0 10%;
  }

  .chat-input-glow {
    position: relative;
    background: var(--qt-surface-glass);
    border: 1px solid color-mix(in srgb, #3b82f6 40%, var(--qt-border) 60%);
    border-radius: 16px;
    backdrop-filter: blur(20px);
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3), 0 0 0 1px inset rgba(255,255,255,0.05);
    transition: box-shadow 0.3s ease;
    
    &:focus-within {
      box-shadow: 0 8px 32px rgba(59, 130, 246, 0.15), 0 0 0 1px inset rgba(59, 130, 246, 0.2);
    }

    .glass-chat-textarea {
      :deep(.el-textarea__inner) {
        background: transparent;
        box-shadow: none;
        border: none;
        color: var(--qt-text);
        font-size: 14px;
        padding: 16px 60px 16px 20px;
        resize: none;
        
        &::placeholder {
          color: var(--qt-text-muted);
        }
      }
    }

    .input-actions {
      position: absolute;
      right: 12px;
      bottom: 12px;
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .optimize-btn {
      background: transparent;
      border: none;
      color: var(--qt-text-secondary);
      &:hover { color: var(--qt-text); background: rgba(255,255,255,0.1); }
    }

    .send-btn {
      width: 32px;
      height: 32px;
      padding: 0;
      border-radius: 50%;
      background: #3b82f6;
      border: none;
      box-shadow: 0 4px 12px rgba(59, 130, 246, 0.3);
      transition: all 0.2s;
      
      &:hover {
        transform: translateY(-1px);
        box-shadow: 0 6px 16px rgba(59, 130, 246, 0.4);
      }
      
      .el-icon {
        font-size: 16px;
      }
    }
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
