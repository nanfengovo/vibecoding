import axios from 'axios'
import type { AxiosInstance, AxiosResponse } from 'axios'
import type {
  AiChatResult,
  AiModelsResult,
  AiPromptOptimizeResult,
  CompanyProfile,
  Stock,
  StockQuote,
  Candlestick,
  Strategy,
  Trade,
  Backtest,
  MonitorRule,
  WatchlistItem,
  SystemConfig,
  ReviewRecord,
  PagedResult,
  StockAnalysisResult,
  AuthResponse,
  AuthUser,
  AiChatSessionSummary,
  AiChatSessionDetail,
  AiMemoryListResult,
  AiMemoryRecord,
  CrawlerSource,
  CrawlerJobRecord,
  CrawlerDocument,
  KnowledgeBase,
  KnowledgeDocument,
  ReaderBook,
  ReaderHighlight,
  ReaderProgress
} from '@/types'
import { announceDemoMode, demoApi, shouldUseDemoApi } from '@/api/demo'

const DEMO_MODE = shouldUseDemoApi()

if (DEMO_MODE) {
  announceDemoMode()
}

function resolveApiBaseUrl(): string {
  const rawValue = String(import.meta.env.VITE_API_BASE_URL || '').trim()
  if (rawValue) {
    const normalized = rawValue.replace(/\/+$/, '')
    return normalized.endsWith('/api') ? normalized : `${normalized}/api`
  }

  return '/api'
}

const api: AxiosInstance = axios.create({
  baseURL: resolveApiBaseUrl(),
  timeout: 90000
})

export const AUTH_TOKEN_KEY = 'qt-auth-token'
export const AUTH_USER_KEY = 'qt-auth-user'

api.interceptors.request.use((config) => {
  // Important: when sending FormData, JSON content-type forces Axios to serialize
  // the payload to `{}`. Remove it so browser can set multipart boundary.
  if (typeof FormData !== 'undefined' && config.data instanceof FormData) {
    const headers = config.headers as any
    if (headers) {
      if (typeof headers.delete === 'function') {
        headers.delete('Content-Type')
      } else {
        delete headers['Content-Type']
      }
    }
  }

  if (typeof localStorage !== 'undefined') {
    const token = localStorage.getItem(AUTH_TOKEN_KEY)
    if (token) {
      config.headers.Authorization = `Bearer ${token}`
    }
  }
  return config
})

function parseStrategyConfig(configSource: any) {
  const fallback = {
    conditions: [] as any[],
    actions: [] as any[],
    targetSymbols: [] as string[],
    checkInterval: 300
  }

  let raw = configSource
  if (typeof configSource === 'string') {
    try {
      raw = JSON.parse(configSource)
    } catch {
      raw = {}
    }
  }

  if (!raw || typeof raw !== 'object') {
    return fallback
  }

  const targetSymbols = Array.isArray(raw.targetSymbols)
    ? raw.targetSymbols
    : (Array.isArray(raw.symbols) ? raw.symbols : [])

  const conditions = Array.isArray(raw.conditions)
    ? raw.conditions
    : [
      ...(Array.isArray(raw.entryConditions) ? raw.entryConditions : []),
      ...(Array.isArray(raw.exitConditions) ? raw.exitConditions : [])
    ]

  return {
    conditions,
    actions: Array.isArray(raw.actions) ? raw.actions : [],
    targetSymbols,
    checkInterval: Number(raw.checkInterval ?? raw.checkIntervalSeconds ?? 300)
  }
}

function normalizeStrategy(raw: any): Strategy {
  const config = parseStrategyConfig(raw?.config ?? raw?.configJson)

  return {
    id: Number(raw?.id ?? 0),
    name: String(raw?.name ?? ''),
    description: String(raw?.description ?? ''),
    config,
    isActive: Boolean(raw?.isActive ?? raw?.isEnabled),
    createdAt: String(raw?.createdAt ?? new Date().toISOString()),
    updatedAt: String(raw?.updatedAt ?? new Date().toISOString()),
    lastExecutedAt: raw?.lastExecutedAt ? String(raw.lastExecutedAt) : undefined
  }
}

