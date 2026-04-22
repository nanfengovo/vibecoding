import { normalizeSymbol, normalizeSymbols, requestLongbridge, toNumber } from './_http.js'

const DEFAULT_SYNC_GROUP = 'QuantTrading Sync'

function getRealtimeRows(payload) {
  if (Array.isArray(payload?.data?.secu_quote)) {
    return payload.data.secu_quote
  }

  if (Array.isArray(payload?.data?.quote)) {
    return payload.data.quote
  }

  if (Array.isArray(payload?.data?.list)) {
    return payload.data.list
  }

  if (Array.isArray(payload?.data)) {
    return payload.data
  }

  if (Array.isArray(payload?.secu_quote)) {
    return payload.secu_quote
  }

  if (Array.isArray(payload?.quote)) {
    return payload.quote
  }

  if (Array.isArray(payload?.list)) {
    return payload.list
  }

  return []
}

function parseGroupId(value) {
  const numeric = Number(value)
  return Number.isFinite(numeric) && numeric > 0 ? Math.round(numeric) : null
}

function getGroups(payload) {
  if (Array.isArray(payload?.data?.groups)) {
    return payload.data.groups
  }

  if (Array.isArray(payload?.groups)) {
    return payload.groups
  }

  return []
}

function getSecurities(group) {
  if (Array.isArray(group?.securities)) {
    return group.securities
  }

  if (Array.isArray(group?.Securites)) {
    return group.Securites
  }

  return []
}

function findGroup(groups, groupName, groupId = null) {
  if (groupId !== null) {
    const matched = groups.find((item) => parseGroupId(item?.id) === groupId)
    if (matched) {
      return matched
    }
  }

  const normalized = String(groupName || '').trim()
  if (!normalized) {
    return null
  }

  return groups.find((item) => String(item?.name || '').trim() === normalized) || null
}

function parseQuoteTimestampToIso(value) {
  const raw = toNumber(value, 0)
  if (!Number.isFinite(raw) || raw <= 0) {
    return ''
  }

  const ms = raw > 1_000_000_000_000 ? raw : raw * 1000
  const parsed = new Date(ms)
  return Number.isNaN(parsed.getTime()) ? '' : parsed.toISOString()
}

function normalizeQuoteRow(quote, fallbackSymbol) {
  const symbol = normalizeSymbol(quote?.symbol || fallbackSymbol)
  const current = toNumber(
    quote?.last_done
      ?? quote?.last
      ?? quote?.price
      ?? quote?.current,
    Number.NaN
  )
  const previousClose = toNumber(
    quote?.prev_close
      ?? quote?.pre_close
      ?? quote?.previous_close
      ?? quote?.prevClose,
    current
  )

  const rawChange = toNumber(quote?.change_value ?? quote?.change, Number.NaN)
  const change = Number.isFinite(rawChange)
    ? rawChange
    : (Number.isFinite(current) && Number.isFinite(previousClose) ? current - previousClose : Number.NaN)
  const rawChangePercent = toNumber(quote?.change_rate ?? quote?.change_percent ?? quote?.changePercent, Number.NaN)
  const changePercent = Number.isFinite(rawChangePercent)
    ? rawChangePercent
    : (
      Number.isFinite(previousClose) && previousClose !== 0 && Number.isFinite(change)
        ? (change / previousClose) * 100
        : Number.NaN
    )

  return {
    symbol,
    name: String(quote?.name || symbol).trim() || symbol,
    current,
    previousClose,
    open: toNumber(quote?.open, current),
    high: toNumber(quote?.high, current),
    low: toNumber(quote?.low, current),
    volume: toNumber(quote?.volume, 0),
    turnover: toNumber(quote?.turnover, 0),
    change,
    changePercent,
    timestamp: parseQuoteTimestampToIso(quote?.timestamp)
  }
}

function normalizeWatchlistSecurityRow(security, fallbackSymbol) {
  const symbol = normalizeSymbol(security?.symbol || fallbackSymbol)
  const current = toNumber(
    security?.watched_price
      ?? security?.price
      ?? security?.last_done
      ?? security?.current,
    Number.NaN
  )
  const previousClose = toNumber(
    security?.previous_close
      ?? security?.prev_close
      ?? security?.pre_close
      ?? security?.prevClose,
    current
  )
  const change = Number.isFinite(current) && Number.isFinite(previousClose)
    ? current - previousClose
    : Number.NaN
  const changePercent = Number.isFinite(previousClose) && previousClose !== 0 && Number.isFinite(change)
    ? (change / previousClose) * 100
    : Number.NaN
  const watchedAt = toNumber(security?.watched_at, 0)
  const watchedAtMs = watchedAt > 0 ? watchedAt * 1000 : 0

  return {
    symbol,
    name: String(security?.name || symbol).trim() || symbol,
    current,
    previousClose,
    open: toNumber(security?.open, current),
    high: toNumber(security?.high, current),
    low: toNumber(security?.low, current),
    volume: toNumber(security?.volume, 0),
    turnover: toNumber(security?.turnover, 0),
    change,
    changePercent,
    timestamp: watchedAtMs > 0 ? new Date(watchedAtMs).toISOString() : new Date().toISOString(),
    isRealtime: false
  }
}

function buildRealtimePath(symbols) {
  const query = symbols.map((symbol) => `symbol=${encodeURIComponent(symbol)}`).join('&')
  return `/v1/quote/realtime?${query}`
}

