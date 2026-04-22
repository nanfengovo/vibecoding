import { normalizeSymbol, normalizeSymbols, requestLongbridge } from './_http.js'

function marketFromSymbol(symbol) {
  const normalized = normalizeSymbol(symbol)
  const parts = normalized.split('.')
  return parts.length > 1 ? String(parts.pop() || '').toUpperCase() : 'US'
}

function pickName(item, symbol) {
  return String(
    item?.name
    || item?.name_en
    || item?.name_cn
    || item?.name_hk
    || symbol
  ).trim() || symbol
}

function normalizeStaticRow(item) {
  const symbol = normalizeSymbol(item?.symbol || '')
  if (!symbol) {
    return null
  }

  return {
    symbol,
    name: pickName(item, symbol),
    nameEn: String(item?.name_en || '').trim() || undefined,
    nameCn: String(item?.name_cn || '').trim() || undefined,
    nameHk: String(item?.name_hk || '').trim() || undefined,
    market: marketFromSymbol(symbol),
    exchange: undefined,
    currency: undefined,
    lotSize: 0,
    totalShares: 0,
    circulatingShares: 0,
    hkShares: 0,
    eps: Number.NaN,
    epsTtm: Number.NaN,
    bps: Number.NaN,
    dividendYield: Number.NaN,
    board: undefined
  }
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

  try {
    const symbolsByMarket = new Map()
    symbols.forEach((symbol) => {
      const market = marketFromSymbol(symbol)
      if (!symbolsByMarket.has(market)) {
        symbolsByMarket.set(market, new Set())
      }
      symbolsByMarket.get(market).add(symbol)
    })

    const resultMap = new Map()
    for (const [market, symbolSet] of symbolsByMarket.entries()) {
      // According to Longbridge docs, get_security_list currently supports US + Overnight.
      if (market !== 'US') {
        continue
      }

      const payload = await requestLongbridge({
        method: 'GET',
        path: '/v1/quote/get_security_list',
        query: { market: 'US', category: 'Overnight' },
        ...requestBase
      })

      const list = Array.isArray(payload?.data?.list) ? payload.data.list : []
      list.forEach((item) => {
        const normalized = normalizeStaticRow(item)
        if (!normalized) {
          return
        }
        if (symbolSet.has(normalized.symbol)) {
          resultMap.set(normalized.symbol, normalized)
        }
      })
    }

    const rows = symbols.map((symbol) => resultMap.get(symbol)).filter(Boolean)
    return res.status(200).json({ list: rows, source: 'longbridge-security-list' })
  } catch (error) {
    return res.status(error?.status || 502).json({
      message: error?.message || '长桥静态信息请求失败',
      code: error?.code ?? null
    })
  }
}