function normalizeStock(raw: any): Stock {
  const normalized = raw && typeof raw === 'object' ? raw : {}
  return {
    symbol: String(normalized?.symbol ?? '').trim().toUpperCase(),
    name: String(normalized?.name ?? normalized?.title ?? '').trim(),
    exchange: normalized?.exchange ? String(normalized.exchange) : undefined,
    market: normalized?.market ? String(normalized.market).toUpperCase() : undefined,
    currency: normalized?.currency ? String(normalized.currency).toUpperCase() : undefined,
    currentPrice: Number(normalized?.currentPrice ?? normalized?.current ?? normalized?.price ?? 0),
    previousClose: Number(normalized?.previousClose ?? normalized?.prevClose ?? 0),
    open: Number(normalized?.open ?? 0),
    high: Number(normalized?.high ?? 0),
    low: Number(normalized?.low ?? 0),
    change: Number(normalized?.change ?? 0),
    changePercent: Number(normalized?.changePercent ?? normalized?.change_rate ?? 0),
    volume: Number(normalized?.volume ?? 0),
    marketCap: Number(normalized?.marketCap ?? 0),
    high52Week: Number(normalized?.high52Week ?? 0),
    low52Week: Number(normalized?.low52Week ?? 0),
    avgVolume: Number(normalized?.avgVolume ?? 0),
    pe: Number(normalized?.pe ?? 0),
    eps: Number(normalized?.eps ?? 0),
    dividend: Number(normalized?.dividend ?? 0),
    updatedAt: String(normalized?.updatedAt ?? normalized?.lastUpdated ?? new Date().toISOString())
  }
}

function normalizeWatchlistRow(raw: any): WatchlistItem {
  const stock = normalizeStock(raw)
  const fallbackTime = stock.updatedAt || new Date().toISOString()
  return {
    id: Number(raw?.id ?? 0),
    symbol: stock.symbol,
    name: stock.name || stock.symbol.split('.')[0] || stock.symbol,
    notes: raw?.notes ? String(raw.notes) : '',
    addedAt: String(raw?.addedAt ?? raw?.createdAt ?? fallbackTime),
    stock
  }
}

// 响应拦截器
api.interceptors.response.use(
  (response: AxiosResponse) => response.data,
  (error) => {
    if (error.response?.status === 401 && typeof localStorage !== 'undefined') {
      localStorage.removeItem(AUTH_TOKEN_KEY)
      localStorage.removeItem(AUTH_USER_KEY)
      if (!window.location.pathname.includes('/login')) {
        window.location.href = '/login'
      }
    }
    const message = error.response?.data?.message || '请求失败'
    console.error('API Error:', message)
    return Promise.reject(error)
  }
)

export const authApi = {
  login: (payload: { username: string; password: string }) =>
    api.post<any, AuthResponse>('/auth/login', payload),
  me: () => api.get<any, AuthUser>('/auth/me'),
  listUsers: () => api.get<any, AuthUser[]>('/users'),
  createUser: (payload: { username: string; displayName?: string; password: string; role?: string }) =>
    api.post<any, AuthUser>('/users', payload)
}

// 股票API
export const stockApi = {
  search: (query: string) =>
    DEMO_MODE
      ? demoApi.stockApi.search(query)
      : api.get<any, any[]>('/stocks/search', { params: { keyword: query } })
        .then((rows) => (rows || []).map((item) => normalizeStock(item))),
  
  getQuote: (symbol: string) =>
    DEMO_MODE
      ? demoApi.stockApi.getQuote(symbol)
      : api.get<any, StockQuote>(`/stocks/${symbol}/quote`),
  
  getQuotes: (symbols: string[]) =>
    DEMO_MODE
      ? demoApi.stockApi.getQuotes(symbols)
      : api.post<any, StockQuote[]>('/stocks/quotes', { symbols }),
  
  getKline: (
    symbol: string,
    period: string = '1d',
    limit: number = 100,
    options?: { start?: string; end?: string }
  ) =>
    DEMO_MODE
      ? demoApi.stockApi.getKline(symbol, period, limit, options)
      : api.get<any, Candlestick[]>(`/stocks/${symbol}/kline`, {
        params: {
          period,
          count: limit,
          start: options?.start,
          end: options?.end
        }
      }),
  
  getDetail: (symbol: string) =>
    DEMO_MODE
      ? demoApi.stockApi.getDetail(symbol)
      : api.get<any, any>(`/stocks/${symbol}`)
        .then((row) => normalizeStock(row)),

  getCompanyProfile: (symbol: string) =>
    DEMO_MODE
      ? demoApi.stockApi.getCompanyProfile(symbol)
      : api.get<any, CompanyProfile>(`/stocks/${symbol}/profile`)
}

