import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { aiApi } from '@/api'

export interface AiChatMessage {
  id: string
  role: 'user' | 'assistant'
  content: string
  time: string
  model?: string
  isError?: boolean
}

export interface AiChatSession {
  id: string
  title: string
  createdAt: string
  updatedAt: string
  symbol: string
  providerId: string
  model: string
  messages: AiChatMessage[]
}

type PersistedAiChatState = {
  sessions: AiChatSession[]
  activeSessionId: string
  draftQuestion: string
}

const STORAGE_KEY = 'qt-ai-chat-state-v2'
const DEFAULT_SESSION_TITLE = '新会话'

function createSessionId() {
  return `session-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
}

function createMessageId(prefix: 'u' | 'a') {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
}

function normalizeTitle(input: string) {
  const clean = String(input || '').replace(/\s+/g, ' ').trim()
  if (!clean) {
    return DEFAULT_SESSION_TITLE
  }

  if (clean.length <= 26) {
    return clean
  }

  return `${clean.slice(0, 26)}...`
}

function createSession(seed?: Partial<AiChatSession>): AiChatSession {
  const now = new Date().toISOString()
  return {
    id: String(seed?.id || createSessionId()),
    title: normalizeTitle(seed?.title || DEFAULT_SESSION_TITLE),
    createdAt: seed?.createdAt || now,
    updatedAt: seed?.updatedAt || now,
    symbol: String(seed?.symbol || ''),
    providerId: String(seed?.providerId || ''),
    model: String(seed?.model || ''),
    messages: Array.isArray(seed?.messages) ? seed!.messages : []
  }
}

function parsePersistedState(raw: string | null): PersistedAiChatState | null {
  if (!raw) {
    return null
  }

  try {
    const parsed = JSON.parse(raw)
    if (!parsed || typeof parsed !== 'object') {
      return null
    }

    const sessions = Array.isArray(parsed.sessions)
      ? parsed.sessions.map((item: any) => createSession({
        ...item,
        messages: Array.isArray(item?.messages)
          ? item.messages.map((row: any) => ({
            id: String(row?.id || createMessageId(row?.role === 'assistant' ? 'a' : 'u')),
            role: row?.role === 'assistant' ? 'assistant' : 'user',
            content: String(row?.content || ''),
            time: String(row?.time || new Date().toISOString()),
            model: row?.model ? String(row.model) : undefined,
            isError: Boolean(row?.isError)
          }))
          : []
      }))
      : []

    return {
      sessions,
      activeSessionId: String(parsed.activeSessionId || ''),
      draftQuestion: String(parsed.draftQuestion || '')
    }
  } catch {
    return null
  }
}

export const useAiChatStore = defineStore('ai-chat', () => {
  const hydrated = ref(false)
  const sessions = ref<AiChatSession[]>([])
  const activeSessionId = ref('')
  const draftQuestion = ref('')
  const sending = ref(false)
  const optimizing = ref(false)

  const activeSession = computed(() => {
    return sessions.value.find((item) => item.id === activeSessionId.value) || sessions.value[0] || null
  })

  function persist() {
    if (typeof localStorage === 'undefined') {
      return
    }

    const payload: PersistedAiChatState = {
      sessions: sessions.value,
      activeSessionId: activeSessionId.value,
      draftQuestion: draftQuestion.value
    }

    localStorage.setItem(STORAGE_KEY, JSON.stringify(payload))
  }

  function ensureHydrated() {
    if (hydrated.value) {
      return
    }

    const parsed = parsePersistedState(typeof localStorage === 'undefined' ? null : localStorage.getItem(STORAGE_KEY))
    if (parsed?.sessions?.length) {
      sessions.value = parsed.sessions
      activeSessionId.value = parsed.activeSessionId
      draftQuestion.value = parsed.draftQuestion
    }

    if (!sessions.value.length) {
      const session = createSession()
      sessions.value = [session]
      activeSessionId.value = session.id
    } else if (!sessions.value.some((item) => item.id === activeSessionId.value)) {
      activeSessionId.value = sessions.value[0].id
    }

    hydrated.value = true
    persist()
  }

  function touchSession(session: AiChatSession) {
    session.updatedAt = new Date().toISOString()
  }

  function updateSessionMeta(payload: { providerId?: string; model?: string; symbol?: string }) {
    ensureHydrated()
    const session = activeSession.value
    if (!session) {
      return
    }

    let changed = false
    if (typeof payload.providerId === 'string') {
      const next = payload.providerId
      if (session.providerId !== next) {
        session.providerId = next
        changed = true
      }
    }
    if (typeof payload.model === 'string') {
      const next = payload.model
      if (session.model !== next) {
        session.model = next
        changed = true
      }
    }
    if (typeof payload.symbol === 'string') {
      const next = payload.symbol
      if (session.symbol !== next) {
        session.symbol = next
        changed = true
      }
    }

    if (!changed) {
      return
    }

    touchSession(session)
    persist()
  }

  function setDraftQuestion(value: string) {
    ensureHydrated()
    draftQuestion.value = value
    persist()
  }

  function createNewSession() {
    ensureHydrated()
    const session = createSession()
    sessions.value.unshift(session)
    activeSessionId.value = session.id
    draftQuestion.value = ''
    persist()
  }

  function switchSession(sessionId: string) {
    ensureHydrated()
    const exists = sessions.value.some((item) => item.id === sessionId)
    if (!exists) {
      return
    }
    activeSessionId.value = sessionId
    draftQuestion.value = ''
    persist()
  }

  function renameSession(sessionId: string, title: string) {
    ensureHydrated()
    const target = sessions.value.find((item) => item.id === sessionId)
    if (!target) {
      return
    }

    target.title = normalizeTitle(title)
    touchSession(target)
    persist()
  }

  function removeSession(sessionId: string) {
    ensureHydrated()
    if (sessions.value.length <= 1) {
      const only = sessions.value[0]
      if (only) {
        only.messages = []
        only.title = DEFAULT_SESSION_TITLE
        only.symbol = ''
        only.model = ''
        only.providerId = ''
        touchSession(only)
        draftQuestion.value = ''
        persist()
      }
      return
    }

    sessions.value = sessions.value.filter((item) => item.id !== sessionId)
    if (!sessions.value.some((item) => item.id === activeSessionId.value)) {
      activeSessionId.value = sessions.value[0]?.id || ''
      draftQuestion.value = ''
    }
    persist()
  }

  function clearActiveSessionMessages() {
    ensureHydrated()
    const session = activeSession.value
    if (!session) {
      return
    }

    session.messages = []
    session.title = DEFAULT_SESSION_TITLE
    touchSession(session)
    persist()
  }

  function upsertMessages(sessionId: string, nextMessages: AiChatMessage[]) {
    const target = sessions.value.find((item) => item.id === sessionId)
    if (!target) {
      return
    }

    target.messages = nextMessages
    touchSession(target)
    sessions.value = [...sessions.value].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))
    persist()
  }

  async function sendMessage(payload: {
    question: string
    providerId?: string
    model?: string
    symbol?: string
  }) {
    ensureHydrated()
    if (sending.value) {
      return
    }

    const session = activeSession.value
    if (!session) {
      return
    }

    const question = String(payload.question || '').trim()
    if (!question) {
      return
    }

    const symbol = String(payload.symbol || '').trim()
    const providerId = String(payload.providerId || '').trim()
    const model = String(payload.model || '').trim()

    updateSessionMeta({
      symbol,
      providerId,
      model
    })

    const userMessage: AiChatMessage = {
      id: createMessageId('u'),
      role: 'user',
      content: question,
      time: new Date().toISOString()
    }

    const nextMessages = [...session.messages, userMessage]
    upsertMessages(session.id, nextMessages)
    if (session.title === DEFAULT_SESSION_TITLE && nextMessages.length <= 2) {
      renameSession(session.id, question)
    }

    draftQuestion.value = ''
    sending.value = true

    try {
      const result = await aiApi.chat({
        question,
        symbol: symbol || undefined,
        providerId: providerId || undefined,
        model: model || undefined
      })

      const refreshedSession = sessions.value.find((item) => item.id === session.id)
      if (!refreshedSession) {
        return
      }

      upsertMessages(session.id, [
        ...refreshedSession.messages,
        {
          id: createMessageId('a'),
          role: 'assistant',
          content: String(result?.content || ''),
          time: String(result?.generatedAt || new Date().toISOString()),
          model: String(result?.model || model || ''),
          isError: false
        }
      ])
    } catch (error: any) {
      const message = error?.response?.data?.message || error?.message || 'AI 调用失败'
      const refreshedSession = sessions.value.find((item) => item.id === session.id)
      if (!refreshedSession) {
        return
      }

      upsertMessages(session.id, [
        ...refreshedSession.messages,
        {
          id: createMessageId('a'),
          role: 'assistant',
          content: `调用失败：${message}`,
          time: new Date().toISOString(),
          model: model || undefined,
          isError: true
        }
      ])
    } finally {
      sending.value = false
    }
  }

  return {
    sessions,
    activeSessionId,
    activeSession,
    draftQuestion,
    sending,
    optimizing,
    ensureHydrated,
    setDraftQuestion,
    updateSessionMeta,
    createNewSession,
    switchSession,
    renameSession,
    removeSession,
    clearActiveSessionMessages,
    sendMessage
  }
})
