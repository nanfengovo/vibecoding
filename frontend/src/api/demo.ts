import type {
  AiChatMarketContext,
  AiChatResult,
  AiProviderConfig,
  Backtest,
  Candlestick,
  CompanyProfile,
  MonitorCondition,
  MonitorRule,
  NotificationChannel,
  PagedResult,
  ReviewRecord,
  Stock,
  StockAnalysisResult,
  StockQuote,
  Strategy,
  StrategyAction,
  StrategyCondition,
  SystemConfig,
  Trade,
  WatchlistItem
} from '@/types'

type SymbolMeta = {
  name: string
  market: string
  exchange: string
}

type DemoState = {
  watchlist: WatchlistItem[]
  monitorRules: MonitorRule[]
  strategies: Strategy[]
  trades: Trade[]
  backtests: Backtest[]
  reviews: ReviewRecord[]
  config: SystemConfig
}

type OpenAiLikeConfig = SystemConfig['openAi']
type LongBridgeLikeConfig = SystemConfig['longBridge']
type DemoSecurity = {
  symbol: string
  ticker: string
  name: string
  market: string
}

type DemoRealtimeQuote = {
  symbol: string
  name: string
  current: number
  previousClose: number
  open: number
  high: number
  low: number
  volume: number
  turnover: number
  change: number
  changePercent: number
  timestamp: string
  isRealtime: boolean
  source?: string
}

type DemoStaticInfo = {
  symbol: string
  name: string
  nameEn?: string
  nameCn?: string
  nameHk?: string
  market?: string
  exchange?: string
  currency?: string
  lotSize?: number
  totalShares?: number
  circulatingShares?: number
  hkShares?: number
  eps?: number
  epsTtm?: number
  bps?: number
  dividendYield?: number
  board?: string
}

const STORAGE_KEY = 'qt-demo-state-v1'

const SYMBOL_LIBRARY: Record<string, SymbolMeta> = {
  AAPL: { name: 'Apple Inc.', market: 'US', exchange: 'NASDAQ' },
  MSFT: { name: 'Microsoft Corporation', market: 'US', exchange: 'NASDAQ' },
  NVDA: { name: 'NVIDIA Corporation', market: 'US', exchange: 'NASDAQ' },
  AMZN: { name: 'Amazon.com Inc.', market: 'US', exchange: 'NASDAQ' },
  META: { name: 'Meta Platforms Inc.', market: 'US', exchange: 'NASDAQ' },
  GOOGL: { name: 'Alphabet Inc.', market: 'US', exchange: 'NASDAQ' },
  TSLA: { name: 'Tesla Inc.', market: 'US', exchange: 'NASDAQ' },
  NFLX: { name: 'Netflix Inc.', market: 'US', exchange: 'NASDAQ' },
  AMD: { name: 'Advanced Micro Devices', market: 'US', exchange: 'NASDAQ' },
  INTC: { name: 'Intel Corporation', market: 'US', exchange: 'NASDAQ' },
  BABA: { name: 'Alibaba Group Holding', market: 'US', exchange: 'NYSE' },
  JPM: { name: 'JPMorgan Chase & Co.', market: 'US', exchange: 'NYSE' },
  BAC: { name: 'Bank of America Corp.', market: 'US', exchange: 'NYSE' }
}

const DEFAULT_SYMBOLS = ['AAPL', 'MSFT', 'NVDA', 'TSLA']

const TRUE_VALUES = new Set(['1', 'true', 'yes', 'on'])
const FALSE_VALUES = new Set(['0', 'false', 'no', 'off'])
const SUPPORTED_MARKETS = new Set(['US', 'HK', 'SH', 'SZ', 'SG'])
const REALTIME_STALE_THRESHOLD_SECONDS = 25 * 60
const REALTIME_QUESTION_KEYWORDS = [
  '最新', '实时', '现价', '股价', '价格', '涨跌', '开盘', '收盘', '行情', '报价', '多少', '最新价',
  'last price', 'live price', 'realtime', 'real-time', 'quote', 'latest price', 'current price'
]
const MARKET_TIMEZONE: Record<string, string> = {
  US: 'America/New_York',
  HK: 'Asia/Hong_Kong',
  SH: 'Asia/Shanghai',
  SZ: 'Asia/Shanghai',
  SG: 'Asia/Singapore'
}
const MARKET_TRADING_SESSIONS: Record<string, Array<{ startMinute: number; endMinute: number }>> = {
  US: [{ startMinute: 9 * 60 + 30, endMinute: 16 * 60 }],
  HK: [
    { startMinute: 9 * 60 + 30, endMinute: 12 * 60 },
    { startMinute: 13 * 60, endMinute: 16 * 60 }
  ],
  SH: [
    { startMinute: 9 * 60 + 30, endMinute: 11 * 60 + 30 },
    { startMinute: 13 * 60, endMinute: 15 * 60 }
  ],
  SZ: [
    { startMinute: 9 * 60 + 30, endMinute: 11 * 60 + 30 },
    { startMinute: 13 * 60, endMinute: 15 * 60 }
  ],
  SG: [{ startMinute: 9 * 60, endMinute: 17 * 60 }]
}

let demoNoticeShown = false
let cachedState: DemoState | null = null
let securityUniverseCache: {
  cacheKey: string
  fetchedAt: number
  items: DemoSecurity[]
} | null = null

export function shouldUseDemoApi(): boolean {
  const explicitMode = String(import.meta.env.VITE_DEMO_MODE || '').trim().toLowerCase()
  if (TRUE_VALUES.has(explicitMode)) {
    return true
  }

  if (FALSE_VALUES.has(explicitMode)) {
    return false
  }

  if (typeof window === 'undefined') {
    return false
  }

  const host = String(window.location.hostname || '').toLowerCase()
  const apiBase = String(import.meta.env.VITE_API_BASE_URL || '').trim()
  return host.endsWith('vercel.app') && !apiBase
}

export function announceDemoMode(): void {
  if (demoNoticeShown || !shouldUseDemoApi()) {
    return
  }

  demoNoticeShown = true
  console.info('[quant-demo] Backend not configured, running with built-in demo data mode.')
}

function round(value: number, precision = 2): number {
  const factor = Math.pow(10, precision)
  return Math.round(value * factor) / factor
}

function nowIso(): string {
  return new Date().toISOString()
}

function normalizeSymbol(value: string): string {
  return String(value || '').trim().toUpperCase()
}

function symbolTicker(value: string): string {
  const normalized = normalizeSymbol(value)
  if (!normalized) {
    return ''
  }
  const parts = normalized.split('.')
  return parts[0] || normalized
}

function inferCnRegionFromCode(code: string): 'SH' | 'SZ' {
  return /^[69]/.test(code) ? 'SH' : 'SZ'
}

function normalizeLookupSymbol(value: string): string {
  const normalized = normalizeSymbol(value)
  if (!normalized) {
    return ''
  }

  const shSzPrefixMatch = normalized.match(/^(SH|SZ)(\d{6})$/)
  if (shSzPrefixMatch) {
    return `${shSzPrefixMatch[2]}.${shSzPrefixMatch[1]}`
  }

  const explicitMatch = normalized.match(/^([A-Z0-9]+)\.([A-Z]{2})$/)
  if (explicitMatch) {
    const ticker = explicitMatch[1]
    const market = explicitMatch[2]
    if (!SUPPORTED_MARKETS.has(market)) {
      return normalized
    }
    return `${ticker}.${market}`
  }

  if (normalized.includes('.')) {
    return normalized
  }

  if (/^\d{6}$/.test(normalized)) {
    const region = inferCnRegionFromCode(normalized)
    return `${normalized}.${region}`
  }

  if (/^\d{5}$/.test(normalized)) {
    return `${normalized}.HK`
  }

  return `${normalized}.US`
}

function toDisplaySymbol(value: string): string {
  const normalized = normalizeSymbol(value)
  if (normalized.endsWith('.US')) {
    return normalized.slice(0, -3)
  }
  return normalized
}

function marketName(market: string): string {
  switch (market) {
    case 'SH':
    case 'SZ':
      return 'A股'
    case 'HK':
      return '港股'
    case 'SG':
      return '新加坡'
    default:
      return '美股'
  }
}

function inferSecurityFromInput(value: string): DemoSecurity | null {
  const lookupSymbol = normalizeLookupSymbol(value)
  if (!lookupSymbol || !lookupSymbol.includes('.')) {
    return null
  }

  const [tickerRaw, marketRaw] = lookupSymbol.split('.')
  const ticker = String(tickerRaw || '').trim().toUpperCase()
  const market = String(marketRaw || '').trim().toUpperCase()
  if (!ticker || !SUPPORTED_MARKETS.has(market)) {
    return null
  }

  if (!/^[A-Z0-9]+$/.test(ticker)) {
    return null
  }

  const symbol = `${ticker}.${market}`
  return {
    symbol,
    ticker,
    market,
    name: `${marketName(market)} ${ticker}`
  }
}

function isLikelySymbolKeyword(value: string): boolean {
  const normalized = normalizeSymbol(value)
  if (!normalized) {
    return false
  }

  if (/\s/.test(normalized)) {
    return false
  }

  return /^[A-Z0-9.]{2,16}$/.test(normalized)
}

function hashString(input: string): number {
  let hash = 2166136261
  for (let i = 0; i < input.length; i++) {
    hash ^= input.charCodeAt(i)
    hash = Math.imul(hash, 16777619)
  }
  return hash >>> 0
}

function pseudo(seed: number): number {
  const value = Math.sin(seed) * 10000
  return value - Math.floor(value)
}

function parseMaybeDate(value?: string): number | null {
  if (!value) {
    return null
  }
  const parsed = Date.parse(value)
  return Number.isFinite(parsed) ? parsed : null
}

function toMarketCode(value: string): string {
  const normalized = String(value || '').trim().toUpperCase()
  if (!normalized) {
    return 'US'
  }
  if (normalized.startsWith('SH')) {
    return 'SH'
  }
  if (normalized.startsWith('SZ')) {
    return 'SZ'
  }
  return normalized
}

function getMarketLocalTimeParts(timestampMs: number, market: string): { weekDay: number; minutes: number } | null {
  const marketCode = toMarketCode(market)
  const timeZone = MARKET_TIMEZONE[marketCode] || MARKET_TIMEZONE.US
  try {
    const formatter = new Intl.DateTimeFormat('en-US', {
      timeZone,
      weekday: 'short',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23'
    })
    const parts = formatter.formatToParts(new Date(timestampMs))
    const weekDayRaw = parts.find((item) => item.type === 'weekday')?.value || ''
    const hour = Number(parts.find((item) => item.type === 'hour')?.value || 0)
    const minute = Number(parts.find((item) => item.type === 'minute')?.value || 0)

    const weekMap: Record<string, number> = {
      Mon: 1,
      Tue: 2,
      Wed: 3,
      Thu: 4,
      Fri: 5,
      Sat: 6,
      Sun: 0
    }
    const weekDay = weekMap[weekDayRaw] ?? -1
    if (weekDay < 0 || !Number.isFinite(hour) || !Number.isFinite(minute)) {
      return null
    }

    return {
      weekDay,
      minutes: hour * 60 + minute
    }
  } catch {
    return null
  }
}

function isMarketOpenNow(market: string, atMs = Date.now()): boolean {
  const marketCode = toMarketCode(market)
  const sessions = MARKET_TRADING_SESSIONS[marketCode] || MARKET_TRADING_SESSIONS.US
  const local = getMarketLocalTimeParts(atMs, marketCode)
  if (!local) {
    return false
  }
  if (local.weekDay === 0 || local.weekDay === 6) {
    return false
  }

  return sessions.some((session) => local.minutes >= session.startMinute && local.minutes <= session.endMinute)
}

function getMeta(symbol: string): SymbolMeta {
  const normalized = symbolTicker(symbol)
  return SYMBOL_LIBRARY[normalized] || {
    name: `${normalized || normalizeSymbol(symbol)} Holdings`,
    market: 'US',
    exchange: 'NASDAQ'
  }
}