// 策略API
export const strategyApi = {
  list: async () => {
    if (DEMO_MODE) {
      return demoApi.strategyApi.list()
    }

    const rows = await api.get<any, any[]>('/strategies')
    return (rows || []).map(normalizeStrategy)
  },
  
  get: async (id: number) => {
    if (DEMO_MODE) {
      return demoApi.strategyApi.get(id)
    }

    const row = await api.get<any, any>(`/strategies/${id}`)
    return normalizeStrategy(row)
  },
  
  create: async (data: Partial<Strategy>) => {
    if (DEMO_MODE) {
      return demoApi.strategyApi.create(data)
    }

    const row = await api.post<any, any>('/strategies', data)
    return normalizeStrategy(row)
  },
  
  update: async (id: number, data: Partial<Strategy>) => {
    if (DEMO_MODE) {
      return demoApi.strategyApi.update(id, data)
    }

    const row = await api.put<any, any>(`/strategies/${id}`, data)
    return normalizeStrategy(row)
  },
  
  delete: (id: number) =>
    DEMO_MODE
      ? demoApi.strategyApi.delete(id)
      : api.delete(`/strategies/${id}`),
  
  toggle: (id: number, isActive: boolean) =>
    DEMO_MODE
      ? demoApi.strategyApi.toggle(id, isActive)
      : api.post(`/strategies/${id}/toggle`, { isActive }),
  
  execute: (id: number) =>
    DEMO_MODE
      ? demoApi.strategyApi.execute(id)
      : api.post(`/strategies/${id}/execute`),
  
  reload: (id: number) =>
    DEMO_MODE
      ? demoApi.strategyApi.reload(id)
      : api.post(`/strategies/${id}/reload`)
}

