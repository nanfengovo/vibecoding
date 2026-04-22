<template>
  <div class="ai-chat-page">
    <div class="page-header">
      <h1>AI Chat</h1>
      <div class="header-actions">
        <el-button @click="clearMessages">清空会话</el-button>
      </div>
    </div>

    <div class="card chat-config">
      <el-form label-width="90px" inline>
        <el-form-item label="模型源">
          <el-select v-model="selectedProviderId" placeholder="选择模型源" style="width: 220px">
            <el-option
              v-for="provider in providers"
              :key="provider.id"
              :label="provider.name"
              :value="provider.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="模型">
          <el-select v-model="selectedModel" placeholder="选择模型" style="width: 260px">
            <el-option
              v-for="model in currentModels"
              :key="model"
              :label="model"
              :value="model"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="标的">
          <el-input v-model="symbol" placeholder="可选，如 NVDA / 600519.SH" style="width: 220px" />
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

    <div class="card chat-messages">
      <div v-if="messages.length === 0" class="empty-tip">
        你可以问：某只股票趋势、交易计划、风控、仓位管理、事件驱动影响等。
      </div>
      <div v-for="item in messages" :key="item.id" :class="['msg-item', item.role]">
        <div class="msg-meta">
          <span class="role">{{ item.role === 'user' ? '你' : 'AI' }}</span>
          <span class="time">{{ formatTime(item.time) }}</span>
          <span v-if="item.model" class="model">{{ item.model }}</span>
        </div>
        <pre class="msg-content">{{ item.content }}</pre>
      </div>
      <el-skeleton v-if="loading" :rows="4" animated />
    </div>

    <div class="card chat-input">
      <el-input
        v-model="question"
        type="textarea"
        :rows="4"
        placeholder="输入股票或交易相关问题，回车发送（Shift+回车换行）"
        @keydown.enter.prevent="handleEnter"
      />
      <div class="input-actions">
        <el-button type="primary" :loading="loading" @click="sendQuestion">发送</el-button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import dayjs from 'dayjs'
import { ElMessage } from 'element-plus'
import { aiApi, configApi } from '@/api'
import type { AiProviderConfig, SystemConfig } from '@/types'

type ChatMessage = {
  id: string
  role: 'user' | 'assistant'
  content: string
  time: string
  model?: string
}

const providers = ref<AiProviderConfig[]>([])
const selectedProviderId = ref('')
const selectedModel = ref('')
const symbol = ref('')
const question = ref('')
const loading = ref(false)
const messages = ref<ChatMessage[]>([])

const quickPrompts = [
  '帮我看下这只股票短中期趋势',
  '给一个保守型交易计划（含止损）',
  '这只股票有哪些关键风险点',
  '如果我只做波段，仓位怎么分配'
]

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

const currentProvider = computed(() => {
  return providers.value.find((item) => item.id === selectedProviderId.value) || providers.value[0] || null
})

const currentModels = computed(() => {
  const raw = currentProvider.value?.model || ''
  const list = parseModelCandidates(raw)
  return list.length > 0 ? list : ['gpt-5-mini']
})

watch(currentProvider, () => {
  if (!currentModels.value.includes(selectedModel.value)) {
    selectedModel.value = currentModels.value[0] || ''
  }
})

async function loadProviders() {
  try {
    const config = await configApi.get()
    const rows = normalizeProviders(config?.openAi)
    providers.value = rows
    const preferred = String(config?.openAi?.activeProviderId || '').trim()
    selectedProviderId.value = rows.some((item) => item.id === preferred) ? preferred : rows[0]?.id || ''
    selectedModel.value = currentModels.value[0] || ''
  } catch {
    providers.value = [createProvider(undefined, 0)]
    selectedProviderId.value = providers.value[0].id
    selectedModel.value = parseModelCandidates(providers.value[0].model)[0] || 'gpt-5-mini'
  }
}

function clearMessages() {
  messages.value = []
}

function applyPrompt(value: string) {
  question.value = value
}

function formatTime(value: string) {
  return dayjs(value).format('MM-DD HH:mm:ss')
}

function handleEnter(event: Event | KeyboardEvent) {
  if (!(event instanceof KeyboardEvent)) {
    return
  }

  if (event.shiftKey) {
    question.value += '\n'
    return
  }
  void sendQuestion()
}

async function sendQuestion() {
  const text = String(question.value || '').trim()
  if (!text || loading.value) {
    return
  }

  const userMessage: ChatMessage = {
    id: `u-${Date.now()}`,
    role: 'user',
    content: text,
    time: new Date().toISOString()
  }
  messages.value.push(userMessage)
  question.value = ''
  loading.value = true

  try {
    const result = await aiApi.chat({
      question: text,
      symbol: String(symbol.value || '').trim() || undefined,
      providerId: selectedProviderId.value || undefined,
      model: selectedModel.value || undefined
    })

    messages.value.push({
      id: `a-${Date.now()}`,
      role: 'assistant',
      content: result.content,
      time: result.generatedAt,
      model: result.model
    })
  } catch (error: any) {
    const message = error?.response?.data?.message || error?.message || 'AI 调用失败'
    ElMessage.error(message)
    messages.value.push({
      id: `e-${Date.now()}`,
      role: 'assistant',
      content: `调用失败：${message}`,
      time: new Date().toISOString()
    })
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  loadProviders()
})
</script>

<style lang="scss" scoped>
.ai-chat-page {
  .chat-config {
    margin-bottom: 16px;
  }

  .quick-prompts {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    margin-top: 4px;

    .label {
      font-size: 12px;
      color: var(--qt-text-muted);
      margin-right: 2px;
      line-height: 28px;
    }
  }

  .chat-messages {
    min-height: 320px;
    max-height: 560px;
    overflow-y: auto;
    margin-bottom: 16px;
  }

  .empty-tip {
    color: var(--qt-text-muted);
    font-size: 13px;
  }

  .msg-item {
    border: 1px solid var(--qt-border);
    border-radius: 8px;
    padding: 10px 12px;
    margin-bottom: 10px;
    background: var(--qt-card-bg);

    &.user {
      border-color: color-mix(in srgb, #3b82f6 35%, var(--qt-border) 65%);
    }
  }

  .msg-meta {
    display: flex;
    gap: 8px;
    align-items: center;
    font-size: 12px;
    color: var(--qt-text-muted);
    margin-bottom: 6px;

    .role {
      font-weight: 600;
      color: var(--qt-text-primary);
    }

    .model {
      color: #2563eb;
    }
  }

  .msg-content {
    margin: 0;
    white-space: pre-wrap;
    word-break: break-word;
    font-size: 14px;
    line-height: 1.65;
    color: var(--qt-text-primary);
    font-family: inherit;
  }

  .input-actions {
    margin-top: 10px;
    display: flex;
    justify-content: flex-end;
  }
}
</style>