function buildQuote(symbolValue: string, at = Date.now()): StockQuote {
  const symbol = normalizeSymbol(symbolValue)
  const meta = getMeta(symbol)
  const baseSeed = hashString(symbol)
  const minuteSeed = Math.floor(at / 60000)
  const basePrice = 20 + (baseSeed % 420)
  const drift = (pseudo(baseSeed + minuteSeed) - 0.5) * 0.06
  const current = round(Math.max(0.5, basePrice * (1 + drift)))
  const prevDrift = (pseudo(baseSeed + minuteSeed - 1) - 0.5) * 0.06
  const previousClose = round(Math.max(0.5, basePrice * (1 + prevDrift)))
  const change = round(current - previousClose)
  const changePercent = previousClose ? round((change / previousClose) * 100) : 0
  const high = round(Math.max(current, previousClose) * (1 + pseudo(baseSeed + 11) * 0.015))
  const low = round(Math.min(current, previousClose) * (1 - pseudo(baseSeed + 19) * 0.015))
  const open = round((current + previousClose) / 2)
  const volume = Math.round(500000 + pseudo(baseSeed + minuteSeed + 29) * 9000000)
  const turnover = round(current * volume, 0)

  return {
    symbol,
    name: meta.name,
    current,
    previousClose,
    change,
    changePercent,
    high,
    low,
    open,
    volume,
    turnover,
    timestamp: new Date(at).toISOString()
  }
}

function buildStock(symbolValue: string): Stock {
  const symbol = normalizeSymbol(symbolValue)
  const meta = getMeta(symbol)
  const quote = buildQuote(symbol)
  const seed = hashString(symbol)
  const marketCap = round((40 + pseudo(seed + 3) * 900) * 1_000_000_000, 0)
  const high52Week = round(Math.max(quote.current, quote.previousClose) * (1.2 + pseudo(seed + 5) * 0.35))
  const low52Week = round(Math.min(quote.current, quote.previousClose) * (0.55 + pseudo(seed + 7) * 0.2))
  const avgVolume = round(quote.volume * (0.85 + pseudo(seed + 13) * 0.35), 0)
  const pe = round(12 + pseudo(seed + 17) * 45)
  const eps = round(Math.max(0.2, quote.current / Math.max(5, pe)), 2)
  const dividend = round(pseudo(seed + 23) * 3.2, 2)

  return {
    symbol,
    name: meta.name,
    exchange: meta.exchange,
    market: meta.market,
    currentPrice: quote.current,
    previousClose: quote.previousClose,
    open: quote.open,
    high: quote.high,
    low: quote.low,
    change: quote.change,
    changePercent: quote.changePercent,
    volume: quote.volume,
    marketCap,
    high52Week,
    low52Week,
    avgVolume,
    pe,
    eps,
    dividend,
    updatedAt: nowIso()
  }
}

function periodToMilliseconds(periodRaw: string): number {
  const period = String(periodRaw || '').trim().toUpperCase()
  switch (period) {
    case '1':
    case '1M':
      return 60 * 1000
    case '5':
    case '5M':
      return 5 * 60 * 1000
    case '15':
    case '15M':
      return 15 * 60 * 1000
    case '30':
    case '30M':
      return 30 * 60 * 1000
    case '60':
    case '60M':
    case '1H':
      return 60 * 60 * 1000
    case 'W':
    case '1W':
      return 7 * 24 * 60 * 60 * 1000
    case 'M':
    case '1MO':
      return 30 * 24 * 60 * 60 * 1000
    case 'Y':
    case '1Y':
      return 365 * 24 * 60 * 60 * 1000
    case 'D':
    case '1D':
    default:
      return 24 * 60 * 60 * 1000
  }
}

function buildKline(
  symbolValue: string,
  period = '1d',
  limit = 100,
  options?: { start?: string; end?: string }
): Candlestick[] {
  const symbol = normalizeSymbol(symbolValue)
  const seed = hashString(symbol)
  const step = periodToMilliseconds(period)
  const maxCount = Math.max(2, Math.min(Math.floor(limit || 100), 2000))
  const endTs = parseMaybeDate(options?.end) ?? Date.now()
  const startTsRaw = parseMaybeDate(options?.start)

  let count = maxCount
  let startTs = startTsRaw ?? (endTs - step * (count - 1))
  if (startTsRaw !== null && endTs > startTsRaw) {
    const possible = Math.floor((endTs - startTsRaw) / step) + 1
    count = Math.max(2, Math.min(maxCount, possible))
    startTs = startTsRaw
  }

  const rows: Candlestick[] = []
  let previousClose = buildQuote(symbol, startTs).previousClose
  const trendBase = (pseudo(seed + 101) - 0.5) * 0.0018

  for (let i = 0; i < count; i++) {
    const ts = startTs + i * step
    const localSeed = seed + i * 37 + Math.floor(startTs / step)
    const volatility = (pseudo(localSeed) - 0.5) * 0.03
    const drift = trendBase + volatility
    const open = round(previousClose)
    const close = round(Math.max(0.5, open * (1 + drift)))
    const high = round(Math.max(open, close) * (1 + pseudo(localSeed + 1) * 0.012))
    const low = round(Math.max(0.01, Math.min(open, close) * (1 - pseudo(localSeed + 2) * 0.012)))
    const volume = Math.round((600000 + pseudo(localSeed + 3) * 7000000) * (0.8 + Math.abs(drift) * 20))

    rows.push({
      time: new Date(ts).toISOString(),
      open,
      high,
      low,
      close,
      volume
    })
    previousClose = close
  }

  return rows
}

function normalizeLongBridgeBaseUrl(value: string): string {
  const raw = String(value || '').trim()
  if (!raw) {
    return 'https://openapi.longbridge.com'
  }

  try {
    const url = new URL(raw)
    const host = String(url.host || '').toLowerCase()

    if (host === 'open.longbridge.com') {
      return 'https://openapi.longbridge.com'
    }

    if (host === 'open.longbridge.cn') {
      return 'https://openapi.longbridge.cn'
    }

    if (host === 'openapi.longbridgeapp.com') {
      return 'https://openapi.longbridge.com'
    }

    if (host === 'openapi.longbridge.com' || host === 'openapi.longbridge.cn') {
      return `${url.protocol}//${url.host}`
    }
  } catch {
    return 'https://openapi.longbridge.com'
  }

  return 'https://openapi.longbridge.com'
}

function buildStockPlaceholder(
  symbolValue: string,
  options?: { name?: string; market?: string; exchange?: string; updatedAt?: string }
): Stock {
  const normalized = normalizeLookupSymbol(symbolValue)
  const displaySymbol = toDisplaySymbol(normalized)
  const meta = getMeta(displaySymbol)
  const market = String(options?.market || normalized.split('.').pop() || meta.market || 'US').toUpperCase()
  const exchange = String(options?.exchange || meta.exchange || '').trim() || undefined
  const name = String(options?.name || meta.name || displaySymbol).trim() || displaySymbol

  return {
    symbol: displaySymbol,
    name,
    exchange,
    market,
    currentPrice: Number.NaN,
    previousClose: Number.NaN,
    open: Number.NaN,
    high: Number.NaN,
    low: Number.NaN,
    change: Number.NaN,
    changePercent: Number.NaN,
    volume: 0,
    marketCap: Number.NaN,
    high52Week: Number.NaN,
    low52Week: Number.NaN,
    avgVolume: Number.NaN,
    pe: Number.NaN,
    eps: Number.NaN,
    dividend: Number.NaN,
    updatedAt: options?.updatedAt || nowIso()
  }
}

function securityToStock(item: DemoSecurity): Stock {
  const hasToken = hasLongBridgeToken(getState().config.longBridge)
  const detail = hasToken
    ? buildStockPlaceholder(item.symbol, {
      name: item.name,
      market: item.market
    })
    : buildStock(item.symbol)

  return {
    ...detail,
    symbol: toDisplaySymbol(item.symbol),
    name: item.name || detail.name,
    market: item.market || detail.market
  }
}

function buildLibraryUniverse(): DemoSecurity[] {
  return Object.entries(SYMBOL_LIBRARY).map(([ticker, meta]) => ({
    symbol: `${ticker}.US`,
    ticker,
    name: meta.name,
    market: 'US'
  }))
}

async function fetchLongBridgeSecurityList(
  config: LongBridgeLikeConfig,
  options?: { market?: string; category?: string }
): Promise<DemoSecurity[]> {
  const accessToken = String(config.accessToken || '').trim()
  const appKey = String(config.appKey || '').trim()
  const appSecret = String(config.appSecret || '').trim()
  if (!accessToken) {
    return []
  }

  const market = toMarketCode(options?.market || 'US')
  const category = String(options?.category || 'Overnight').trim() || 'Overnight'

  const response = await fetch('/api/longbridge/security-list', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      accessToken,
      appKey,
      appSecret,
      baseUrl: normalizeLongBridgeBaseUrl(config.baseUrl || ''),
      market,
      category
    })
  })

  const payload = await response.json().catch(() => ({})) as any
  if (!response.ok) {
    const message = String(payload?.message || `长桥接口请求失败(${response.status})`).trim()
    throw new Error(message || '长桥接口请求失败')
  }

  const list = Array.isArray(payload?.list) ? payload.list : []
  return list
    .map((item: any) => {
      const symbol = normalizeLookupSymbol(String(item?.symbol || ''))
      const ticker = symbolTicker(symbol)
      if (!symbol || !ticker) {
        return null
      }

      const market = String(item?.market || symbol.split('.').pop() || 'US').toUpperCase()
      const name = String(item?.name || ticker).trim()
      return {
        symbol,
        ticker,
        name: name || ticker,
        market
      } satisfies DemoSecurity
    })
    .filter(Boolean) as DemoSecurity[]
}

async function getSecurityUniverse(): Promise<DemoSecurity[]> {
  const config = getState().config.longBridge
  const token = String(config.accessToken || '').trim()
  const baseUrl = normalizeLongBridgeBaseUrl(config.baseUrl || '')
  const cacheKey = token ? `${baseUrl}:${token.slice(0, 16)}` : 'library-only'
  const now = Date.now()
  const cacheTtl = 10 * 60 * 1000

  if (securityUniverseCache && securityUniverseCache.cacheKey === cacheKey && now - securityUniverseCache.fetchedAt < cacheTtl) {
    return securityUniverseCache.items
  }

  let longBridgeRows: DemoSecurity[] = []
  if (token) {
    const marketRequests: Array<{ market: string; category: string }> = [
      { market: 'US', category: 'Overnight' },
      { market: 'HK', category: 'Overnight' },
      { market: 'SH', category: 'Overnight' },
      { market: 'SZ', category: 'Overnight' },
      { market: 'SG', category: 'Overnight' }
    ]

    const settled = await Promise.allSettled(
      marketRequests.map((request) => fetchLongBridgeSecurityList(config, request))
    )
    longBridgeRows = settled
      .filter((row): row is PromiseFulfilledResult<DemoSecurity[]> => row.status === 'fulfilled')
      .flatMap((row) => row.value)
  }

  const merged = token
    ? [...longBridgeRows, ...buildLibraryUniverse()]
    : [...buildLibraryUniverse()]
  const dedupedMap = new Map<string, DemoSecurity>()
  merged.forEach((item) => {
    const key = normalizeLookupSymbol(item.symbol)
    if (!key) {
      return
    }
    if (!dedupedMap.has(key)) {
      dedupedMap.set(key, {
        symbol: key,
        ticker: symbolTicker(key),
        name: String(item.name || symbolTicker(key)).trim() || symbolTicker(key),
        market: String(item.market || key.split('.').pop() || 'US').toUpperCase()
      })
    }
  })

  const items = Array.from(dedupedMap.values())
  securityUniverseCache = {
    cacheKey,
    fetchedAt: now,
    items
  }
  return items
}