// 交易API
export const tradeApi = {
  list: async (params?: {
    page?: number
    pageSize?: number
    symbol?: string
    side?: string
    status?: string
    startDate?: string
    endDate?: string
  }): Promise<PagedResult<Trade>> => {
    if (DEMO_MODE) {
      return demoApi.tradeApi.list(params)
    }

    const response = await api.get<any, any>('/trades', {
      params: {
        startDate: params?.startDate,
        endDate: params?.endDate,
        limit: 500
      }
    })

    const rows: any[] = Array.isArray(response)
      ? response
      : (response?.items ?? [])

    const toNumber = (value: unknown, fallback = 0): number => {
      const numeric = Number(value)
      return Number.isFinite(numeric) ? numeric : fallback
    }

    const normalizeStatus = (status: unknown): Trade['status'] => {
      const normalized = String(status ?? '').toLowerCase()
      if (normalized === 'filled') {
        return 'filled'
      }

      if (normalized === 'cancelled' || normalized === 'canceled') {
        return 'cancelled'
      }

      if (normalized === 'rejected') {
        return 'rejected'
      }

      return 'pending'
    }

    const normalizedRows: Trade[] = rows.map((row: any) => {
      const symbol = String(row?.symbol ?? '').toUpperCase()
      const quantity = toNumber(row?.filledQuantity ?? row?.quantity, 0)
      const price = toNumber(row?.filledPrice ?? row?.price, 0)
      const amount = toNumber(row?.amount, quantity * price)
      const commission = toNumber(row?.commission, 0)

      return {
        id: toNumber(row?.id),
        symbol,
        stockName: String(row?.stockName ?? symbol),
        side: String(row?.side ?? 'buy').toLowerCase() === 'sell' ? 'sell' : 'buy',
        quantity,
        price,
        amount,
        commission,
        status: normalizeStatus(row?.status),
        orderId: row?.orderId,
        strategyId: Number.isFinite(Number(row?.strategyId)) ? Number(row?.strategyId) : undefined,
        strategyName: row?.strategyName ?? row?.strategy?.name,
        executedAt: String(row?.filledAt ?? row?.executedAt ?? row?.createdAt ?? new Date().toISOString()),
        createdAt: String(row?.createdAt ?? row?.filledAt ?? row?.executedAt ?? new Date().toISOString())
      }
    })

    const filtered = normalizedRows.filter((row) => {
      if (params?.symbol) {
        const keyword = params.symbol.toLowerCase()
        const symbolMatched = row.symbol.toLowerCase().includes(keyword)
        const nameMatched = row.stockName.toLowerCase().includes(keyword)
        if (!symbolMatched && !nameMatched) {
          return false
        }
      }

      if (params?.side && row.side !== params.side) {
        return false
      }

      if (params?.status && row.status !== params.status) {
        return false
      }

      if (params?.startDate) {
        const rowTime = Date.parse(row.executedAt)
        const start = Date.parse(params.startDate)
        if (Number.isFinite(rowTime) && Number.isFinite(start) && rowTime < start) {
          return false
        }
      }

      if (params?.endDate) {
        const rowTime = Date.parse(row.executedAt)
        const end = Date.parse(params.endDate)
        if (Number.isFinite(rowTime) && Number.isFinite(end) && rowTime > end) {
          return false
        }
      }

      if (!params?.startDate && !params?.endDate) {
        return true
      }

      const rowTime = Date.parse(row.executedAt)
      if (!Number.isFinite(rowTime)) {
        return false
      }

      return true
    })

    const page = params?.page ?? 1
    const pageSize = params?.pageSize ?? 20
    const start = (page - 1) * pageSize
    const items = filtered.slice(start, start + pageSize)

    return {
      items,
      total: filtered.length,
      page,
      pageSize,
      totalPages: Math.max(1, Math.ceil(filtered.length / pageSize))
    }
  },
  
  get: (id: number) =>
    DEMO_MODE
      ? demoApi.tradeApi.get(id)
      : api.get<any, Trade>(`/trades/${id}`),
  
  getByStrategy: (strategyId: number) =>
    DEMO_MODE
      ? demoApi.tradeApi.getByStrategy(strategyId)
      : api.get<any, Trade[]>(`/trades/strategy/${strategyId}`),

  getAccount: () =>
    DEMO_MODE
      ? demoApi.tradeApi.getAccount()
      : api.get('/trades/account'),

  getPositions: () =>
    DEMO_MODE
      ? demoApi.tradeApi.getPositions()
      : api.get('/trades/positions'),
  
  getStats: async (startDate?: string, endDate?: string) => {
    if (DEMO_MODE) {
      return demoApi.tradeApi.getStats(startDate, endDate)
    }

    const result = await tradeApi.list({ page: 1, pageSize: 1000, startDate, endDate })
    const rows = result.items || []
    const totalTrades = rows.length
    const totalVolume = rows.reduce((sum, row: any) => {
      const amount = Number(row?.amount ?? ((row?.filledQuantity ?? row?.quantity ?? 0) * (row?.filledPrice ?? row?.price ?? 0)))
      return sum + (Number.isFinite(amount) ? amount : 0)
    }, 0)

    const pnlRows = rows.filter((row: any) => typeof row?.pnl === 'number')
    const totalPnl = pnlRows.reduce((sum: number, row: any) => sum + Number(row.pnl || 0), 0)
    const winRate = pnlRows.length > 0
      ? pnlRows.filter((row: any) => Number(row.pnl) > 0).length / pnlRows.length
      : 0

    return {
      totalTrades,
      totalPnl,
      winRate,
      totalVolume
    }
  },

  placeOrder: (payload: {
    symbol: string
    side: 'buy' | 'sell'
    orderType: 'market' | 'limit'
    quantity: number
    price?: number
    strategyId?: number
  }) =>
    DEMO_MODE
      ? demoApi.tradeApi.placeOrder(payload)
      : api.post('/trades', payload),

  cancelOrder: (orderId: string) =>
    DEMO_MODE
      ? demoApi.tradeApi.cancelOrder(orderId)
      : api.delete(`/trades/${orderId}`)
}

// 回测API
export const backtestApi = {
  list: () =>
    DEMO_MODE
      ? demoApi.backtestApi.list()
      : api.get<any, Backtest[]>('/backtests'),
  
  get: (id: number) =>
    DEMO_MODE
      ? demoApi.backtestApi.get(id)
      : api.get<any, Backtest>(`/backtests/${id}`),
  
  create: (data: { strategyId: number; startDate: string; endDate: string; initialCapital: number }) =>
    DEMO_MODE
      ? demoApi.backtestApi.create(data)
      : api.post<any, Backtest>('/backtests', data),
  
  delete: (id: number) =>
    DEMO_MODE
      ? demoApi.backtestApi.delete(id)
      : api.delete(`/backtests/${id}`)
}

