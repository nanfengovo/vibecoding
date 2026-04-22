import { requestLongbridge } from './_http.js'

function normalizeRow(item, fallbackMarket) {
  const symbol = String(item?.symbol || '').trim().toUpperCase()
  if (!symbol) {
    return null
  }

  const name = String(
    item?.name
    || item?.name_en
    || item?.name_cn
    || item?.name_hk
    || symbol
  ).trim()

  return {
    symbol,
    name: name || symbol,
    market: symbol.includes('.') ? symbol.split('.').pop() : fallbackMarket
  }
}

export default async function handler(req, res) {
  if (req.method !== 'POST') {
    res.setHeader('Allow', 'POST')
    return res.status(405).json({ message: 'Method Not Allowed' })
  }

  const body = req.body && typeof req.body === 'object' ? req.body : {}
  const market = String(body.market || 'US').toUpperCase()
  const category = String(body.category || 'Overnight')

  try {
    const payload = await requestLongbridge({
      method: 'GET',
      baseUrl: body.baseUrl,
      path: '/v1/quote/get_security_list',
      query: { market, category },
      accessToken: body.accessToken,
      appKey: body.appKey,
      appSecret: body.appSecret,
      acceptLanguage: String(req.headers['accept-language'] || 'zh-CN,zh;q=0.9,en;q=0.8')
    })

    const list = Array.isArray(payload?.data?.list) ? payload.data.list : []
    const rows = list.map((item) => normalizeRow(item, market)).filter(Boolean)
    return res.status(200).json({ list: rows, source: 'longbridge-http' })
  } catch (error) {
    return res.status(error?.status || 502).json({
      message: error?.message || 'Failed to call Longbridge API',
      code: error?.code ?? null
    })
  }
}