function staticInfoToSecurity(item: DemoStaticInfo): DemoSecurity | null {
  const symbol = normalizeLookupSymbol(item.symbol)
  const ticker = symbolTicker(symbol)
  if (!symbol || !ticker) {
    return null
  }

  return {
    symbol,
    ticker,
    name: String(item.name || item.nameEn || item.nameCn || item.nameHk || ticker).trim() || ticker,
    market: String(item.market || symbol.split('.').pop() || 'US').toUpperCase()
  }
}

async function resolveSecurity(value: string): Promise<DemoSecurity | null> {
  const normalizedLookup = normalizeLookupSymbol(value)
  const normalizedTicker = symbolTicker(value)
  if (!normalizedLookup && !normalizedTicker) {
    return null
  }

  const universe = await getSecurityUniverse()
  const exact = universe.find((item) => item.symbol === normalizedLookup)
  if (exact) {
    return exact
  }

  const byTicker = universe.find((item) => item.ticker === normalizedTicker)
  if (byTicker) {
    return byTicker
  }

  const rawKeyword = String(value || '').trim()
  if (rawKeyword && !isLikelySymbolKeyword(rawKeyword)) {
    const byNameExact = universe.find((item) => String(item.name || '').trim() === rawKeyword)
    if (byNameExact) {
      return byNameExact
    }

    const keywordLower = rawKeyword.toLowerCase()
    const byNameContains = universe.find((item) => String(item.name || '').toLowerCase().includes(keywordLower))
    if (byNameContains) {
      return byNameContains
    }
  }

  const inferred = inferSecurityFromInput(value)
  const config = getState().config.longBridge
  if (!hasLongBridgeToken(config)) {
    return inferred
  }

  // get_security_list 静态接口目前主要覆盖美股夜盘列表。
  // A 股/港股等跨市场代码在这里先按合法代码格式放行。
  if (inferred && inferred.market !== 'US') {
    return inferred
  }

  try {
    const staticInfos = await fetchLongBridgeStaticInfos(config, [normalizedLookup || normalizedTicker])
    const [first] = staticInfos
    if (first) {
      return staticInfoToSecurity(first)
    }
  } catch {
    // Ignore static fetch errors and continue with inferred fallback.
  }

  return inferred
}

function hasLongBridgeToken(config: LongBridgeLikeConfig): boolean {
  return Boolean(String(config.accessToken || '').trim())
}

function toFiniteNumber(value: unknown, fallback = 0): number {
  const numeric = Number(value)
  return Number.isFinite(numeric) ? numeric : fallback
}

function buildQuoteFromRealtime(item: DemoRealtimeQuote): StockQuote {
  const lookupSymbol = normalizeLookupSymbol(item.symbol)
  const displaySymbol = toDisplaySymbol(lookupSymbol)
  const current = toFiniteNumber(item.current, Number.NaN)
  const previousClose = toFiniteNumber(item.previousClose, current)
  const change = toFiniteNumber(
    item.change,
    Number.isFinite(current) && Number.isFinite(previousClose) ? (current - previousClose) : Number.NaN
  )
  const changePercent = toFiniteNumber(
    item.changePercent,
    previousClose > 0 && Number.isFinite(change) ? (change / previousClose) * 100 : Number.NaN
  )
  const high = toFiniteNumber(item.high, current)
  const low = toFiniteNumber(item.low, current)
  const open = toFiniteNumber(item.open, current)
  const volume = Math.max(0, Math.round(toFiniteNumber(item.volume, 0)))
  const turnover = Math.max(0, toFiniteNumber(item.turnover, current * volume))
  const name = String(item.name || getMeta(displaySymbol).name || displaySymbol).trim() || displaySymbol

  return {
    symbol: displaySymbol,
    name,
    current: round(current),
    previousClose: round(previousClose),
    change: round(change),
    changePercent: round(changePercent),
    high: round(high),
    low: round(low),
    open: round(open),
    volume,
    turnover: round(turnover, 0),
    timestamp: item.timestamp || nowIso()
  }
}

function normalizeStaticInfo(item: any): DemoStaticInfo | null {
  const symbol = normalizeLookupSymbol(String(item?.symbol || ''))
  const ticker = symbolTicker(symbol)
  if (!symbol || !ticker) {
    return null
  }

  const market = String(item?.market || symbol.split('.').pop() || 'US').toUpperCase()
  const fallbackName = String(item?.name || item?.nameEn || item?.nameCn || item?.nameHk || ticker).trim() || ticker

  return {
    symbol,
    name: fallbackName,
    nameEn: String(item?.nameEn || '').trim() || undefined,
    nameCn: String(item?.nameCn || '').trim() || undefined,
    nameHk: String(item?.nameHk || '').trim() || undefined,
    market,
    exchange: String(item?.exchange || '').trim() || undefined,
    currency: String(item?.currency || '').trim() || undefined,
    lotSize: Math.max(0, Math.round(toFiniteNumber(item?.lotSize, 0))),
    totalShares: Math.max(0, toFiniteNumber(item?.totalShares, 0)),
    circulatingShares: Math.max(0, toFiniteNumber(item?.circulatingShares, 0)),
    hkShares: Math.max(0, toFiniteNumber(item?.hkShares, 0)),
    eps: toFiniteNumber(item?.eps, Number.NaN),
    epsTtm: toFiniteNumber(item?.epsTtm, Number.NaN),
    bps: toFiniteNumber(item?.bps, Number.NaN),
    dividendYield: toFiniteNumber(item?.dividendYield, Number.NaN),
    board: String(item?.board || '').trim() || undefined
  }
}

async function fetchLongBridgeStaticInfos(
  config: LongBridgeLikeConfig,
  symbols: string[]
): Promise<DemoStaticInfo[]> {
  if (!hasLongBridgeToken(config)) {
    return []
  }

  const accessToken = String(config.accessToken || '').trim()
  const appKey = String(config.appKey || '').trim()
  const appSecret = String(config.appSecret || '').trim()
  const normalizedSymbols = Array.from(
    new Set(
      symbols
        .map((item) => normalizeLookupSymbol(item))
        .filter(Boolean)
    )
  ).slice(0, 200)

  if (normalizedSymbols.length === 0) {
    return []
  }

  const response = await fetch('/api/longbridge/static', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      accessToken,
      appKey,
      appSecret,
      baseUrl: normalizeLongBridgeBaseUrl(config.baseUrl || ''),
      symbols: normalizedSymbols
    })
  })

  const payload = await response.json().catch(() => ({})) as any
  if (!response.ok) {
    const message = String(payload?.message || `长桥静态信息请求失败(${response.status})`).trim()
    throw new Error(message || '长桥静态信息请求失败')
  }

  const list = Array.isArray(payload?.list) ? payload.list : []
  return list
    .map(normalizeStaticInfo)
    .filter(Boolean) as DemoStaticInfo[]
}

async function fetchLongBridgeRealtimeQuotes(
  config: LongBridgeLikeConfig,
  symbols: string[]
): Promise<DemoRealtimeQuote[]> {
  if (!hasLongBridgeToken(config)) {
    return []
  }

  const accessToken = String(config.accessToken || '').trim()
  const appKey = String(config.appKey || '').trim()
  const appSecret = String(config.appSecret || '').trim()

  const normalizedSymbols = Array.from(
    new Set(
      symbols
        .map((item) => normalizeLookupSymbol(item))
        .filter(Boolean)
    )
  )

  if (normalizedSymbols.length === 0) {
    return []
  }

  const response = await fetch('/api/longbridge/quote', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      accessToken,
      appKey,
      appSecret,
      baseUrl: normalizeLongBridgeBaseUrl(config.baseUrl || ''),
      symbols: normalizedSymbols
    })
  })

  const payload = await response.json().catch(() => ({})) as any
  if (!response.ok) {
    const message = String(payload?.message || `长桥行情请求失败(${response.status})`).trim()
    throw new Error(message || '长桥行情请求失败')
  }

  if (typeof payload?.warning === 'string' && payload.warning.trim()) {
    console.warn('[longbridge-quote]', payload.warning)
  }

  const source = String(payload?.source || '').trim()
  const sourceIsRealtime = source === 'longbridge-realtime'
  const list = Array.isArray(payload?.list) ? payload.list : []
  return list
    .map((item: any) => ({
      symbol: String(item?.symbol || ''),
      name: String(item?.name || ''),
      current: toFiniteNumber(item?.current, Number.NaN),
      previousClose: toFiniteNumber(item?.previousClose, Number.NaN),
      open: toFiniteNumber(item?.open, Number.NaN),
      high: toFiniteNumber(item?.high, Number.NaN),
      low: toFiniteNumber(item?.low, Number.NaN),
      volume: toFiniteNumber(item?.volume, 0),
      turnover: toFiniteNumber(item?.turnover, 0),
      change: toFiniteNumber(item?.change, Number.NaN),
      changePercent: toFiniteNumber(item?.changePercent, Number.NaN),
      timestamp: String(item?.timestamp || ''),
      isRealtime: item?.isRealtime === undefined ? sourceIsRealtime : Boolean(item?.isRealtime),
      source
    } satisfies DemoRealtimeQuote))
}

function buildCompanyProfileFallback(symbol: string, nameHint?: string): CompanyProfile {
  const normalized = normalizeLookupSymbol(symbol)
  const display = toDisplaySymbol(normalized)
  const meta = getMeta(display)

  return {
    symbol: display,
    title: String(nameHint || meta.name || display).trim() || display,
    overview: '暂未获取到公司简介，可稍后重试或检查证券代码与市场后缀。',
    fields: [
      { key: '代码', value: normalized || display },
      { key: '市场', value: marketName(normalized.split('.').pop() || 'US') }
    ]
  }
}

async function fetchLongBridgeCompanyProfile(
  config: LongBridgeLikeConfig,
  symbol: string
): Promise<CompanyProfile> {
  const normalized = normalizeLookupSymbol(symbol)
  if (!normalized) {
    throw new Error('symbol is required')
  }

  const accessToken = String(config.accessToken || '').trim()
  const appKey = String(config.appKey || '').trim()
  const appSecret = String(config.appSecret || '').trim()

  const response = await fetch('/api/longbridge/company', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      symbol: normalized,
      accessToken,
      appKey,
      appSecret,
      baseUrl: normalizeLongBridgeBaseUrl(config.baseUrl || '')
    })
  })

  const payload = await response.json().catch(() => ({})) as any
  if (!response.ok) {
    const message = String(payload?.message || `公司信息请求失败(${response.status})`).trim()
    throw new Error(message || '公司信息请求失败')
  }

  return {
    symbol: String(payload?.symbol || toDisplaySymbol(normalized)),
    title: String(payload?.title || '').trim() || toDisplaySymbol(normalized),
    overview: String(payload?.overview || '').trim(),
    sourceUrl: typeof payload?.sourceUrl === 'string' ? payload.sourceUrl : undefined,
    fields: Array.isArray(payload?.fields)
      ? payload.fields
        .map((item: any) => ({
          key: String(item?.key || '').trim(),
          value: String(item?.value || '').trim()
        }))
        .filter((item: { key: string; value: string }) => item.key && item.value)
      : []
  }
}