// 监控API
export const monitorApi = {
  listRules: () =>
    DEMO_MODE
      ? demoApi.monitorApi.listRules()
      : api.get<any, MonitorRule[]>('/monitor/rules'),
  
  createRule: (data: any) =>
    DEMO_MODE
      ? demoApi.monitorApi.createRule(data)
      : api.post<any, MonitorRule>('/monitor/rules', data),
  
  updateRule: (id: number, data: any) =>
    DEMO_MODE
      ? demoApi.monitorApi.updateRule(id, data)
      : api.put<any, MonitorRule>(`/monitor/rules/${id}`, data),
  
  deleteRule: (id: number) =>
    DEMO_MODE
      ? demoApi.monitorApi.deleteRule(id)
      : api.delete(`/monitor/rules/${id}`),
  
  toggleRule: (id: number, isActive: boolean) =>
    DEMO_MODE
      ? demoApi.monitorApi.toggleRule(id, isActive)
      : api.post(`/monitor/rules/${id}/toggle`),
  
  getAlerts: () =>
    DEMO_MODE
      ? demoApi.monitorApi.getAlerts()
      : api.get('/monitor/alerts'),
  
  // 关注列表
  getWatchlist: () =>
    DEMO_MODE
      ? demoApi.monitorApi.getWatchlist()
      : api.get<any, any[]>('/stocks/watchlist')
        .then((rows) => (rows || []).map((item) => normalizeWatchlistRow(item))),
  
  addToWatchlist: (symbol: string, notes?: string) =>
    DEMO_MODE
      ? demoApi.monitorApi.addToWatchlist(symbol, notes)
      : api.post<any, any>('/stocks/watchlist', { symbol, notes })
        .then((row) => normalizeWatchlistRow(row)),
  
  removeFromWatchlist: (id: number) =>
    DEMO_MODE
      ? demoApi.monitorApi.removeFromWatchlist(id)
      : api.delete(`/stocks/watchlist/${id}`)
}

export const aiApi = {
  analyzeStock: (
    symbol: string,
    payload?: {
      period?: string
      start?: string
      end?: string
      count?: number
      focus?: string
      providerId?: string
      model?: string
    }
  ) =>
    DEMO_MODE
      ? demoApi.aiApi.analyzeStock(symbol, payload)
      : api.post<any, StockAnalysisResult>(`/ai/analyze/stock/${symbol}`, payload ?? {}),

  chat: (
    payload: {
      question: string
      symbol?: string
      focus?: string
      skillId?: string
      providerId?: string
      model?: string
      sessionId?: number
      knowledgeBaseId?: number
      useMemory?: boolean
      readerContext?: {
        bookId: number
        title: string
        format: string
        locator: string
        selectedText: string
      }
    }
  ) =>
    DEMO_MODE
      ? demoApi.aiApi.chat(payload)
      : api.post<any, AiChatResult>('/ai/chat', payload),

  listSessions: () =>
    DEMO_MODE ? Promise.resolve([] as AiChatSessionSummary[]) : api.get<any, AiChatSessionSummary[]>('/ai/sessions'),

  getSession: (id: number) =>
    api.get<any, AiChatSessionDetail>(`/ai/sessions/${id}`),

  createSession: (payload: { title?: string; symbol?: string; skillId?: string; providerId?: string; model?: string }) =>
    api.post<any, AiChatSessionSummary>('/ai/sessions', payload),

  deleteSession: (id: number) =>
    api.delete(`/ai/sessions/${id}`),

  listMemories: (
    params?: {
      type?: string
      sourceType?: string
      knowledgeBaseId?: number
      query?: string
      page?: number
      pageSize?: number
    }
  ) =>
    api.get<any, AiMemoryListResult>('/ai/memories', { params }),

  createMemory: (payload: {
    type?: string
    title?: string
    content: string
    symbol?: string
    tags?: string
    priority?: number
    sourceType?: string
    sourceUrl?: string
    sourceRef?: string
    knowledgeBaseId?: number
    providerId?: string
    model?: string
  }) =>
    api.post<any, AiMemoryRecord>('/ai/memories', payload),

  updateMemory: (
    id: number,
    payload: {
      type?: string
      title?: string
      content?: string
      symbol?: string
      tags?: string
      priority?: number
      sourceType?: string
      sourceUrl?: string
      sourceRef?: string
      knowledgeBaseId?: number
      providerId?: string
      model?: string
    }
  ) =>
    api.put<any, AiMemoryRecord>(`/ai/memories/${id}`, payload),

  syncMemory: (id: number) =>
    api.post<any, AiMemoryRecord>(`/ai/memories/${id}/sync`, {}),

  deleteMemory: (id: number) =>
    api.delete(`/ai/memories/${id}`),

  optimizePrompt: (
    payload: {
      question: string
      symbol?: string
      providerId?: string
      model?: string
      scene?: string
      contextText?: string
      knowledgeBaseId?: number
      readerContext?: {
        bookId: number
        title: string
        format: string
        locator: string
        selectedText: string
      }
    }
  ) =>
    DEMO_MODE
      ? Promise.resolve({
        model: payload.model || 'demo-optimizer',
        optimizedPrompt: payload.question,
        generatedAt: new Date().toISOString()
      } as AiPromptOptimizeResult)
      : api.post<any, AiPromptOptimizeResult>('/ai/optimize-prompt', payload),

  listModels: (
    payload?: {
      providerId?: string
      baseUrl?: string
      apiKey?: string
    }
  ) =>
    DEMO_MODE
      ? Promise.resolve({
        providerId: payload?.providerId || 'demo',
        models: ['demo-analyst-v1'],
        fetchedAt: new Date().toISOString()
      } as AiModelsResult)
      : api.post<any, AiModelsResult>('/ai/models', payload ?? {})
}

