import * as signalR from '@microsoft/signalr'
import { shouldUseDemoApi } from '@/api/demo'

function resolveHubUrl(): string {
  const rawValue = String(import.meta.env.VITE_SIGNALR_BASE_URL || '').trim()
  if (!rawValue) {
    return '/hubs/trading'
  }

  const normalized = rawValue.replace(/\/+$/, '')
  return `${normalized}/hubs/trading`
}

class SignalRService {
  private connection: signalR.HubConnection | null = null
  private reconnectAttempts = 0
  private maxReconnectAttempts = 5
  private manualDisconnect = false
  private quoteSubscriptions = new Set<string>()
  private strategySubscriptions = new Set<number>()

  async connect(): Promise<void> {
    if (shouldUseDemoApi()) {
      return
    }

    if (
      this.connection?.state === signalR.HubConnectionState.Connected ||
      this.connection?.state === signalR.HubConnectionState.Connecting
    ) {
      return
    }

    this.manualDisconnect = false

    const hubUrl = resolveHubUrl()

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Information)
      .build()

    this.connection.onreconnecting((error) => {
      console.log('SignalR 正在重连...', error)
    })

    this.connection.onreconnected((connectionId) => {
      console.log('SignalR 已重连', connectionId)
      this.reconnectAttempts = 0
      void this.resubscribeAll()
    })

    this.connection.onclose((error) => {
      console.log('SignalR 连接关闭', error)
      if (!this.manualDisconnect) {
        void this.tryReconnect()
      }
    })

    try {
      await this.connection.start()
      console.log('SignalR 已连接')
      this.reconnectAttempts = 0
      await this.resubscribeAll()
    } catch (error) {
      console.error('SignalR 连接失败', error)
      void this.tryReconnect()
    }
  }

  private async tryReconnect(): Promise<void> {
    if (this.manualDisconnect) {
      return
    }

    if (this.reconnectAttempts >= this.maxReconnectAttempts) {
      console.error('SignalR 重连次数已达上限')
      return
    }

    if (
      this.connection?.state === signalR.HubConnectionState.Connected ||
      this.connection?.state === signalR.HubConnectionState.Connecting
    ) {
      return
    }

    this.reconnectAttempts++
    const delay = Math.min(1000 * Math.pow(2, this.reconnectAttempts), 30000)
    
    setTimeout(() => {
      void this.connect()
    }, delay)
  }

  disconnect(): void {
    this.manualDisconnect = true
    if (this.connection) {
      void this.connection.stop()
      this.connection = null
    }
  }

  private async resubscribeAll(): Promise<void> {
    if (this.connection?.state !== signalR.HubConnectionState.Connected) {
      return
    }

    const quoteSymbols = Array.from(this.quoteSubscriptions)
    if (quoteSymbols.length > 0) {
      await this.connection.invoke('SubscribeQuote', quoteSymbols)
    }

    const strategyIds = Array.from(this.strategySubscriptions)
    for (const strategyId of strategyIds) {
      await this.connection.invoke('SubscribeStrategy', strategyId)
    }
  }

  // 订阅行情
  async subscribeQuote(symbols: string[]): Promise<void> {
    const normalizedSymbols = symbols
      .map((symbol) => String(symbol || '').trim().toUpperCase())
      .filter(Boolean)

    normalizedSymbols.forEach((symbol) => this.quoteSubscriptions.add(symbol))

    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('SubscribeQuote', normalizedSymbols)
    }
  }

  // 取消订阅行情
  async unsubscribeQuote(symbols: string[]): Promise<void> {
    const normalizedSymbols = symbols
      .map((symbol) => String(symbol || '').trim().toUpperCase())
      .filter(Boolean)

    normalizedSymbols.forEach((symbol) => this.quoteSubscriptions.delete(symbol))

    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('UnsubscribeQuote', normalizedSymbols)
    }
  }

  // 订阅策略状态
  async subscribeStrategy(strategyId: number): Promise<void> {
    this.strategySubscriptions.add(strategyId)
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('SubscribeStrategy', strategyId)
    }
  }

  // 监听行情更新
  onQuoteUpdate(callback: (quote: any) => void): void {
    this.connection?.off('QuoteUpdate')
    this.connection?.on('QuoteUpdate', (arg1: any, arg2?: any) => {
      // Compatible with both payload styles:
      // 1) QuoteUpdate(quote)
      // 2) QuoteUpdate(symbol, quote)
      if (arg2 && typeof arg2 === 'object') {
        callback({
          ...arg2,
          symbol: arg2.symbol || arg1
        })
        return
      }

      if (arg1 && typeof arg1 === 'object') {
        callback(arg1)
      }
    })
  }

  // 监听策略执行
  onStrategyExecuted(callback: (result: any) => void): void {
    this.connection?.off('StrategyExecuted')
    this.connection?.on('StrategyExecuted', callback)
  }

  // 监听交易更新
  onTradeUpdate(callback: (trade: any) => void): void {
    this.connection?.off('TradeUpdate')
    this.connection?.on('TradeUpdate', callback)
  }

  // 监听通知
  onNotification(callback: (notification: any) => void): void {
    this.connection?.off('Notification')
    this.connection?.on('Notification', callback)
  }

  // 监听监控告警
  onMonitorAlert(callback: (alert: any) => void): void {
    this.connection?.off('MonitorAlert')
    this.connection?.on('MonitorAlert', callback)
  }

  // 监听策略热重载
  onStrategyReloaded(callback: (strategyId: number) => void): void {
    this.connection?.off('StrategyReloaded')
    this.connection?.on('StrategyReloaded', callback)
  }

  // 移除监听
  off(event: string): void {
    this.connection?.off(event)
  }
}

export const signalRService = new SignalRService()
export default signalRService
