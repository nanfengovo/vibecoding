// 股票相关类型
export interface Stock {
  symbol: string
  name: string
  exchange?: string
  market?: string
  currentPrice: number
  previousClose: number
  change: number
  changePercent: number
  volume: number
  marketCap: number
  high52Week: number
  low52Week: number
  avgVolume: number
  pe: number
  eps: number
  dividend: number
  updatedAt: string
}

export interface StockQuote {
  symbol: string
  name: string
  current: number
  previousClose: number
  change: number
  changePercent: number
  high: number
  low: number
  open: number
  volume: number
  turnover: number
  timestamp: string
}

export interface Candlestick {
  time: string
  open: number
  high: number
  low: number
  close: number
  volume: number
}

// 策略相关类型
export interface Strategy {
  id: number
  name: string
  description: string
  config: StrategyConfig
  isActive: boolean
  createdAt: string
  updatedAt: string
  lastExecutedAt?: string
}

export interface StrategyConfig {
  conditions: StrategyCondition[]
  actions: StrategyAction[]
  targetSymbols: string[]
  checkInterval: number
}

export type ConditionType = 
  | 'price_above' 
  | 'price_below' 
  | 'price_change_percent'
  | 'volume_above'
  | 'volume_change_percent'
  | 'ma_cross_up'
  | 'ma_cross_down'
  | 'rsi_above'
  | 'rsi_below'
  | 'macd_cross_up'
  | 'macd_cross_down'
  | 'kdj_golden_cross'
  | 'kdj_death_cross'
  | 'boll_upper_break'
  | 'boll_lower_break'

export interface StrategyCondition {
  id: string
  type: ConditionType
  params: Record<string, number | string>
  operator: 'and' | 'or'
}

export type ActionType = 
  | 'buy' 
  | 'sell' 
  | 'notify_email' 
  | 'notify_feishu' 
  | 'notify_wechat'
  | 'log'

export interface StrategyAction {
  id: string
  type: ActionType
  params: Record<string, number | string>
}

// 交易相关类型
export interface Trade {
  id: number
  symbol: string
  stockName: string
  side: 'buy' | 'sell'
  quantity: number
  price: number
  amount: number
  commission: number
  status: 'pending' | 'filled' | 'cancelled' | 'rejected'
  orderId?: string
  strategyId?: number
  strategyName?: string
  executedAt: string
  createdAt: string
}

// 回测相关类型
export interface Backtest {
  id: number
  strategyId: number
  strategyName: string
  startDate: string
  endDate: string
  initialCapital: number
  finalCapital: number
  totalReturn: number
  annualizedReturn: number
  maxDrawdown: number
  sharpeRatio: number
  winRate: number
  totalTrades: number
  profitTrades: number
  lossTrades: number
  status: 'pending' | 'running' | 'completed' | 'failed'
  equityCurve: EquityPoint[]
  trades: BacktestTrade[]
  createdAt: string
  completedAt?: string
}

export interface EquityPoint {
  date: string
  equity: number
  drawdown: number
}

export interface BacktestTrade {
  symbol: string
  side: 'buy' | 'sell'
  price: number
  quantity: number
  date: string
  profit?: number
}

// 监控相关类型
export interface MonitorRule {
  id: number
  name: string
  symbols: string[]
  conditions: MonitorCondition[]
  notifications: NotificationChannel[]
  checkInterval: number
  isActive: boolean
  lastTriggeredAt?: string
  createdAt: string
}

export interface MonitorCondition {
  type: string
  operator: 'gt' | 'lt' | 'eq' | 'gte' | 'lte'
  value: number
}

export interface NotificationChannel {
  type: 'email' | 'feishu' | 'wechat'
  enabled: boolean
}

// 关注列表
export interface WatchlistItem {
  id: number
  symbol: string
  name: string
  notes?: string
  addedAt: string
  stock?: Stock
}

// 系统配置
export interface SystemConfig {
  longBridge: {
    appKey: string
    appSecret: string
    accessToken: string
    baseUrl: string
  }
  proxy: {
    enabled: boolean
    host: string
    port: number
    username?: string
    password?: string
  }
  email: {
    enabled: boolean
    smtpHost: string
    smtpPort: number
    username: string
    password: string
    fromAddress: string
    toAddresses: string[]
    useSsl: boolean
  }
  feishu: {
    enabled: boolean
    webhookUrl: string
    signSecret?: string
  }
  wechat: {
    enabled: boolean
    webhookUrl: string
  }
  openAi: {
    enabled: boolean
    apiKey: string
    baseUrl: string
    model: string
  }
}

// 复盘相关
export interface ReviewRecord {
  id: number
  date: string
  marketSummary: string
  trades: Trade[]
  notes: string
  lessons: string
  tags: string[]
  createdAt: string
}

// API响应
export interface ApiResponse<T> {
  success: boolean
  data: T
  message?: string
  errors?: string[]
}

export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
  totalPages: number
}

export interface StockAnalysisResult {
  symbol: string
  model: string
  analysis: string
  generatedAt: string
}