export const crawlerApi = {
  listSources: () => api.get<any, CrawlerSource[]>('/crawler/sources'),
  createSource: (payload: Partial<CrawlerSource>) => api.post<any, CrawlerSource>('/crawler/sources', payload),
  updateSource: (id: number, payload: Partial<CrawlerSource>) => api.put<any, CrawlerSource>(`/crawler/sources/${id}`, payload),
  deleteSource: (id: number) => api.delete(`/crawler/sources/${id}`),
  runSource: (id: number) => api.post<any, CrawlerJobRecord>(`/crawler/sources/${id}/run`),
  listDocuments: (params?: { sourceId?: number; symbol?: string }) =>
    api.get<any, CrawlerDocument[]>('/crawler/documents', { params })
}

export const knowledgeApi = {
  list: () => api.get<any, KnowledgeBase[]>('/knowledge-bases'),
  create: (payload: { name: string; description?: string }) => api.post<any, KnowledgeBase>('/knowledge-bases', payload),
  update: (id: number, payload: { name: string; description?: string }) => api.put<any, KnowledgeBase>(`/knowledge-bases/${id}`, payload),
  delete: (id: number) => api.delete(`/knowledge-bases/${id}`),
  listDocuments: (id: number) => api.get<any, KnowledgeDocument[]>(`/knowledge-bases/${id}/documents`),
  getDocument: (id: number, documentId: number) => api.get<any, KnowledgeDocument>(`/knowledge-bases/${id}/documents/${documentId}`),
  importMarkdown: (id: number, payload: { title: string; markdown: string; sourceUrl?: string; sourceType?: string }) =>
    api.post<any, KnowledgeDocument>(`/knowledge-bases/${id}/documents/import-markdown`, payload),
  importCrawlerDocument: (id: number, crawlerDocumentId: number) =>
    api.post<any, KnowledgeDocument>(`/knowledge-bases/${id}/documents/import-crawler/${crawlerDocumentId}`),
  exportDocument: (id: number, documentId: number) =>
    api.get<any, Blob>(`/knowledge-bases/${id}/documents/${documentId}/export`, { responseType: 'blob' }),
  chat: (id: number, payload: { question: string; providerId?: string; model?: string }) =>
    api.post<any, AiChatResult>(`/knowledge-bases/${id}/chat`, payload)
}

