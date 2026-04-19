import * as signalR from '@microsoft/signalr'

class SignalRService {
  private connection: signalR.HubConnection | null = null
  private reconnectAttempts = 0
  private maxReconnectAttempts = 5

  async connect(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      return
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/trading')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Information)
      .build()

    this.connection.onreconnecting((error) => {
      console.log('SignalR 正在重连...', error)
    })

    this.connection.onreconnected((connectionId) => {
      console.log('SignalR 已重连', connectionId)
      this.reconnectAttempts = 0
    })

    this.connection.onclose((error) => {
      console.log('SignalR 连接关闭', error)
      this.tryReconnect()
    })

    try {
      await this.connection.start()
      console.log('SignalR 已连接')
      this.reconnectAttempts = 0
    } catch (error) {
      console.error('SignalR 连接失败', error)
      this.tryReconnect()
    }
  }

  private async tryReconnect(): Promise<void> {
    if (this.reconnectAttempts >= this.maxReconnectAttempts) {
      console.error('SignalR 重连次数已达上限')
      return
    }

    this.reconnectAttempts++
    const delay = Math.min(1000 * Math.pow(2, this.reconnectAttempts), 30000)
    
    setTimeout(() => {
      this.connect()
    }, delay)
  }

  disconnect(): void {
    if (this.connection) {
      this.connection.stop()
      this.connection = null
    }
  }

  // 订阅行情
  async subscribeQuote(symbols: string[]): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('SubscribeQuote', symbols)
    }
  }

  // 取消订阅行情
  async unsubscribeQuote(symbols: string[]): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('UnsubscribeQuote', symbols)
    }
  }

  // 订阅策略状态
  async subscribeStrategy(strategyId: number): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('SubscribeStrategy', strategyId)
    }
  }

  // 监听行情更新
  onQuoteUpdate(callback: (quote: any) => void): void {
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
    this.connection?.on('StrategyExecuted', callback)
  }

  // 监听交易更新
  onTradeUpdate(callback: (trade: any) => void): void {
    this.connection?.on('TradeUpdate', callback)
  }

  // 监听通知
  onNotification(callback: (notification: any) => void): void {
    this.connection?.on('Notification', callback)
  }

  // 监听监控告警
  onMonitorAlert(callback: (alert: any) => void): void {
    this.connection?.on('MonitorAlert', callback)
  }

  // 监听策略热重载
  onStrategyReloaded(callback: (strategyId: number) => void): void {
    this.connection?.on('StrategyReloaded', callback)
  }

  // 移除监听
  off(event: string): void {
    this.connection?.off(event)
  }
}

export const signalRService = new SignalRService()
export default signalRService