async function getRealtimeQuotes(
  symbols: string[],
  options?: { requireRealtime?: boolean }
): Promise<StockQuote[]> {
  const normalizedSymbols = Array.from(new Set(symbols.map((item) => normalizeSymbol(item)).filter(Boolean)))
  if (normalizedSymbols.length === 0) {
    return []
  }

  const requireRealtime = options?.requireRealtime === true
  const config = getState().config.longBridge
  if (!hasLongBridgeToken(config)) {
    if (requireRealtime) {
      throw new Error('未配置长桥 Access Token，无法提供实时行情。')
    }
    return normalizedSymbols.map((symbol) => buildQuote(symbol))
  }

  const liveRows = await fetchLongBridgeRealtimeQuotes(config, normalizedSymbols)
  if (liveRows.length === 0) {
    throw new Error('长桥实时行情返回为空，请检查凭证权限或证券代码格式')
  }

  const strictRows = requireRealtime
    ? liveRows.filter((item) => item.isRealtime)
    : liveRows
  if (strictRows.length === 0) {
    throw new Error('当前账号仅返回关注快照，未返回实时成交行情，无法用于严格实时问答。')
  }

  const liveQuotes = strictRows.map(buildQuoteFromRealtime)

  const bySymbol = new Map<string, StockQuote>()
  liveQuotes.forEach((quote) => {
    const displaySymbol = normalizeSymbol(quote.symbol)
    if (displaySymbol) {
      bySymbol.set(displaySymbol, quote)
      const lookupSymbol = normalizeLookupSymbol(displaySymbol)
      if (lookupSymbol) {
        bySymbol.set(lookupSymbol, quote)
      }
      const ticker = symbolTicker(displaySymbol)
      if (ticker) {
        bySymbol.set(ticker, quote)
      }
    }
  })

  const rows = normalizedSymbols
    .map((symbol) => {
      const normalizedLookup = normalizeLookupSymbol(symbol)
      const ticker = symbolTicker(symbol)
      return bySymbol.get(symbol) || bySymbol.get(normalizedLookup) || bySymbol.get(ticker) || null
    })
    .filter((item): item is StockQuote => {
      if (!item) {
        return false
      }
      return Number.isFinite(item.current) && item.current > 0
    })

  if (rows.length === 0) {
    throw new Error('未匹配到有效实时行情，请检查证券代码格式')
  }

  return rows
}

function resolveChatCompletionsEndpoint(baseUrl: string): string {
  const trimmed = String(baseUrl || '').trim()
  if (!trimmed) {
    return 'https://api.openai.com/v1/chat/completions'
  }

  const normalized = trimmed.replace(/\/+$/, '')
  if (/\/chat\/completions$/i.test(normalized)) {
    return normalized
  }

  return `${normalized}/chat/completions`
}

function parseModelCandidates(raw: string): string[] {
  const models = String(raw || '')
    .split(/[\n,;|]+/g)
    .map((item) => item.trim())
    .filter(Boolean)

  return Array.from(new Set(models))
}

function createAiProviderId(index = 0): string {
  const suffix = Math.random().toString(36).slice(2, 8)
  return `provider-${Date.now()}-${index}-${suffix}`
}

function normalizeAiProvider(input: Partial<AiProviderConfig>, index = 0): AiProviderConfig {
  const baseUrl = String(input.baseUrl || '').trim() || 'https://api.openai.com/v1'
  const model = String(input.model || '').trim() || 'gpt-5-mini'
  const id = String(input.id || '').trim() || createAiProviderId(index)
  const name = String(input.name || '').trim() || `模型源 ${index + 1}`

  return {
    id,
    name,
    apiKey: String(input.apiKey || '').trim(),
    baseUrl,
    model
  }
}

function getAiProviders(config: OpenAiLikeConfig): AiProviderConfig[] {
  const providers = Array.isArray((config as any)?.providers)
    ? ((config as any).providers as Array<Partial<AiProviderConfig>>)
      .map((item, index) => normalizeAiProvider(item, index))
      .filter((item) => item.id)
    : []

  if (providers.length > 0) {
    return providers
  }

  return [
    normalizeAiProvider({
      id: 'default',
      name: '默认模型源',
      apiKey: config.apiKey,
      baseUrl: config.baseUrl,
      model: config.model
    })
  ]
}

function pickAiProvider(
  config: OpenAiLikeConfig,
  preferredProviderId?: string
): AiProviderConfig {
  const providers = getAiProviders(config)
  const preferredId = String(preferredProviderId || '').trim()
    || String((config as any)?.activeProviderId || '').trim()

  const matched = preferredId
    ? providers.find((item) => item.id === preferredId)
    : null

  return matched || providers[0]
}

function movingAverage(values: number[], period: number): number | null {
  if (values.length < period || period <= 0) {
    return null
  }
  const slice = values.slice(values.length - period)
  const sum = slice.reduce((acc, item) => acc + item, 0)
  return round(sum / period, 2)
}