export const readerApi = {
  listBooks: () => api.get<any, ReaderBook[]>('/reader/books'),
  getBook: (id: number) => api.get<any, ReaderBook>(`/reader/books/${id}`),
  deleteBook: (id: number) => api.delete(`/reader/books/${id}`),
  uploadBook: (
    file: File,
    options?: {
      timeoutMs?: number
      onProgress?: (percent: number) => void
    }
  ) => {
    const form = new FormData()
    form.append('file', file)
    return api.post<any, ReaderBook>('/reader/books/upload', form, {
      timeout: options?.timeoutMs ?? 10 * 60 * 1000,
      onUploadProgress: (event) => {
        const total = Number(event.total || 0)
        const loaded = Number(event.loaded || 0)
        if (!options?.onProgress || total <= 0) {
          return
        }

        const percent = Math.max(0, Math.min(100, Math.round((loaded / total) * 100)))
        options.onProgress(percent)
      }
    })
  },
  importCrawlerDocument: (crawlerDocumentId: number) =>
    api.post<any, ReaderBook>(`/reader/books/import-crawler/${crawlerDocumentId}`),
  getBookContent: (id: number) =>
    api.get<any, Blob>(`/reader/books/${id}/content`, { responseType: 'blob' }),
  getProgress: (id: number) =>
    api.get<any, ReaderProgress | null>(`/reader/books/${id}/progress`),
  saveProgress: (
    id: number,
    payload: { locator?: string; chapterTitle?: string; pageNumber?: number | null; percentage?: number | null }
  ) =>
    api.put<any, ReaderProgress>(`/reader/books/${id}/progress`, payload),
  listHighlights: (id: number) =>
    api.get<any, ReaderHighlight[]>(`/reader/books/${id}/highlights`),
  createHighlight: (
    id: number,
    payload: { locator?: string; chapterTitle?: string; selectedText: string; note?: string; color?: string }
  ) =>
    api.post<any, ReaderHighlight>(`/reader/books/${id}/highlights`, payload),
  updateHighlight: (
    id: number,
    highlightId: number,
    payload: { locator?: string; chapterTitle?: string; selectedText?: string; note?: string; color?: string }
  ) =>
    api.put<any, ReaderHighlight>(`/reader/books/${id}/highlights/${highlightId}`, payload),
  deleteHighlight: (id: number, highlightId: number) =>
    api.delete(`/reader/books/${id}/highlights/${highlightId}`)
}

// 配置API
export const configApi = {
  get: () =>
    DEMO_MODE
      ? demoApi.configApi.get()
      : api.get<any, SystemConfig>('/config'),
  
  update: (data: Partial<SystemConfig>) =>
    DEMO_MODE
      ? demoApi.configApi.update(data)
      : api.put('/config', data),
  
  testEmail: () =>
    DEMO_MODE
      ? demoApi.configApi.testEmail()
      : api.post('/config/test/email'),
  
  testFeishu: () =>
    DEMO_MODE
      ? demoApi.configApi.testFeishu()
      : api.post('/config/test/feishu'),
  
  testWechat: () =>
    DEMO_MODE
      ? demoApi.configApi.testWechat()
      : api.post('/config/test/wechat'),
  
  testLongBridge: () =>
    DEMO_MODE
      ? demoApi.configApi.testLongBridge()
      : api.post('/config/test/longbridge'),

  testMcp: () =>
    DEMO_MODE
      ? Promise.resolve({ success: true, message: '演示模式：MCP 测试已跳过（使用真实后端可测试）' })
      : api.post('/config/test/mcp'),

  testOpenAi: () =>
    DEMO_MODE
      ? demoApi.configApi.testOpenAi()
      : api.post('/config/test/openai')
}

// 复盘API
export const reviewApi = {
  list: (params?: { startDate?: string; endDate?: string }) =>
    DEMO_MODE
      ? demoApi.reviewApi.list(params)
      : api.get<any, ReviewRecord[]>('/reviews', { params }),
  
  get: (id: number) =>
    DEMO_MODE
      ? demoApi.reviewApi.get(id)
      : api.get<any, ReviewRecord>(`/reviews/${id}`),
  
  create: (data: Partial<ReviewRecord>) =>
    DEMO_MODE
      ? demoApi.reviewApi.create(data)
      : api.post<any, ReviewRecord>('/reviews', data),
  
  update: (id: number, data: Partial<ReviewRecord>) =>
    DEMO_MODE
      ? demoApi.reviewApi.update(id, data)
      : api.put<any, ReviewRecord>(`/reviews/${id}`, data),
  
  delete: (id: number) =>
    DEMO_MODE
      ? demoApi.reviewApi.delete(id)
      : api.delete(`/reviews/${id}`),
  
  getStats: (startDate?: string, endDate?: string) =>
    DEMO_MODE
      ? demoApi.reviewApi.getStats(startDate, endDate)
      : api.get('/reviews/stats', { params: { startDate, endDate } })
}

export default api
