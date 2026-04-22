import { createHash, createHmac } from 'node:crypto'

function normalizeBaseUrl(value) {
  const raw = String(value || '').trim()
  if (!raw) {
    return 'https://openapi.longbridge.com'
  }

  let url
  try {
    url = new URL(raw)
  } catch {
    return 'https://openapi.longbridge.com'
  }

  const host = String(url.host || '').toLowerCase()
  if (host === 'open.longbridge.com' || host === 'openapi.longbridgeapp.com') {
    return 'https://openapi.longbridge.com'
  }

  if (host === 'open.longbridge.cn') {
    return 'https://openapi.longbridge.cn'
  }

  if (host === 'openapi.longbridge.com' || host === 'openapi.longbridge.cn') {
    return `${url.protocol}//${url.host}`
  }

  return 'https://openapi.longbridge.com'
}

function normalizeSymbol(value) {
  const normalized = String(value || '').trim().toUpperCase()
  if (!normalized) {
    return ''
  }

  if (/^(SH|SZ)\d{6}$/.test(normalized)) {
    return `${normalized.slice(2)}.${normalized.slice(0, 2)}`
  }

  if (normalized.includes('.')) {
    return normalized
  }

  if (/^\d{6}$/.test(normalized)) {
    // A-share: 6/9 开头通常为 SH，其余常见为 SZ。
    const region = /^[69]/.test(normalized) ? 'SH' : 'SZ'
    return `${normalized}.${region}`
  }

  if (/^\d{5}$/.test(normalized)) {
    return `${normalized}.HK`
  }

  return `${normalized}.US`
}

function normalizeSymbols(values, limit = 200) {
  const rows = Array.isArray(values) ? values : []
  return Array.from(new Set(rows.map(normalizeSymbol).filter(Boolean))).slice(0, Math.max(1, limit))
}

function extractMessage(payload, fallback) {
  if (payload && typeof payload.message === 'string' && payload.message.trim()) {
    return payload.message.trim()
  }

  if (payload && typeof payload.error === 'string' && payload.error.trim()) {
    return payload.error.trim()
  }

  return fallback
}

function looksLikeJwtToken(token) {
  return /^[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+$/.test(token)
}

function resolveAuthMode(accessToken, appKey, appSecret) {
  const token = String(accessToken || '').trim()
  const key = String(appKey || '').trim()
  const secret = String(appSecret || '').trim()

  if (token.toLowerCase().startsWith('bearer ')) {
    return 'oauth'
  }

  // Legacy API Key 模式必须同时携带 App Key + App Secret。
  if (key && secret) {
    return 'legacy-signed'
  }

  // 若 token 形态像 JWT，则默认按 OAuth Bearer 发送。
  if (looksLikeJwtToken(token)) {
    return 'oauth'
  }

  // 其余情况按 Legacy raw token 发送，避免把 Legacy Token 误加 Bearer 前缀。
  return 'legacy-raw'
}

function buildAuthorizationHeader(accessToken, authMode) {
  const token = String(accessToken || '').trim()
  if (token.toLowerCase().startsWith('bearer ')) {
    return token
  }

  if (authMode === 'oauth') {
    return `Bearer ${token}`
  }

  return token
}

function sha1Hex(input) {
  return createHash('sha1').update(String(input || ''), 'utf8').digest('hex')
}

function buildSignatureHeaders({ method, url, authorization, appKey, appSecret, bodyText, authMode }) {
  if (authMode !== 'legacy-signed') {
    return {}
  }

  const key = String(appKey || '').trim()
  const secret = String(appSecret || '').trim()
  if (!key || !secret) {
    throw new Error('Legacy 模式需要同时配置 App Key 与 App Secret')
  }

  const signedHeaders = 'authorization;x-api-key;x-timestamp'
  const timestamp = Date.now().toString()
  const requestUrl = new URL(url)
  const query = requestUrl.search.startsWith('?') ? requestUrl.search.slice(1) : requestUrl.search
  const headersText = [
    `authorization:${authorization}`,
    `x-api-key:${key}`,
    `x-timestamp:${timestamp}`
  ].join('\n') + '\n'

  let plainText = `${String(method || 'GET').toUpperCase()}|${requestUrl.pathname}|${query}|${headersText}|${signedHeaders}|`
  if (bodyText) {
    plainText += sha1Hex(bodyText)
  }

  const textToSign = `HMAC-SHA256|${sha1Hex(plainText)}`
  const signature = createHmac('sha256', secret).update(textToSign, 'utf8').digest('hex')

  return {
    'x-timestamp': timestamp,
    'x-api-signature': `HMAC-SHA256 SignedHeaders=${signedHeaders}, Signature=${signature}`
  }
}

function toNumber(value, fallback = 0) {
  const numeric = Number(value)
  return Number.isFinite(numeric) ? numeric : fallback
}

async function requestLongbridge({
  method = 'GET',
  baseUrl,
  path,
  query,
  body,
  accessToken,
  appKey,
  appSecret,
  acceptLanguage = 'zh-CN,zh;q=0.9,en;q=0.8'
}) {
  const token = String(accessToken || '').trim()
  if (!token) {
    throw Object.assign(new Error('accessToken is required'), { status: 400, code: null })
  }

  const endpoint = new URL(path, normalizeBaseUrl(baseUrl))
  if (query && typeof query === 'object') {
    Object.entries(query).forEach(([key, value]) => {
      if (value !== undefined && value !== null && String(value).trim()) {
        endpoint.searchParams.set(key, String(value))
      }
    })
  }

  const authMode = resolveAuthMode(token, appKey, appSecret)
  const authorization = buildAuthorizationHeader(token, authMode)
  const bodyText = body ? JSON.stringify(body) : ''
  const signatureHeaders = buildSignatureHeaders({
    method,
    url: endpoint.toString(),
    authorization,
    appKey,
    appSecret,
    bodyText,
    authMode
  })
  const legacyApiKey = authMode === 'legacy-signed' ? String(appKey || '').trim() : ''

  const response = await fetch(endpoint.toString(), {
    method: String(method || 'GET').toUpperCase(),
    headers: {
      Accept: 'application/json',
      'Accept-Language': acceptLanguage,
      Authorization: authorization,
      ...(bodyText ? { 'Content-Type': 'application/json' } : {}),
      ...(legacyApiKey ? { 'x-api-key': legacyApiKey } : {}),
      ...signatureHeaders
    },
    ...(bodyText ? { body: bodyText } : {})
  })

  const text = await response.text()
  let payload = null
  try {
    payload = text ? JSON.parse(text) : null
  } catch {
    payload = null
  }

  if (!response.ok) {
    const message = extractMessage(payload, `Longbridge request failed (${response.status})`)
    throw Object.assign(new Error(message), {
      status: response.status,
      code: payload?.code ?? null,
      payload
    })
  }

  return payload
}

export {
  extractMessage,
  normalizeBaseUrl,
  normalizeSymbol,
  normalizeSymbols,
  requestLongbridge,
  toNumber
}
