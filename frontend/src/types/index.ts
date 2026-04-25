// 股票相关类型
export interface Stock {
  symbol: string
  name: string
  exchange?: string
  market?: string
  currency?: string
  currentPrice: number
  previousClose: number
  open?: number
  high?: number
  low?: number
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

export interface CompanyProfile {
  symbol: string
  title: string
  overview: string
  sourceUrl?: string
  fields: Array<{
    key: string
    value: string
  }>
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
    skillEnabled: boolean
    skillInstallUrl: string
    mcpEnabled: boolean
    mcpServerUrl: string
    mcpTransport: string
    mcpClientName: string
    mcpAuthToken: string
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
    providers: AiProviderConfig[]
    activeProviderId: string
  }
}

export interface AiProviderConfig {
  id: string
  name: string
  apiKey: string
  baseUrl: string
  model: string
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

export interface AiChatResult {
  model: string
  content: string
  generatedAt: string
  marketContext?: AiChatMarketContext
  sessionId?: number
  references?: AiKnowledgeReference[]
}

export interface AiChatMarketContext {
  symbol: string
  market: string
  price: number
  changePercent: number
  quoteTime: string
  lagSeconds: number
  marketOpen: boolean
  freshness: 'realtime' | 'delayed_close' | 'stale'
  source: string
}

export interface AiPromptOptimizeResult {
  model: string
  optimizedPrompt: string
  generatedAt: string
}

export interface AiModelsResult {
  providerId: string
  models: string[]
  fetchedAt: string
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

export interface AuthUser {
  id: number
  username: string
  displayName: string
  role: 'admin' | 'user' | string
  isActive: boolean
  createdAt: string
  lastLoginAt?: string
}

export interface AuthResponse {
  token: string
  user: AuthUser
}

export interface AiChatSessionSummary {
  id: number
  title: string
  symbol: string
  skillId: string
  providerId: string
  model: string
  createdAt: string
  updatedAt: string
}

export interface AiChatMessageRecord {
  id: number
  role: 'user' | 'assistant'
  content: string
  model: string
  marketContext?: AiChatMarketContext
  isError: boolean
  createdAt: string
}

export interface AiChatSessionDetail {
  session: AiChatSessionSummary
  messages: AiChatMessageRecord[]
}

export interface AiMemoryRecord {
  id: number
  type: string
  title: string
  content: string
  symbol: string
  tags: string
  priority: number
  createdAt: string
  updatedAt: string
}

export interface AiKnowledgeReference {
  documentId: number
  chunkId: number
  title: string
  sourceUrl: string
  snippet: string
}

export interface CrawlerSource {
  id: number
  name: string
  type: 'longbridge_news' | 'longbridge_quote' | 'rss' | 'markdown' | 'web' | string
  url: string
  symbol: string
  tags: string
  isEnabled: boolean
  crawlIntervalMinutes: number
  maxPages: number
  lastRunAt?: string
  createdAt: string
  updatedAt: string
}

export interface CrawlerJobRecord {
  id: number
  sourceId: number
  status: string
  documentsFound: number
  documentsSaved: number
  errorMessage: string
  startedAt: string
  finishedAt?: string
}

export interface CrawlerDocument {
  id: number
  sourceId: number
  symbol: string
  title: string
  url: string
  markdown: string
  summary: string
  tags: string
  publishedAt?: string
  createdAt: string
}

export interface KnowledgeBase {
  id: number
  name: string
  description: string
  isDefault: boolean
  createdAt: string
  updatedAt: string
}

export interface KnowledgeDocument {
  id: number
  knowledgeBaseId: number
  title: string
  sourceUrl: string
  sourceType: string
  markdown: string
  createdAt: string
  updatedAt: string
}