async function requestOpenAiLikeCompletion(
  provider: Pick<AiProviderConfig, 'apiKey' | 'baseUrl' | 'model'>,
  userPrompt: string,
  options?: { maxTokens?: number; temperature?: number; modelOverride?: string }
): Promise<{ model: string; content: string }> {
  const apiKey = String(provider.apiKey || '').trim()
  const modelOverride = String(options?.modelOverride || '').trim()
  const models = modelOverride
    ? [modelOverride]
    : parseModelCandidates(provider.model || '')

  if (!apiKey) {
    throw new Error('请先配置 OpenAI API Key')
  }

  if (models.length === 0) {
    throw new Error('请先配置至少一个 AI 模型')
  }

  const endpoint = resolveChatCompletionsEndpoint(provider.baseUrl || 'https://api.openai.com/v1')
  const errors: string[] = []
  const tokenBudget = Math.max(256, Math.min(3200, Math.floor(options?.maxTokens ?? 1500)))

  for (const model of models) {
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), 30000)
    let disableMaxTokens = false

    try {
      while (true) {
        const payloadBody: Record<string, any> = {
          model,
          temperature: options?.temperature ?? 0.4,
          messages: [
            {
              role: 'system',
              content: '你是专业的量化交易分析助手。输出简洁、结构化、面向实战，并始终提示风险。'
            },
            {
              role: 'user',
              content: userPrompt
            }
          ]
        }

        if (!disableMaxTokens) {
          payloadBody.max_tokens = tokenBudget
        }

        const response = await fetch(endpoint, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${apiKey}`
          },
          body: JSON.stringify(payloadBody),
          signal: controller.signal
        })

        if (!response.ok) {
          const raw = await response.text()
          if (
            !disableMaxTokens &&
            /max_tokens/i.test(raw) &&
            /max_completion_tokens/i.test(raw) &&
            /cannot both be set/i.test(raw)
          ) {
            disableMaxTokens = true
            continue
          }

          const message = raw.slice(0, 220)
          errors.push(`${model}: 接口错误(${response.status}) ${message}`)
          break
        }

        const payload = await response.json() as any
        const content = String(payload?.choices?.[0]?.message?.content || '').trim()
        if (!content) {
          errors.push(`${model}: 返回内容为空`)
          break
        }

        return {
          model: String(payload?.model || model),
          content
        }
      }
    } catch (error: any) {
      if (error?.name === 'AbortError') {
        errors.push(`${model}: 请求超时`)
      } else if (typeof error?.message === 'string' && error.message.trim()) {
        errors.push(`${model}: ${error.message}`)
      } else {
        errors.push(`${model}: 调用失败`)
      }
    } finally {
      clearTimeout(timer)
    }
  }

  throw new Error(`所有模型调用失败：${errors.join(' | ')}`)
}

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}

function requireValue<T>(value: T | null | undefined, message: string): NonNullable<T> {
  if (value === null || value === undefined) {
    throw new Error(message)
  }
  return value as NonNullable<T>
}

function buildAiAnalysisPrompt(
  symbol: string,
  payload?: {
    period?: string
    start?: string
    end?: string
    count?: number
    focus?: string
  }
): string {
  const period = String(payload?.period || 'D').toUpperCase()
  const count = Math.max(60, Math.min(Number(payload?.count || 240), 320))
  const periodForKline = period === 'Y' ? 'M' : period
  const quote = buildQuote(symbol)
  const rows = buildKline(symbol, periodForKline, count, {
    start: payload?.start,
    end: payload?.end
  })

  const closes = rows.map((item) => item.close)
  const highs = rows.map((item) => item.high)
  const lows = rows.map((item) => item.low)
  const volumes = rows.map((item) => item.volume)
  const ma5 = movingAverage(closes, 5)
  const ma20 = movingAverage(closes, 20)
  const high = highs.length > 0 ? round(Math.max(...highs), 2) : quote.high
  const low = lows.length > 0 ? round(Math.min(...lows), 2) : quote.low
  const avgVolume = volumes.length > 0
    ? Math.round(volumes.reduce((sum, value) => sum + value, 0) / volumes.length)
    : quote.volume

  const focus = String(payload?.focus || '').trim() || '请重点分析趋势延续性、关键支撑压力位和风险控制。'

  return [
    `请分析股票 ${symbol}，并输出简洁的中文 Markdown。`,
    '',
    '输出要求：',
    '1. 必须包含三个三级标题：### AI 观察结论、### 交易思路、### 风险提示',
    '2. 每个部分使用 2-4 条要点，避免空话',
    '3. 明确提示这不是投资建议',
    '',
    '行情输入：',
    `- 周期：${period}`,
    `- 最新价：${quote.current}`,
    `- 涨跌幅：${quote.changePercent}%`,
    `- 区间高低：${low} ~ ${high}`,
    `- MA5：${ma5 ?? '无'}`,
    `- MA20：${ma20 ?? '无'}`,
    `- 平均成交量：${avgVolume}`,
    `- 最新成交量：${quote.volume}`,
    '',
    `关注点：${focus}`
  ].join('\n')
}

function isRealtimeSensitiveQuestion(question: string): boolean {
  const normalized = String(question || '').trim().toLowerCase()
  if (!normalized) {
    return false
  }
  return REALTIME_QUESTION_KEYWORDS.some((keyword) => normalized.includes(String(keyword).toLowerCase()))
}

function extractChineseNameCandidates(question: string): string[] {
  const rows = (String(question || '').match(/[\u4e00-\u9fff]{2,18}/g) || [])
    .map((item) => item.trim())
    .filter(Boolean)
    .map((item) => {
      let next = item
      ;[
        '股票', '股价', '价格', '行情', '走势', '分析', '最新', '实时', '现在', '获取', '帮我', '请问', '一下',
        '看看', '查询', '多少', '的', '股', '一只', '标的', '美股', '港股', 'A股', 'a股', '今日', '今天', '当前'
      ].forEach((noise) => {
        next = next.split(noise).join('')
      })
      return next.trim()
    })
    .filter((item) => item.length >= 2 && item.length <= 10)

  return Array.from(new Set(rows)).slice(0, 6)
}

async function resolveChatTargetSymbol(inputSymbol: string | undefined, question: string): Promise<DemoSecurity | null> {
  const direct = String(inputSymbol || '').trim()
  if (direct) {
    const resolved = await resolveSecurity(direct)
    if (resolved) {
      return resolved
    }
  }

  const symbolTokens = Array.from(
    new Set(
      (String(question || '').toUpperCase().match(/\b(?:[A-Z]{1,5}\.(?:US|HK|SG)|\d{5}\.HK|\d{6}\.(?:SH|SZ)|(?:SH|SZ)\d{6}|[A-Z]{1,5}|\d{5}|\d{6})\b/g) || [])
        .map((item) => item.trim())
        .filter((item) => item.length > 1)
    )
  ).slice(0, 8)

  for (const token of symbolTokens) {
    const resolved = await resolveSecurity(token)
    if (resolved) {
      return resolved
    }
  }

  for (const candidate of extractChineseNameCandidates(question)) {
    const resolved = await resolveSecurity(candidate)
    if (resolved) {
      return resolved
    }
  }

  return null
}

function buildChatMarketContext(quote: StockQuote, market: string): AiChatMarketContext {
  const quoteTime = String(quote.timestamp || '').trim()
  const quoteTimeMs = Date.parse(quoteTime)
  if (!Number.isFinite(quoteTimeMs)) {
    throw new Error('行情时间戳不可用，无法判定实时性。')
  }

  const marketCode = toMarketCode(String(market || normalizeLookupSymbol(quote.symbol).split('.').pop() || 'US'))
  const marketOpen = isMarketOpenNow(marketCode)
  const lagSecondsRaw = Math.round((Date.now() - quoteTimeMs) / 1000)
  const lagSeconds = Math.max(0, lagSecondsRaw)

  let freshness: AiChatMarketContext['freshness'] = 'delayed_close'
  if (marketOpen) {
    freshness = lagSeconds <= REALTIME_STALE_THRESHOLD_SECONDS ? 'realtime' : 'stale'
  }

  return {
    symbol: normalizeLookupSymbol(quote.symbol),
    market: marketCode,
    price: Number(quote.current || 0),
    changePercent: Number(quote.changePercent || 0),
    quoteTime,
    lagSeconds,
    marketOpen,
    freshness,
    source: 'longbridge'
  }
}

function buildStrictChatPrompt(question: string, focus: string, marketContext?: AiChatMarketContext): string {
  const rows = [
    '你是专业交易助手，请使用简洁中文回答。',
    '若涉及价格判断，必须只使用下面给出的实时行情，不得引用训练记忆历史价格。',
    ''
  ]

  if (marketContext) {
    rows.push(
      '实时行情上下文（可信数据源）：',
      `- 标的：${marketContext.symbol}`,
      `- 市场：${marketContext.market}`,
      `- 最新价：${marketContext.price}`,
      `- 涨跌幅：${marketContext.changePercent}%`,
      `- 行情时间(UTC)：${marketContext.quoteTime}`,
      `- 时延：${marketContext.lagSeconds} 秒`,
      `- 新鲜度：${marketContext.freshness}`,
      `- 来源：${marketContext.source}`,
      ''
    )
  }

  if (focus) {
    rows.push(`关注点：${focus}`, '')
  }

  rows.push(
    `用户问题：${question}`,
    '',
    '请按“结论 / 依据 / 风险提示”结构回复，并明确“不构成投资建议”。'
  )

  return rows.join('\n')
}

function withStockInfo(item: WatchlistItem): WatchlistItem {
  const stock = buildStock(item.symbol)
  return {
    ...item,
    name: item.name || stock.name,
    stock
  }
}

function withStockPlaceholder(item: WatchlistItem): WatchlistItem {
  return {
    ...item,
    stock: undefined
  }
}

function initialWatchlist(): WatchlistItem[] {
  const now = Date.now()
  return DEFAULT_SYMBOLS.map((symbol, index) => {
    const meta = getMeta(symbol)
    return {
      id: index + 1,
      symbol,
      name: meta.name,
      notes: index === 0 ? '核心观察标的' : '',
      addedAt: new Date(now - index * 24 * 60 * 60 * 1000).toISOString(),
      stock: buildStock(symbol)
    }
  })
}

function initialStrategies(): Strategy[] {
  const now = nowIso()
  const cond1: StrategyCondition = {
    id: 'cond-1',
    type: 'price_change_percent',
    params: { value: 2.5 },
    operator: 'and'
  }
  const cond2: StrategyCondition = {
    id: 'cond-2',
    type: 'volume_above',
    params: { value: 1200000 },
    operator: 'and'
  }
  const action1: StrategyAction = {
    id: 'action-1',
    type: 'notify_email',
    params: { message: '价格放量突破，关注入场机会' }
  }
  const action2: StrategyAction = {
    id: 'action-2',
    type: 'buy',
    params: { quantity: 100, priceType: 'market' }
  }

  return [
    {
      id: 1,
      name: '突破确认策略',
      description: '价格涨幅与成交量同时放大时触发',
      config: {
        conditions: [cond1, cond2],
        actions: [action1, action2],
        targetSymbols: ['AAPL', 'NVDA', 'MSFT'],
        checkInterval: 300
      },
      isActive: true,
      createdAt: now,
      updatedAt: now,
      lastExecutedAt: now
    },
    {
      id: 2,
      name: '均线回踩观察',
      description: '中期趋势中回踩后确认反弹',
      config: {
        conditions: [
          {
            id: 'cond-3',
            type: 'ma_cross_up',
            params: { shortPeriod: 5, longPeriod: 20 },
            operator: 'and'
          }
        ],
        actions: [
          {
            id: 'action-3',
            type: 'notify_feishu',
            params: { message: '均线金叉出现，等待确认' }
          }
        ],
        targetSymbols: ['TSLA', 'AMZN'],
        checkInterval: 900
      },
      isActive: false,
      createdAt: now,
      updatedAt: now
    }
  ]
}

function initialTrades(): Trade[] {
  const now = Date.now()
  const sample: Array<Pick<Trade, 'symbol' | 'side' | 'quantity' | 'price' | 'strategyId' | 'strategyName'>> = [
    { symbol: 'AAPL', side: 'buy', quantity: 50, price: 182.3, strategyId: 1, strategyName: '突破确认策略' },
    { symbol: 'NVDA', side: 'buy', quantity: 20, price: 864.8, strategyId: 1, strategyName: '突破确认策略' },
    { symbol: 'MSFT', side: 'sell', quantity: 10, price: 418.2, strategyId: 1, strategyName: '突破确认策略' },
    { symbol: 'TSLA', side: 'buy', quantity: 30, price: 189.4, strategyId: 2, strategyName: '均线回踩观察' },
    { symbol: 'AMZN', side: 'sell', quantity: 15, price: 178.1, strategyId: 2, strategyName: '均线回踩观察' }
  ]

  return sample.map((item, index) => {
    const amount = round(item.price * item.quantity)
    return {
      id: index + 1,
      symbol: item.symbol,
      stockName: getMeta(item.symbol).name,
      side: item.side,
      quantity: item.quantity,
      price: item.price,
      amount,
      commission: round(amount * 0.0005),
      status: index === 0 ? 'pending' : 'filled',
      orderId: `SIM-${1000 + index}`,
      strategyId: item.strategyId,
      strategyName: item.strategyName,
      executedAt: new Date(now - index * 3 * 60 * 60 * 1000).toISOString(),
      createdAt: new Date(now - index * 3.2 * 60 * 60 * 1000).toISOString()
    }
  })
}

function generateEquityCurve(
  initialCapital: number,
  startDate: string,
  endDate: string,
  totalReturn: number,
  seed: number
): Array<{ date: string; equity: number; drawdown: number }> {
  const startTs = parseMaybeDate(startDate) ?? Date.now() - 60 * 24 * 60 * 60 * 1000
  const endTs = parseMaybeDate(endDate) ?? Date.now()
  const points = 60
  const rows: Array<{ date: string; equity: number; drawdown: number }> = []
  let peak = initialCapital

  for (let i = 0; i < points; i++) {
    const progress = i / (points - 1)
    const ts = startTs + (endTs - startTs) * progress
    const noise = (pseudo(seed + i * 13) - 0.5) * 0.03
    const pnlRatio = totalReturn * progress + noise
    const equity = round(Math.max(initialCapital * 0.6, initialCapital * (1 + pnlRatio)))
    peak = Math.max(peak, equity)
    const drawdown = peak > 0 ? round((peak - equity) / peak, 4) : 0
    rows.push({
      date: new Date(ts).toISOString(),
      equity,
      drawdown
    })
  }

  return rows
}

function generateBacktestTrades(seed: number, startDate: string, endDate: string): Array<{
  symbol: string
  side: 'buy' | 'sell'
  price: number
  quantity: number
  date: string
  profit?: number
}> {
  const symbols = ['AAPL', 'MSFT', 'NVDA', 'TSLA', 'AMZN']
  const startTs = parseMaybeDate(startDate) ?? Date.now() - 30 * 24 * 60 * 60 * 1000
  const endTs = parseMaybeDate(endDate) ?? Date.now()
  const count = 14
  const rows: Array<{
    symbol: string
    side: 'buy' | 'sell'
    price: number
    quantity: number
    date: string
    profit?: number
  }> = []

  for (let i = 0; i < count; i++) {
    const localSeed = seed + i * 17
    const symbol = symbols[Math.floor(pseudo(localSeed) * symbols.length)]
    const side = pseudo(localSeed + 1) > 0.45 ? 'buy' : 'sell'
    const quantity = Math.round(10 + pseudo(localSeed + 2) * 140)
    const price = round(40 + pseudo(localSeed + 3) * 880)
    const profit = round((pseudo(localSeed + 4) - 0.45) * quantity * price * 0.08)
    const ts = startTs + ((endTs - startTs) * i) / Math.max(1, count - 1)

    rows.push({
      symbol,
      side,
      price,
      quantity,
      date: new Date(ts).toISOString(),
      profit
    })
  }

  return rows
}

function createBacktestFromInput(input: {
  id: number
  strategyId: number
  strategyName: string
  startDate: string
  endDate: string
  initialCapital: number
}): Backtest {
  const seed = hashString(`${input.strategyId}-${input.startDate}-${input.endDate}-${input.id}`)
  const totalReturn = round((pseudo(seed) - 0.35) * 0.42, 4)
  const annualizedReturn = round(totalReturn * (1.3 + pseudo(seed + 1) * 0.5), 4)
  const maxDrawdown = round(0.04 + pseudo(seed + 2) * 0.18, 4)
  const sharpeRatio = round(0.8 + pseudo(seed + 3) * 1.8, 2)
  const winRate = round(0.42 + pseudo(seed + 4) * 0.33, 4)
  const totalTrades = 22 + Math.floor(pseudo(seed + 5) * 48)
  const profitTrades = Math.round(totalTrades * winRate)
  const lossTrades = Math.max(0, totalTrades - profitTrades)
  const finalCapital = round(input.initialCapital * (1 + totalReturn))
  const equityCurve = generateEquityCurve(
    input.initialCapital,
    input.startDate,
    input.endDate,
    totalReturn,
    seed + 7
  )

  return {
    id: input.id,
    strategyId: input.strategyId,
    strategyName: input.strategyName,
    startDate: input.startDate,
    endDate: input.endDate,
    initialCapital: input.initialCapital,
    finalCapital,
    totalReturn,
    annualizedReturn,
    maxDrawdown,
    sharpeRatio,
    winRate,
    totalTrades,
    profitTrades,
    lossTrades,
    status: 'completed',
    equityCurve,
    trades: generateBacktestTrades(seed + 11, input.startDate, input.endDate),
    createdAt: nowIso(),
    completedAt: nowIso()
  }
}

function initialBacktests(strategies: Strategy[]): Backtest[] {
  const primary = strategies[0]
  if (!primary) {
    return []
  }

  return [
    createBacktestFromInput({
      id: 1,
      strategyId: primary.id,
      strategyName: primary.name,
      startDate: new Date(Date.now() - 120 * 24 * 60 * 60 * 1000).toISOString(),
      endDate: nowIso(),
      initialCapital: 100000
    })
  ]
}

function initialReviews(trades: Trade[]): ReviewRecord[] {
  const today = new Date()
  const yesterday = new Date(Date.now() - 24 * 60 * 60 * 1000)
  return [
    {
      id: 1,
      date: today.toISOString().slice(0, 10),
      marketSummary: '科技股延续分化，AI 产业链表现相对强势，指数震荡上行。',
      trades: clone(trades.slice(0, 2)),
      notes: '上午追涨节奏偏快，午后控制仓位后波动明显降低。',
      lessons: '高波动日需先设定单笔风险上限，避免情绪化加仓。',
      tags: ['趋势', '波动', '风控'],
      createdAt: nowIso()
    },
    {
      id: 2,
      date: yesterday.toISOString().slice(0, 10),
      marketSummary: '大盘缩量整理，资金集中在少数龙头。',
      trades: clone(trades.slice(2, 4)),
      notes: '策略触发较少，手动干预偏多。',
      lessons: '回测参数需要分市场状态动态调整。',
      tags: ['震荡', '复盘'],
      createdAt: nowIso()
    }
  ]
}

function initialRules(): MonitorRule[] {
  return [
    {
      id: 1,
      name: '突破提醒',
      symbols: ['AAPL', 'NVDA'],
      conditions: [
        { type: 'price_above', operator: 'gt', value: 180 },
        { type: 'volume_above', operator: 'gt', value: 1000000 }
      ],
      notifications: [
        { type: 'email', enabled: true },
        { type: 'feishu', enabled: false }
      ],
      checkInterval: 300,
      isActive: true,
      lastTriggeredAt: nowIso(),
      createdAt: nowIso()
    }
  ]
}

function initialConfig(): SystemConfig {
  return {
    longBridge: {
      appKey: '',
      appSecret: '',
      accessToken: '',
      baseUrl: 'https://openapi.longbridge.com'
    },
    proxy: {
      enabled: false,
      host: '127.0.0.1',
      port: 7890,
      username: '',
      password: ''
    },
    email: {
      enabled: false,
      smtpHost: 'smtp.example.com',
      smtpPort: 587,
      username: '',
      password: '',
      fromAddress: '',
      toAddresses: [],
      useSsl: true
    },
    feishu: {
      enabled: false,
      webhookUrl: '',
      signSecret: ''
    },
    wechat: {
      enabled: false,
      webhookUrl: ''
    },
    openAi: {
      enabled: false,
      apiKey: '',
      baseUrl: 'https://api.openai.com/v1',
      model: 'gpt-5-mini',
      providers: [
        {
          id: 'default',
          name: '默认模型源',
          apiKey: '',
          baseUrl: 'https://api.openai.com/v1',
          model: 'gpt-5-mini'
        }
      ],
      activeProviderId: 'default'
    }
  }
}

function createDefaultState(): DemoState {
  const strategies = initialStrategies()
  const trades = initialTrades()
  return {
    watchlist: initialWatchlist(),
    monitorRules: initialRules(),
    strategies,
    trades,
    backtests: initialBacktests(strategies),
    reviews: initialReviews(trades),
    config: initialConfig()
  }
}

function hasStorage(): boolean {
  return typeof window !== 'undefined' && typeof window.localStorage !== 'undefined'
}

function mergeConfig(current: SystemConfig, patch: Partial<SystemConfig>): SystemConfig {
  const mergedOpenAi = {
    ...current.openAi,
    ...(patch.openAi || {})
  } as OpenAiLikeConfig
  const providers = getAiProviders(mergedOpenAi)
  const preferredProviderId = String(mergedOpenAi.activeProviderId || '').trim()
  const activeProvider = providers.find((item) => item.id === preferredProviderId) || providers[0]

  return {
    ...current,
    ...patch,
    longBridge: {
      ...current.longBridge,
      ...(patch.longBridge || {})
    },
    proxy: {
      ...current.proxy,
      ...(patch.proxy || {})
    },
    email: {
      ...current.email,
      ...(patch.email || {})
    },
    feishu: {
      ...current.feishu,
      ...(patch.feishu || {})
    },
    wechat: {
      ...current.wechat,
      ...(patch.wechat || {})
    },
    openAi: {
      ...mergedOpenAi,
      providers,
      activeProviderId: activeProvider?.id || '',
      apiKey: activeProvider?.apiKey || '',
      baseUrl: activeProvider?.baseUrl || 'https://api.openai.com/v1',
      model: activeProvider?.model || 'gpt-5-mini'
    }
  }
}

function loadStateFromStorage(): DemoState | null {
  if (!hasStorage()) {
    return null
  }

  const raw = window.localStorage.getItem(STORAGE_KEY)
  if (!raw) {
    return null
  }

  try {
    const parsed = JSON.parse(raw) as Partial<DemoState>
    const defaults = createDefaultState()
    return {
      watchlist: Array.isArray(parsed.watchlist) ? parsed.watchlist : defaults.watchlist,
      monitorRules: Array.isArray(parsed.monitorRules) ? parsed.monitorRules : defaults.monitorRules,
      strategies: Array.isArray(parsed.strategies) ? parsed.strategies : defaults.strategies,
      trades: Array.isArray(parsed.trades) ? parsed.trades : defaults.trades,
      backtests: Array.isArray(parsed.backtests) ? parsed.backtests : defaults.backtests,
      reviews: Array.isArray(parsed.reviews) ? parsed.reviews : defaults.reviews,
      config: mergeConfig(defaults.config, parsed.config || {})
    }
  } catch {
    return null
  }
}

function saveStateToStorage(state: DemoState): void {
  if (!hasStorage()) {
    return
  }
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(state))
}

function getState(): DemoState {
  if (cachedState) {
    return cachedState
  }

  cachedState = loadStateFromStorage() || createDefaultState()
  saveStateToStorage(cachedState)
  return cachedState
}

function updateState(mutator: (state: DemoState) => void): DemoState {
  const state = getState()
  mutator(state)
  saveStateToStorage(state)
  return state
}

function nextId(rows: Array<{ id: number }>): number {
  return rows.reduce((max, row) => Math.max(max, Number(row.id) || 0), 0) + 1
}

function normalizeCondition(raw: any): MonitorCondition {
  return {
    type: String(raw?.type || 'price_above'),
    operator: ['gt', 'lt', 'eq', 'gte', 'lte'].includes(raw?.operator) ? raw.operator : 'gt',
    value: Number(raw?.value ?? 0)
  }
}

function normalizeNotifications(raw: any): NotificationChannel[] {
  if (!Array.isArray(raw) || raw.length === 0) {
    return [{ type: 'email', enabled: true }]
  }

  const mapped = raw
    .map((item) => {
      if (typeof item === 'string') {
        if (item === 'email' || item === 'feishu' || item === 'wechat') {
          return { type: item, enabled: true } as NotificationChannel
        }
        return null
      }

      const type = String(item?.type || '')
      if (type !== 'email' && type !== 'feishu' && type !== 'wechat') {
        return null
      }

      return {
        type,
        enabled: item?.enabled !== false
      } as NotificationChannel
    })
    .filter(Boolean) as NotificationChannel[]

  return mapped.length > 0 ? mapped : [{ type: 'email', enabled: true }]
}

function normalizeStrategyConfig(raw: any): Strategy['config'] {
  const conditions = Array.isArray(raw?.conditions) ? raw.conditions : []
  const actions = Array.isArray(raw?.actions) ? raw.actions : []
  const targetSymbols = Array.isArray(raw?.targetSymbols)
    ? raw.targetSymbols.map((item: unknown) => normalizeSymbol(String(item || ''))).filter(Boolean)
    : []
  const checkInterval = Number(raw?.checkInterval ?? 300)

  return {
    conditions,
    actions,
    targetSymbols,
    checkInterval: Number.isFinite(checkInterval) && checkInterval > 0 ? checkInterval : 300
  }
}

function buildAccountSummary(trades: Trade[]) {
  const filled = trades.filter((item) => item.status === 'filled')
  const turnover = filled.reduce((sum, item) => sum + Number(item.amount || 0), 0)
  const realizedPnL = round(
    filled.reduce((sum, item, index) => sum + (index % 2 === 0 ? 1 : -1) * Number(item.amount || 0) * 0.015, 0)
  )
  const unrealizedPnL = round(realizedPnL * 0.35)
  const cash = round(220000 - turnover * 0.05 + realizedPnL)
  const totalAssets = round(cash + turnover * 0.12 + 180000)

  return {
    cash,
    totalAssets,
    realizedPnL,
    unrealizedPnL
  }
}

function applyTradeFilters(
  rows: Trade[],
  params?: {
    page?: number
    pageSize?: number
    symbol?: string
    side?: string
    status?: string
    startDate?: string
    endDate?: string
  }
): PagedResult<Trade> {
  const keyword = String(params?.symbol || '').trim().toLowerCase()
  const start = parseMaybeDate(params?.startDate)
  const end = parseMaybeDate(params?.endDate)

  const filtered = rows.filter((item) => {
    if (keyword) {
      const symbolMatch = String(item.symbol || '').toLowerCase().includes(keyword)
      const nameMatch = String(item.stockName || '').toLowerCase().includes(keyword)
      if (!symbolMatch && !nameMatch) {
        return false
      }
    }

    if (params?.side && item.side !== params.side) {
      return false
    }

    if (params?.status && item.status !== params.status) {
      return false
    }

    const executed = Date.parse(item.executedAt)
    if (start !== null && Number.isFinite(executed) && executed < start) {
      return false
    }

    if (end !== null && Number.isFinite(executed) && executed > end) {
      return false
    }

    return true
  })

  const sorted = [...filtered].sort((a, b) => Date.parse(b.executedAt) - Date.parse(a.executedAt))
  const page = Math.max(1, Number(params?.page || 1))
  const pageSize = Math.max(1, Number(params?.pageSize || 20))
  const startIndex = (page - 1) * pageSize
  const items = sorted.slice(startIndex, startIndex + pageSize)

  return {
    items: clone(items),
    total: sorted.length,
    page,
    pageSize,
    totalPages: Math.max(1, Math.ceil(sorted.length / pageSize))
  }
}

export const demoApi = {
  stockApi: {
    async search(query: string): Promise<Stock[]> {
      const keyword = String(query || '').trim().toLowerCase()
      if (!keyword) {
        return []
      }

      const universe = await getSecurityUniverse()
      const rows = universe
        .filter((item) => {
          return item.ticker.toLowerCase().includes(keyword) || item.name.toLowerCase().includes(keyword)
        })
        .slice(0, 20)

      const normalizedQuery = normalizeSymbol(query)
      if (isLikelySymbolKeyword(normalizedQuery)) {
        const inferred = inferSecurityFromInput(normalizedQuery)
        if (inferred) {
          const exists = rows.some((item) => item.symbol === inferred.symbol || item.ticker === inferred.ticker)
          if (!exists) {
            rows.unshift(inferred)
          }
        }
      }

      return clone(rows.slice(0, 20).map(securityToStock))
    },

    async getQuote(symbol: string): Promise<StockQuote> {
      const quotes = await getRealtimeQuotes([symbol])
      const first = quotes[0]
      if (first) {
        return first
      }

      if (hasLongBridgeToken(getState().config.longBridge)) {
        throw new Error('长桥实时行情为空，请检查 Token 权限或证券代码格式')
      }

      return buildQuote(symbol)
    },

    async getQuotes(symbols: string[]): Promise<StockQuote[]> {
      return getRealtimeQuotes(symbols)
    },

    async getKline(
      symbol: string,
      period = '1d',
      limit = 100,
      options?: { start?: string; end?: string }
    ): Promise<Candlestick[]> {
      return buildKline(symbol, period, limit, options)
    },

    async getDetail(symbol: string): Promise<Stock> {
      const config = getState().config.longBridge
      const resolved = await resolveSecurity(symbol)
      const targetSymbol = resolved?.symbol || normalizeLookupSymbol(symbol)
      const displaySymbol = toDisplaySymbol(targetSymbol)
      const staticInfos = hasLongBridgeToken(config)
        ? await fetchLongBridgeStaticInfos(config, [targetSymbol]).catch(() => [])
        : []
      const staticInfo = staticInfos[0]

      const baseStock = hasLongBridgeToken(config)
        ? buildStockPlaceholder(targetSymbol, {
          name: staticInfo?.name || resolved?.name,
          market: staticInfo?.market || resolved?.market,
          exchange: staticInfo?.exchange
        })
        : (resolved ? securityToStock(resolved) : buildStock(symbol))

      let quote: StockQuote | undefined
      try {
        const rows = await getRealtimeQuotes([targetSymbol || baseStock.symbol])
        quote = rows[0]
      } catch {
        quote = undefined
      }

      if (!quote) {
        return {
          ...baseStock,
          symbol: displaySymbol,
          name: staticInfo?.name || baseStock.name,
          market: staticInfo?.market || baseStock.market,
          exchange: staticInfo?.exchange || baseStock.exchange,
          eps: toFiniteNumber(staticInfo?.eps, baseStock.eps),
          dividend: toFiniteNumber(staticInfo?.dividendYield, baseStock.dividend)
        }
      }

      const totalShares = Math.max(0, toFiniteNumber(staticInfo?.totalShares, 0))
      const marketCap = totalShares > 0 ? quote.current * totalShares : Number.NaN
      const epsForPe = toFiniteNumber(staticInfo?.epsTtm, toFiniteNumber(staticInfo?.eps, Number.NaN))
      const pe = epsForPe > 0 ? quote.current / epsForPe : Number.NaN

      return {
        ...baseStock,
        symbol: displaySymbol,
        name: staticInfo?.name || quote.name || baseStock.name,
        market: staticInfo?.market || baseStock.market,
        exchange: staticInfo?.exchange || baseStock.exchange,
        currentPrice: quote.current,
        previousClose: quote.previousClose,
        open: quote.open,
        high: quote.high,
        low: quote.low,
        change: quote.change,
        changePercent: quote.changePercent,
        volume: quote.volume,
        marketCap,
        pe,
        eps: toFiniteNumber(staticInfo?.eps, Number.NaN),
        dividend: toFiniteNumber(staticInfo?.dividendYield, Number.NaN),
        updatedAt: quote.timestamp || nowIso()
      }
    },

    async getCompanyProfile(symbol: string): Promise<CompanyProfile> {
      const normalized = normalizeLookupSymbol(symbol)
      const config = getState().config.longBridge
      const resolved = await resolveSecurity(symbol)

      try {
        const profile = await fetchLongBridgeCompanyProfile(config, normalized || symbol)
        if (profile.overview || profile.fields.length > 0) {
          return profile
        }
      } catch {
        // Fall back below.
      }

      return buildCompanyProfileFallback(
        normalized || symbol,
        resolved?.name
      )
    }
  },

  strategyApi: {
    async list(): Promise<Strategy[]> {
      return clone(getState().strategies)
    },

    async get(id: number): Promise<Strategy> {
      const item = getState().strategies.find((row) => row.id === id)
      if (!item) {
        throw new Error('策略不存在')
      }
      return clone(item)
    },

    async create(data: Partial<Strategy>): Promise<Strategy> {
      let created: Strategy | null = null
      updateState((state) => {
        const id = nextId(state.strategies)
        const now = nowIso()
        const nextStrategy: Strategy = {
          id,
          name: String(data.name || `策略-${id}`),
          description: String(data.description || ''),
          config: normalizeStrategyConfig(data.config || {}),
          isActive: Boolean(data.isActive),
          createdAt: now,
          updatedAt: now
        }
        created = nextStrategy
        state.strategies.unshift(nextStrategy)
      })
      return clone(requireValue(created, '创建策略失败'))
    },

    async update(id: number, data: Partial<Strategy>): Promise<Strategy> {
      let updated: Strategy | null = null
      updateState((state) => {
        const index = state.strategies.findIndex((row) => row.id === id)
        if (index < 0) {
          throw new Error('策略不存在')
        }
        const previous = state.strategies[index]
        const nextStrategy: Strategy = {
          ...previous,
          ...data,
          name: String(data.name ?? previous.name),
          description: String(data.description ?? previous.description),
          config: normalizeStrategyConfig(data.config ?? previous.config),
          isActive: data.isActive ?? previous.isActive,
          updatedAt: nowIso()
        }
        updated = nextStrategy
        state.strategies[index] = nextStrategy
      })
      return clone(requireValue(updated, '更新策略失败'))
    },

    async delete(id: number): Promise<{ success: boolean }> {
      updateState((state) => {
        state.strategies = state.strategies.filter((row) => row.id !== id)
      })
      return { success: true }
    },

    async toggle(id: number, isActive: boolean): Promise<{ success: boolean }> {
      updateState((state) => {
        const target = state.strategies.find((row) => row.id === id)
        if (target) {
          target.isActive = isActive
          target.updatedAt = nowIso()
        }
      })
      return { success: true }
    },

    async execute(id: number): Promise<{ success: boolean; strategyId: number }> {
      updateState((state) => {
        const target = state.strategies.find((row) => row.id === id)
        if (target) {
          target.lastExecutedAt = nowIso()
          target.updatedAt = nowIso()
        }
      })
      return { success: true, strategyId: id }
    },

    async reload(id: number): Promise<{ success: boolean; strategyId: number }> {
      updateState((state) => {
        const target = state.strategies.find((row) => row.id === id)
        if (target) {
          target.updatedAt = nowIso()
        }
      })
      return { success: true, strategyId: id }
    }
  },

  tradeApi: {
    async list(params?: {
      page?: number
      pageSize?: number
      symbol?: string
      side?: string
      status?: string
      startDate?: string
      endDate?: string
    }): Promise<PagedResult<Trade>> {
      return applyTradeFilters(getState().trades, params)
    },

    async get(id: number): Promise<Trade> {
      const item = getState().trades.find((row) => row.id === id)
      if (!item) {
        throw new Error('交易记录不存在')
      }
      return clone(item)
    },

    async getByStrategy(strategyId: number): Promise<Trade[]> {
      const rows = getState().trades.filter((row) => row.strategyId === strategyId)
      return clone(rows)
    },

    async getAccount(): Promise<{ cash: number; totalAssets: number; realizedPnL: number; unrealizedPnL: number }> {
      return buildAccountSummary(getState().trades)
    },

    async getPositions(): Promise<Array<{ symbol: string; quantity: number; avgCost: number }>> {
      const positionMap = new Map<string, { quantity: number; cost: number }>()
      getState().trades
        .filter((item) => item.status === 'filled')
        .forEach((trade) => {
          const symbol = trade.symbol
          const current = positionMap.get(symbol) || { quantity: 0, cost: 0 }
          if (trade.side === 'buy') {
            current.quantity += trade.quantity
            current.cost += trade.amount
          } else {
            current.quantity -= trade.quantity
            current.cost -= trade.amount
          }
          positionMap.set(symbol, current)
        })

      return Array.from(positionMap.entries())
        .filter(([, value]) => value.quantity > 0)
        .map(([symbol, value]) => ({
          symbol,
          quantity: value.quantity,
          avgCost: value.quantity > 0 ? round(value.cost / value.quantity) : 0
        }))
    },

    async getStats(startDate?: string, endDate?: string): Promise<{
      totalTrades: number
      totalPnl: number
      winRate: number
      totalVolume: number
    }> {
      const rows = applyTradeFilters(getState().trades, {
        page: 1,
        pageSize: 10000,
        startDate,
        endDate
      }).items

      const totalTrades = rows.length
      const totalVolume = rows.reduce((sum, row) => sum + Number(row.amount || 0), 0)
      const pnls = rows.map((row, index) => (index % 2 === 0 ? 1 : -1) * Number(row.amount || 0) * 0.012)
      const totalPnl = round(pnls.reduce((sum, row) => sum + row, 0))
      const wins = pnls.filter((row) => row > 0).length
      const winRate = rows.length > 0 ? wins / rows.length : 0

      return {
        totalTrades,
        totalPnl,
        winRate,
        totalVolume: round(totalVolume)
      }
    },

    async placeOrder(payload: {
      symbol: string
      side: 'buy' | 'sell'
      orderType: 'market' | 'limit'
      quantity: number
      price?: number
      strategyId?: number
    }): Promise<Trade> {
      let created: Trade | null = null
      updateState((state) => {
        const symbol = normalizeSymbol(payload.symbol)
        const livePrice = buildQuote(symbol).current
        const price = Number(payload.price ?? livePrice)
        const quantity = Math.max(1, Number(payload.quantity || 0))
        const amount = round(price * quantity)
        const id = nextId(state.trades)
        const strategy = state.strategies.find((row) => row.id === payload.strategyId)
        const nextTrade: Trade = {
          id,
          symbol,
          stockName: getMeta(symbol).name,
          side: payload.side === 'sell' ? 'sell' : 'buy',
          quantity,
          price: round(price),
          amount,
          commission: round(amount * 0.0005),
          status: 'filled',
          orderId: `SIM-${1000 + id}`,
          strategyId: strategy?.id,
          strategyName: strategy?.name,
          executedAt: nowIso(),
          createdAt: nowIso()
        }
        created = nextTrade
        state.trades.unshift(nextTrade)
      })

      return clone(requireValue(created, '下单失败'))
    },

    async cancelOrder(orderId: string): Promise<{ success: boolean }> {
      updateState((state) => {
        const target = state.trades.find((row) => row.orderId === orderId || String(row.id) === orderId)
        if (target) {
          target.status = 'cancelled'
        }
      })
      return { success: true }
    }
  },

  backtestApi: {
    async list(): Promise<Backtest[]> {
      const rows = [...getState().backtests].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
      return clone(rows)
    },

    async get(id: number): Promise<Backtest> {
      const item = getState().backtests.find((row) => row.id === id)
      if (!item) {
        throw new Error('回测记录不存在')
      }
      return clone(item)
    },

    async create(data: { strategyId: number; startDate: string; endDate: string; initialCapital: number }): Promise<Backtest> {
      let created: Backtest | null = null
      updateState((state) => {
        const strategy = state.strategies.find((row) => row.id === data.strategyId)
        const id = nextId(state.backtests)
        const nextBacktest = createBacktestFromInput({
          id,
          strategyId: data.strategyId,
          strategyName: strategy?.name || `策略 ${data.strategyId}`,
          startDate: data.startDate,
          endDate: data.endDate,
          initialCapital: data.initialCapital
        })
        created = nextBacktest
        state.backtests.unshift(nextBacktest)
      })

      return clone(requireValue(created, '创建回测失败'))
    },

    async delete(id: number): Promise<{ success: boolean }> {
      updateState((state) => {
        state.backtests = state.backtests.filter((row) => row.id !== id)
      })
      return { success: true }
    }
  },

  monitorApi: {
    async listRules(): Promise<MonitorRule[]> {
      return clone(getState().monitorRules)
    },

    async createRule(data: any): Promise<MonitorRule> {
      let created: MonitorRule | null = null
      updateState((state) => {
        const id = nextId(state.monitorRules)
        const nextRule: MonitorRule = {
          id,
          name: String(data?.name || `规则-${id}`),
          symbols: Array.isArray(data?.symbols)
            ? data.symbols.map((item: unknown) => normalizeSymbol(String(item || ''))).filter(Boolean)
            : [],
          conditions: Array.isArray(data?.conditions) ? data.conditions.map(normalizeCondition) : [],
          notifications: normalizeNotifications(data?.notifications),
          checkInterval: Number(data?.checkInterval || 300),
          isActive: Boolean(data?.isActive ?? data?.isEnabled ?? true),
          createdAt: nowIso()
        }
        created = nextRule
        state.monitorRules.unshift(nextRule)
      })
      return clone(requireValue(created, '创建规则失败'))
    },

    async updateRule(id: number, data: any): Promise<MonitorRule> {
      let updated: MonitorRule | null = null
      updateState((state) => {
        const index = state.monitorRules.findIndex((row) => row.id === id)
        if (index < 0) {
          throw new Error('规则不存在')
        }
        const previous = state.monitorRules[index]
        const nextRule: MonitorRule = {
          ...previous,
          ...data,
          name: String(data?.name ?? previous.name),
          symbols: Array.isArray(data?.symbols)
            ? data.symbols.map((item: unknown) => normalizeSymbol(String(item || ''))).filter(Boolean)
            : previous.symbols,
          conditions: Array.isArray(data?.conditions)
            ? data.conditions.map(normalizeCondition)
            : previous.conditions,
          notifications: data?.notifications ? normalizeNotifications(data.notifications) : previous.notifications,
          checkInterval: Number(data?.checkInterval ?? previous.checkInterval),
          isActive: Boolean(data?.isActive ?? data?.isEnabled ?? previous.isActive)
        }
        updated = nextRule
        state.monitorRules[index] = nextRule
      })
      return clone(requireValue(updated, '更新规则失败'))
    },

    async deleteRule(id: number): Promise<{ success: boolean }> {
      updateState((state) => {
        state.monitorRules = state.monitorRules.filter((row) => row.id !== id)
      })
      return { success: true }
    },

    async toggleRule(id: number, isActive: boolean): Promise<{ success: boolean }> {
      updateState((state) => {
        const target = state.monitorRules.find((row) => row.id === id)
        if (target) {
          target.isActive = Boolean(isActive)
        }
      })
      return { success: true }
    },

    async getAlerts(): Promise<any[]> {
      return []
    },

    async getWatchlist(): Promise<WatchlistItem[]> {
      const config = getState().config.longBridge
      if (hasLongBridgeToken(config)) {
        return getState().watchlist.map(withStockPlaceholder)
      }

      return getState().watchlist.map(withStockInfo)
    },

    async addToWatchlist(symbol: string, notes?: string): Promise<WatchlistItem> {
      let result: WatchlistItem | null = null
      const security = await resolveSecurity(symbol)
      if (!security) {
        throw new Error('未找到该证券代码，请从搜索建议中选择长桥支持的标的')
      }

      const hasToken = hasLongBridgeToken(getState().config.longBridge)
      if (hasToken) {
        try {
          const probe = await getRealtimeQuotes([security.symbol])
          if (probe.length === 0) {
            throw new Error('empty quote')
          }
        } catch {
          throw new Error('未获取到有效行情，请检查代码与市场后缀（如 AAPL.US / 00700.HK / 600519.SH）')
        }
      }

      const lookupSymbol = security.symbol
      const displaySymbol = toDisplaySymbol(security.symbol)

      updateState((state) => {
        const existing = state.watchlist.find((item) => {
          const itemLookup = normalizeLookupSymbol(item.symbol)
          const itemTicker = symbolTicker(item.symbol)
          return itemLookup === lookupSymbol || itemTicker === security.ticker
        })
        if (existing) {
          if (notes !== undefined) {
            existing.notes = notes
          }
          existing.symbol = displaySymbol
          existing.name = security.name || existing.name || security.ticker
          result = hasToken ? withStockPlaceholder(existing) : withStockInfo(existing)
          return
        }

        const id = nextId(state.watchlist)
        const item: WatchlistItem = {
          id,
          symbol: displaySymbol,
          name: security.name || security.ticker,
          notes,
          addedAt: nowIso(),
          stock: hasToken ? undefined : buildStock(displaySymbol)
        }
        state.watchlist.unshift(item)
        result = hasToken ? withStockPlaceholder(item) : withStockInfo(item)
      })

      return clone(requireValue(result, '添加自选失败'))
    },

    async removeFromWatchlist(id: number): Promise<{ success: boolean }> {
      updateState((state) => {
        state.watchlist = state.watchlist.filter((item) => item.id !== id)
      })
      return { success: true }
    }
  },

  aiApi: {
    async analyzeStock(
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
    ): Promise<StockAnalysisResult> {
      const normalized = normalizeSymbol(symbol)
      const openAiConfig = getState().config.openAi

      if (openAiConfig.enabled) {
        const provider = pickAiProvider(openAiConfig, payload?.providerId)
        const prompt = buildAiAnalysisPrompt(normalized, payload)
        const result = await requestOpenAiLikeCompletion(provider, prompt, {
          maxTokens: 1800,
          temperature: 0.45,
          modelOverride: payload?.model
        })

        return {
          symbol: normalized,
          model: result.model,
          analysis: result.content,
          generatedAt: nowIso()
        }
      }

      const quote = buildQuote(normalized)
      const trend = quote.changePercent >= 0 ? '偏强' : '偏弱'
      const volatility = Math.abs(quote.changePercent) > 3 ? '较高' : '中等'
      const focus = String(payload?.focus || '').trim()
      const period = String(payload?.period || 'D').toUpperCase()
      const focusLine = focus
        ? `- 重点关注：${focus}`
        : '- 重点关注：趋势延续性与成交量配合情况'

      return {
        symbol: normalized,
        model: 'demo-analyst-v1',
        analysis: [
          `## ${normalized} AI 观察结论`,
          '',
          `- 当前趋势：${trend}`,
          `- 日内波动：${volatility}`,
          `- 分析周期：${period}`,
          focusLine,
          '',
          '### 交易思路',
          '- 若价格延续放量上行，可考虑分批跟随；若量价背离，优先控制仓位。',
          '- 关注关键支撑位附近的承接强度，避免在情绪高点追涨。',
          '',
          '### 风险提示',
          '- 演示模式下数据为模拟生成，不构成投资建议。',
          '- 请结合真实行情与风险偏好制定交易计划。'
        ].join('\n'),
        generatedAt: nowIso()
      }
    },

    async chat(payload: {
      question: string
      symbol?: string
      focus?: string
      providerId?: string
      model?: string
    }): Promise<AiChatResult> {
      const question = String(payload?.question || '').trim()
      if (!question) {
        throw new Error('请输入问题')
      }

      const focus = String(payload?.focus || '').trim()
      const longBridgeConfig = getState().config.longBridge
      const openAiConfig = getState().config.openAi
      const realtimeSensitive = isRealtimeSensitiveQuestion(question)
      const resolvedSecurity = await resolveChatTargetSymbol(payload?.symbol, question)

      if (realtimeSensitive && !resolvedSecurity) {
        throw new Error('这是实时行情问题，但未识别到标的。请补充证券代码或公司名称（如 601899.SH / 紫金矿业）。')
      }

      if (realtimeSensitive && !hasLongBridgeToken(longBridgeConfig)) {
        throw new Error('未配置长桥 Access Token，无法提供实时行情。请先在系统设置完成长桥配置。')
      }

      let marketContext: AiChatMarketContext | undefined
      if (resolvedSecurity && hasLongBridgeToken(longBridgeConfig)) {
        let realtimeQuote: StockQuote | null = null
        try {
          const rows = await getRealtimeQuotes([resolvedSecurity.symbol], {
            requireRealtime: realtimeSensitive
          })
          realtimeQuote = rows[0] || null
        } catch (error: any) {
          if (realtimeSensitive) {
            throw new Error(error?.message || '未获取到有效实时行情，请检查证券代码格式与长桥权限。')
          }
        }

        if (realtimeQuote) {
          try {
            marketContext = buildChatMarketContext(realtimeQuote, resolvedSecurity.market)
          } catch (error: any) {
            if (realtimeSensitive) {
              throw new Error(error?.message || '行情时间戳不可用，无法提供严格实时回答。')
            }
            marketContext = undefined
          }

          if (realtimeSensitive && marketContext?.freshness === 'stale') {
            throw new Error(`行情时间已过期（${marketContext.lagSeconds} 秒），请稍后重试或检查长桥连接状态。`)
          }
        } else if (realtimeSensitive) {
          throw new Error('未获取到有效实时行情，请检查证券代码格式与长桥权限。')
        }
      }

      const userPrompt = buildStrictChatPrompt(question, focus, marketContext)

      if (openAiConfig.enabled) {
        const provider = pickAiProvider(openAiConfig, payload?.providerId)
        const result = await requestOpenAiLikeCompletion(provider, userPrompt, {
          maxTokens: 1600,
          temperature: 0.35,
          modelOverride: payload?.model
        })

        return {
          model: result.model,
          content: result.content,
          generatedAt: nowIso(),
          marketContext
        }
      }

      if (realtimeSensitive) {
        throw new Error('AI 模型未启用，且该问题需要实时行情分析。请先启用模型源后重试。')
      }

      return {
        model: 'demo-chat-v1',
        content: [
          '结论：当前可继续进行方法论咨询，但行情类问题已强制要求真实实时数据。',
          '',
          '依据：',
          '- 你当前处于前端独立模式；',
          '- 问题可继续追问，我会按交易框架给出思路。',
          '',
          '风险提示：',
          '- 本回答不构成投资建议，请结合真实账户与风险偏好决策。'
        ].join('\n'),
        generatedAt: nowIso(),
        marketContext
      }
    }
  },

  configApi: {
    async get(): Promise<SystemConfig> {
      return clone(getState().config)
    },

    async update(data: Partial<SystemConfig>): Promise<SystemConfig> {
      let nextConfig: SystemConfig | null = null
      updateState((state) => {
        state.config = mergeConfig(state.config, data)
        nextConfig = state.config
      })
      return clone(requireValue(nextConfig, '更新配置失败'))
    },

    async testEmail(): Promise<{ success: boolean; message: string }> {
      return { success: true, message: '演示模式：测试邮件已模拟发送' }
    },

    async testFeishu(): Promise<{ success: boolean; message: string }> {
      return { success: true, message: '演示模式：飞书消息已模拟发送' }
    },

    async testWechat(): Promise<{ success: boolean; message: string }> {
      return { success: true, message: '演示模式：企业微信消息已模拟发送' }
    },

    async testLongBridge(): Promise<{ success: boolean; message: string }> {
      const config = getState().config.longBridge
      if (!hasLongBridgeToken(config)) {
        throw new Error('请先配置 LongBridge Access Token')
      }

      const universe = await fetchLongBridgeSecurityList(config)
      if (universe.length === 0) {
        throw new Error('长桥返回证券列表为空，请检查 AppKey/AppSecret/Token 权限')
      }

      const quote = await fetchLongBridgeRealtimeQuotes(config, ['AAPL.US'])
      const staticInfo = await fetchLongBridgeStaticInfos(config, ['AAPL.US'])
      if (quote.length === 0 || staticInfo.length === 0) {
        throw new Error('长桥返回行情或静态信息为空，请检查 Token 权限或市场时段')
      }

      return {
        success: true,
        message: `连接成功（已获取 ${universe.length} 支证券，AAPL 行情与公司信息可用）`
      }
    },

    async testOpenAi(): Promise<{ success: boolean; message: string }> {
      const config = getState().config.openAi
      if (!config.enabled) {
        throw new Error('请先启用 AI 分析配置')
      }

      const provider = pickAiProvider(config)
      const result = await requestOpenAiLikeCompletion(
        provider,
        '请仅回复“连接成功”。',
        { maxTokens: 32, temperature: 0 }
      )

      return { success: true, message: `连接成功（${result.model}）` }
    }
  },

  reviewApi: {
    async list(params?: { startDate?: string; endDate?: string }): Promise<ReviewRecord[]> {
      const start = parseMaybeDate(params?.startDate)
      const end = parseMaybeDate(params?.endDate ? `${params.endDate}T23:59:59.999Z` : undefined)
      const rows = getState().reviews
        .filter((row) => {
          const current = parseMaybeDate(row.date)
          if (current === null) {
            return false
          }
          if (start !== null && current < start) {
            return false
          }
          if (end !== null && current > end) {
            return false
          }
          return true
        })
        .sort((a, b) => String(b.date).localeCompare(String(a.date)))
      return clone(rows)
    },

    async get(id: number): Promise<ReviewRecord> {
      const item = getState().reviews.find((row) => row.id === id)
      if (!item) {
        throw new Error('复盘记录不存在')
      }
      return clone(item)
    },

    async create(data: Partial<ReviewRecord>): Promise<ReviewRecord> {
      let created: ReviewRecord | null = null
      updateState((state) => {
        const id = nextId(state.reviews)
        const nextReview: ReviewRecord = {
          id,
          date: String(data.date || new Date().toISOString().slice(0, 10)),
          marketSummary: String(data.marketSummary || ''),
          trades: Array.isArray(data.trades) ? data.trades : [],
          notes: String(data.notes || ''),
          lessons: String(data.lessons || ''),
          tags: Array.isArray(data.tags) ? data.tags.map((item) => String(item)) : [],
          createdAt: nowIso()
        }
        created = nextReview
        state.reviews.unshift(nextReview)
      })
      return clone(requireValue(created, '创建复盘失败'))
    },

    async update(id: number, data: Partial<ReviewRecord>): Promise<ReviewRecord> {
      let updated: ReviewRecord | null = null
      updateState((state) => {
        const index = state.reviews.findIndex((row) => row.id === id)
        if (index < 0) {
          throw new Error('复盘记录不存在')
        }
        const previous = state.reviews[index]
        const nextReview: ReviewRecord = {
          ...previous,
          ...data,
          date: String(data.date ?? previous.date),
          marketSummary: String(data.marketSummary ?? previous.marketSummary),
          notes: String(data.notes ?? previous.notes),
          lessons: String(data.lessons ?? previous.lessons),
          tags: Array.isArray(data.tags) ? data.tags.map((item) => String(item)) : previous.tags,
          trades: Array.isArray(data.trades) ? data.trades : previous.trades
        }
        updated = nextReview
        state.reviews[index] = nextReview
      })
      return clone(requireValue(updated, '更新复盘失败'))
    },

    async delete(id: number): Promise<{ success: boolean }> {
      updateState((state) => {
        state.reviews = state.reviews.filter((row) => row.id !== id)
      })
      return { success: true }
    },

    async getStats(startDate?: string, endDate?: string): Promise<{ total: number }> {
      const rows = await this.list({ startDate, endDate })
      return { total: rows.length }
    }
  }
}
