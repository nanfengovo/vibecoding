import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { StockQuote, Strategy, WatchlistItem, SystemConfig } from '@/types'
import { strategyApi, monitorApi, configApi, stockApi } from '@/api'
import signalRService from '@/api/signalr'

export const useAppStore = defineStore('app', () => {
  const storageTheme = (localStorage.getItem('qt-theme') || 'light') as 'light' | 'dark'

  // 状态
  const sidebarCollapsed = ref(false)
  const theme = ref<'light' | 'dark'>(storageTheme === 'dark' ? 'dark' : 'light')
  const quotes = ref<Map<string, StockQuote>>(new Map())
  const strategies = ref<Strategy[]>([])
  const watchlist = ref<WatchlistItem[]>([])
  const config = ref<SystemConfig | null>(null)
  const notifications = ref<any[]>([])
  const quoteError = ref('')
  const loading = ref({
    strategies: false,
    watchlist: false,
    config: false
  })

  // 计算属性
  const activeStrategies = computed(() => 
    strategies.value.filter(s => s.isActive)
  )

  const watchlistSymbols = computed(() => 
    watchlist.value.map(item => item.symbol)
  )

  // 方法
  function toggleSidebar() {
    sidebarCollapsed.value = !sidebarCollapsed.value
  }

  function applyTheme(nextTheme: 'light' | 'dark' = theme.value) {
    if (typeof document === 'undefined') {
      return
    }

    document.documentElement.classList.toggle('dark', nextTheme === 'dark')
    localStorage.setItem('qt-theme', nextTheme)
  }

  function setTheme(nextTheme: 'light' | 'dark') {
    theme.value = nextTheme
    applyTheme(nextTheme)
  }

  function toggleTheme() {
    setTheme(theme.value === 'dark' ? 'light' : 'dark')
  }

  async function fetchStrategies() {
    loading.value.strategies = true
    try {
      strategies.value = await strategyApi.list()
    } finally {
      loading.value.strategies = false
    }
  }

  async function fetchWatchlist() {
    loading.value.watchlist = true
    quoteError.value = ''
    try {
      watchlist.value = await monitorApi.getWatchlist()
      // 订阅行情
      if (watchlistSymbols.value.length > 0) {
        await signalRService.subscribeQuote(watchlistSymbols.value)
        try {
          const quotes = await stockApi.getQuotes(watchlistSymbols.value)
          if (Array.isArray(quotes) && quotes.length > 0) {
            quotes.forEach((item) => updateQuote(item as unknown as StockQuote))
          } else {
            throw new Error('行情接口返回为空')
          }
        } catch (error) {
          const fallbackResults = await Promise.allSettled(
            watchlistSymbols.value.map(async (symbol) => {
              const quote = await stockApi.getQuote(symbol)
              updateQuote(quote as unknown as StockQuote)
            })
          )

          const hasSuccess = fallbackResults.some((item) => item.status === 'fulfilled')
          if (!hasSuccess) {
            const message = (error as any)?.response?.data?.message
              || (error as Error)?.message
              || '行情接口暂不可用，请检查长桥配置或凭证权限'
            quoteError.value = message
          }
        }
      }
    } finally {
      loading.value.watchlist = false
    }
  }

  async function fetchConfig() {
    loading.value.config = true
    try {
      config.value = await configApi.get()
    } finally {
      loading.value.config = false
    }
  }

  async function addToWatchlist(symbol: string, notes?: string) {
    const normalized = String(symbol || '').trim().toUpperCase()
    const item = await monitorApi.addToWatchlist(normalized, notes)
    const itemSymbol = String(item?.symbol || normalized).trim().toUpperCase()

    // Upsert by id/symbol to avoid duplicate rows or "not refreshed" perception.
    const existingIndex = watchlist.value.findIndex((row) => {
      const sameId = Number(row.id) > 0 && Number(row.id) === Number(item.id)
      const sameSymbol = String(row.symbol || '').trim().toUpperCase() === itemSymbol
      return sameId || sameSymbol
    })

    if (existingIndex >= 0) {
      watchlist.value[existingIndex] = {
        ...watchlist.value[existingIndex],
        ...item,
        symbol: itemSymbol
      }
    } else {
      watchlist.value.unshift({
        ...item,
        symbol: itemSymbol
      })
    }

    await signalRService.subscribeQuote([itemSymbol])
    try {
      const quote = await stockApi.getQuote(itemSymbol)
      updateQuote(quote as unknown as StockQuote)
    } catch {
      // Ignore quote bootstrap failures. Realtime subscription will eventually update.
    }
  }

  async function removeFromWatchlist(id: number) {
    const item = watchlist.value.find(w => w.id === id)
    if (item) {
      await monitorApi.removeFromWatchlist(id)
      watchlist.value = watchlist.value.filter(w => w.id !== id)
      await signalRService.unsubscribeQuote([item.symbol])
    }
  }

  function normalizeQuote(quote: Partial<StockQuote> & Record<string, any>): StockQuote {
    const symbol = String(quote?.symbol ?? '').toUpperCase()
    const current = Number(quote?.current ?? quote?.price ?? 0)
    const previousClose = Number(quote?.previousClose ?? quote?.prevClose ?? current)
    const change = Number(quote?.change ?? (current - previousClose))
    const changePercent = Number(
      quote?.changePercent
      ?? quote?.change_rate
      ?? (previousClose ? (change / previousClose) * 100 : 0)
    )

    return {
      symbol,
      name: String(quote?.name ?? symbol),
      current,
      previousClose,
      change,
      changePercent,
      high: Number(quote?.high ?? current),
      low: Number(quote?.low ?? current),
      open: Number(quote?.open ?? current),
      volume: Number(quote?.volume ?? 0),
      turnover: Number(quote?.turnover ?? 0),
      timestamp: String(quote?.timestamp ?? new Date().toISOString())
    }
  }

  function updateQuote(quote: Partial<StockQuote> & Record<string, any>) {
    const normalized = normalizeQuote(quote)
    if (!normalized.symbol) {
      return
    }

    quotes.value.set(normalized.symbol, normalized)

    // Backward compatibility: some historical watchlist rows are stored as base ticker (e.g. AAPL).
    // LongBridge quote symbol is market-qualified (e.g. AAPL.US), so keep an alias key.
    const baseSymbol = normalized.symbol.split('.')[0] ?? normalized.symbol
    if (baseSymbol && !baseSymbol.includes('.')) {
      quotes.value.set(baseSymbol, normalized)
    }
  }

  function addNotification(notification: any) {
    notifications.value.unshift({
      ...notification,
      id: Date.now(),
      timestamp: new Date().toISOString()
    })
    // 只保留最近100条
    if (notifications.value.length > 100) {
      notifications.value = notifications.value.slice(0, 100)
    }
  }

  function clearNotifications() {
    notifications.value = []
  }

  // 初始化SignalR监听
  function initSignalRListeners() {
    signalRService.onQuoteUpdate((quote) => {
      updateQuote(quote)
    })

    signalRService.onTradeUpdate((trade) => {
      addNotification({
        type: 'success',
        title: '交易状态更新',
        message: `${trade?.symbol || ''} ${trade?.side || ''} ${trade?.status || ''}`.trim()
      })
    })

    signalRService.onNotification((notification) => {
      addNotification(notification)
    })

    signalRService.onMonitorAlert((alert) => {
      addNotification({
        type: 'warning',
        title: '监控规则触发',
        message: alert?.message || `${alert?.symbol || ''} 触发监控条件`
      })
    })

    signalRService.onStrategyReloaded((strategyId) => {
      fetchStrategies()
      addNotification({
        type: 'info',
        title: '策略已重载',
        message: `策略 #${strategyId} 已成功热重载`
      })
    })
  }

  // 初始化时应用主题
  applyTheme(theme.value)

  return {
    // 状态
    sidebarCollapsed,
    theme,
    quotes,
    strategies,
    watchlist,
    config,
    notifications,
    quoteError,
    loading,
    // 计算属性
    activeStrategies,
    watchlistSymbols,
    // 方法
    toggleSidebar,
    setTheme,
    toggleTheme,
    applyTheme,
    fetchStrategies,
    fetchWatchlist,
    fetchConfig,
    addToWatchlist,
    removeFromWatchlist,
    updateQuote,
    addNotification,
    clearNotifications,
    initSignalRListeners
  }
})
