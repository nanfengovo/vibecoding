import axios from 'axios'
import type { AxiosInstance, AxiosResponse } from 'axios'
import type {
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
  StockAnalysisResult
} from '@/types'

function resolveApiBaseUrl(): string {
  const rawValue = String(import.meta.env.VITE_API_BASE_URL || '').trim()
  if (!rawValue) {
    return '/api'
  }

  const normalized = rawValue.replace(/\/+$/, '')
  return normalized.endsWith('/api') ? normalized : `${normalized}/api`
}

const api: AxiosInstance = axios.create({
  baseURL: resolveApiBaseUrl(),
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json'
  }
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

// 响应拦截器
api.interceptors.response.use(
  (response: AxiosResponse) => response.data,
  (error) => {
    const message = error.response?.data?.message || '请求失败'
    console.error('API Error:', message)
    return Promise.reject(error)
  }
)

// 股票API
export const stockApi = {
  search: (query: string) => 
    api.get<any, Stock[]>('/stocks/search', { params: { keyword: query } }),
  
  getQuote: (symbol: string) => 
    api.get<any, StockQuote>(`/stocks/${symbol}/quote`),
  
  getQuotes: (symbols: string[]) => 
    api.post<any, StockQuote[]>('/stocks/quotes', { symbols }),
  
  getKline: (
    symbol: string,
    period: string = '1d',
    limit: number = 100,
    options?: { start?: string; end?: string }
  ) =>
    api.get<any, Candlestick[]>(`/stocks/${symbol}/kline`, {
      params: {
        period,
        count: limit,
        start: options?.start,
        end: options?.end
      }
    }),
  
  getDetail: (symbol: string) => 
    api.get<any, Stock>(`/stocks/${symbol}`)
}

// 策略API
export const strategyApi = {
  list: async () => {
    const rows = await api.get<any, any[]>('/strategies')
    return (rows || []).map(normalizeStrategy)
  },
  
  get: async (id: number) => {
    const row = await api.get<any, any>(`/strategies/${id}`)
    return normalizeStrategy(row)
  },
  
  create: async (data: Partial<Strategy>) => {
    const row = await api.post<any, any>('/strategies', data)
    return normalizeStrategy(row)
  },
  
  update: async (id: number, data: Partial<Strategy>) => {
    const row = await api.put<any, any>(`/strategies/${id}`, data)
    return normalizeStrategy(row)
  },
  
  delete: (id: number) => 
    api.delete(`/strategies/${id}`),
  
  toggle: (id: number, isActive: boolean) => 
    api.post(`/strategies/${id}/toggle`, { isActive }),
  
  execute: (id: number) => 
    api.post(`/strategies/${id}/execute`),
  
  reload: (id: number) => 
    api.post(`/strategies/${id}/reload`)
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
    api.get<any, Trade>(`/trades/${id}`),
  
  getByStrategy: (strategyId: number) => 
    api.get<any, Trade[]>(`/trades/strategy/${strategyId}`),

  getAccount: () =>
    api.get('/trades/account'),

  getPositions: () =>
    api.get('/trades/positions'),
  
  getStats: async (startDate?: string, endDate?: string) => {
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
  }) => api.post('/trades', payload),

  cancelOrder: (orderId: string) =>
    api.delete(`/trades/${orderId}`)
}

// 回测API
export const backtestApi = {
  list: () => 
    api.get<any, Backtest[]>('/backtests'),
  
  get: (id: number) => 
    api.get<any, Backtest>(`/backtests/${id}`),
  
  create: (data: { strategyId: number; startDate: string; endDate: string; initialCapital: number }) => 
    api.post<any, Backtest>('/backtests', data),
  
  delete: (id: number) => 
    api.delete(`/backtests/${id}`)
}

// 监控API
export const monitorApi = {
  listRules: () => 
    api.get<any, MonitorRule[]>('/monitor/rules'),
  
  createRule: (data: any) =>
    api.post<any, MonitorRule>('/monitor/rules', data),
  
  updateRule: (id: number, data: any) =>
    api.put<any, MonitorRule>(`/monitor/rules/${id}`, data),
  
  deleteRule: (id: number) => 
    api.delete(`/monitor/rules/${id}`),
  
  toggleRule: (id: number, _isActive: boolean) =>
    api.post(`/monitor/rules/${id}/toggle`),
  
  getAlerts: () => 
    api.get('/monitor/alerts'),
  
  // 关注列表
  getWatchlist: () => 
    api.get<any, WatchlistItem[]>('/stocks/watchlist'),
  
  addToWatchlist: (symbol: string, notes?: string) => 
    api.post<any, WatchlistItem>('/stocks/watchlist', { symbol, notes }),
  
  removeFromWatchlist: (id: number) => 
    api.delete(`/stocks/watchlist/${id}`)
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
    }
  ) =>
    api.post<any, StockAnalysisResult>(`/ai/analyze/stock/${symbol}`, payload ?? {})
}

// 配置API
export const configApi = {
  get: () => 
    api.get<any, SystemConfig>('/config'),
  
  update: (data: Partial<SystemConfig>) => 
    api.put('/config', data),
  
  testEmail: () => 
    api.post('/config/test/email'),
  
  testFeishu: () => 
    api.post('/config/test/feishu'),
  
  testWechat: () => 
    api.post('/config/test/wechat'),
  
  testLongBridge: () => 
    api.post('/config/test/longbridge'),

  testOpenAi: () =>
    api.post('/config/test/openai')
}

// 复盘API
export const reviewApi = {
  list: (params?: { startDate?: string; endDate?: string }) => 
    api.get<any, ReviewRecord[]>('/reviews', { params }),
  
  get: (id: number) => 
    api.get<any, ReviewRecord>(`/reviews/${id}`),
  
  create: (data: Partial<ReviewRecord>) => 
    api.post<any, ReviewRecord>('/reviews', data),
  
  update: (id: number, data: Partial<ReviewRecord>) => 
    api.put<any, ReviewRecord>(`/reviews/${id}`, data),
  
  delete: (id: number) => 
    api.delete(`/reviews/${id}`),
  
  getStats: (startDate?: string, endDate?: string) => 
    api.get('/reviews/stats', { params: { startDate, endDate } })
}

export default api