function isApiNotFoundError(error) {
  const code = Number(error?.code ?? 0)
  const message = String(error?.message || '').toLowerCase()
  return code === 404000 || message.includes('api not found') || message.includes('接口不存在')
}

async function fetchRealtimeQuotes(requestBase, symbols) {
  const payload = await requestLongbridge({
    method: 'GET',
    path: buildRealtimePath(symbols),
    ...requestBase
  })

  const rows = getRealtimeRows(payload)
  const rowMap = new Map()
  rows.forEach((item) => {
    const symbol = normalizeSymbol(item?.symbol || '')
    if (symbol) {
      rowMap.set(symbol, item)
    }
  })

  const list = symbols
    .map((symbol) => {
      const row = rowMap.get(symbol)
      if (!row) {
        return null
      }

      return { ...normalizeQuoteRow(row, symbol), isRealtime: true }
    })
    .filter(Boolean)

  return { list, payload }
}

async function fetchWatchlistSnapshot(requestBase, symbols, groupName) {
  const firstList = await requestLongbridge({
    method: 'GET',
    path: '/v1/watchlist/groups',
    ...requestBase
  })

  const groups = getGroups(firstList)
  const matched = findGroup(groups, groupName, null)
  let groupId = matched ? parseGroupId(matched.id) : null

  if (groupId === null) {
    const created = await requestLongbridge({
      method: 'POST',
      path: '/v1/watchlist/groups',
      body: {
        name: groupName,
        securities: symbols
      },
      ...requestBase
    })
    groupId = parseGroupId(created?.data?.id ?? created?.id)
  } else {
    await requestLongbridge({
      method: 'PUT',
      path: '/v1/watchlist/groups',
      body: {
        id: groupId,
        name: groupName,
        securities: symbols,
        mode: 'replace'
      },
      ...requestBase
    })
  }

  if (groupId === null) {
    return []
  }

  const secondList = await requestLongbridge({
    method: 'GET',
    path: '/v1/watchlist/groups',
    ...requestBase
  })

  const syncedGroup = findGroup(getGroups(secondList), groupName, groupId)
  if (!syncedGroup) {
    return []
  }

  const securities = getSecurities(syncedGroup)
  const map = new Map()
  securities.forEach((item) => {
    const symbol = normalizeSymbol(item?.symbol || '')
    if (symbol) {
      map.set(symbol, item)
    }
  })

  return symbols
    .map((symbol) => {
      const item = map.get(symbol)
      if (!item) {
        return null
      }

      return normalizeWatchlistSecurityRow(item, symbol)
    })
    .filter(Boolean)
}

export default async function handler(req, res) {
  if (req.method !== 'POST') {
    res.setHeader('Allow', 'POST')
    return res.status(405).json({ message: 'Method Not Allowed' })
  }

  const body = req.body && typeof req.body === 'object' ? req.body : {}
  const symbols = normalizeSymbols(body.symbols, 200)
  if (symbols.length === 0) {
    return res.status(400).json({ message: 'symbols is required' })
  }

  const requestBase = {
    baseUrl: body.baseUrl,
    accessToken: body.accessToken,
    appKey: body.appKey,
    appSecret: body.appSecret,
    acceptLanguage: String(req.headers['accept-language'] || 'zh-CN,zh;q=0.9,en;q=0.8')
  }
  const groupName = String(body.groupName || DEFAULT_SYNC_GROUP).trim() || DEFAULT_SYNC_GROUP

  try {
    const realtime = await fetchRealtimeQuotes(requestBase, symbols)
    let merged = realtime.list
    let warning = ''

    const missing = symbols.filter((symbol) => !merged.some((item) => item.symbol === symbol))
    if (missing.length > 0) {
      try {
        const snapshots = await fetchWatchlistSnapshot(requestBase, missing, groupName)
        if (snapshots.length > 0) {
          merged = [...merged, ...snapshots]
          warning = '部分标的未返回实时成交时间，已回退到自选快照补全。'
        }
      } catch {
        // Ignore snapshot fallback failures if realtime already has data.
      }
    }

    if (merged.length > 0) {
      return res.status(200).json({
        list: merged,
        source: warning ? 'longbridge-realtime-with-fallback' : 'longbridge-realtime',
        warning: warning || undefined
      })
    }

    throw Object.assign(
      new Error(String(realtime.payload?.message || '未获取到有效行情，请检查代码格式与行情权限').trim()),
      { status: 502, code: realtime.payload?.code ?? null }
    )
  } catch (primaryError) {
    if (!isApiNotFoundError(primaryError)) {
      return res.status(primaryError?.status || 502).json({
        message: primaryError?.message || '长桥行情请求失败',
        code: primaryError?.code ?? null
      })
    }

    try {
      const snapshots = await fetchWatchlistSnapshot(requestBase, symbols, groupName)
      if (snapshots.length > 0) {
        return res.status(200).json({
          list: snapshots,
          source: 'longbridge-watchlist-snapshot',
          warning: '当前账号不支持实时行情接口，已回退到关注快照（可能不是实时成交价）。'
        })
      }
    } catch (fallbackError) {
      return res.status(fallbackError?.status || 502).json({
        message: fallbackError?.message || primaryError?.message || '长桥行情请求失败',
        code: fallbackError?.code ?? primaryError?.code ?? null
      })
    }

    return res.status(primaryError?.status || 502).json({
      message: primaryError?.message || '长桥行情请求失败',
      code: primaryError?.code ?? null
    })
  }
}
