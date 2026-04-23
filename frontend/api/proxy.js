const DEFAULT_TIMEOUT_MS = 90_000

function trimTrailingSlash(value) {
  return String(value || '').trim().replace(/\/+$/, '')
}

function resolveBackendApiBaseUrl() {
  const candidates = [
    ['BACKEND_API_BASE_URL', process.env.BACKEND_API_BASE_URL],
    ['BACKEND_API_URL', process.env.BACKEND_API_URL]
  ]
    .map(([key, value]) => [key, trimTrailingSlash(value)])
    .filter(([, value]) => Boolean(value))

  const [source, raw] = candidates[0] || ['', '']
  if (!raw) {
    return { baseUrl: '', source: '' }
  }

  let parsed
  try {
    parsed = new URL(raw)
  } catch {
    return { baseUrl: '', source: '' }
  }

  const pathname = trimTrailingSlash(parsed.pathname || '')
  if (!pathname.toLowerCase().endsWith('/api')) {
    parsed.pathname = `${pathname}/api`
  } else {
    parsed.pathname = pathname
  }

  return {
    baseUrl: trimTrailingSlash(parsed.toString()),
    source
  }
}

function normalizePathParam(value) {
  if (Array.isArray(value)) {
    return value
      .map((item) => String(item || '').trim())
      .filter(Boolean)
      .join('/')
  }

  return String(value || '').trim().replace(/^\/+/, '')
}

function appendQuery(searchParams, key, value) {
  if (value === undefined || value === null) {
    return
  }

  if (Array.isArray(value)) {
    value.forEach((item) => appendQuery(searchParams, key, item))
    return
  }

  const normalized = String(value).trim()
  if (!normalized) {
    return
  }

  searchParams.append(key, normalized)
}

function shouldSendBody(method) {
  const normalized = String(method || 'GET').toUpperCase()
  return normalized !== 'GET' && normalized !== 'HEAD'
}

function buildUpstreamBody(req, hasBody) {
  if (!hasBody) {
    return undefined
  }

  const body = req.body
  if (body === undefined || body === null) {
    return undefined
  }

  if (typeof body === 'string') {
    return body
  }

  if (Buffer.isBuffer(body)) {
    return body
  }

  if (typeof body === 'object') {
    return JSON.stringify(body)
  }

  return String(body)
}

function pickHeader(req, key) {
  const value = req.headers?.[key]
  if (Array.isArray(value)) {
    return value[0]
  }
  return value
}

export default async function handler(req, res) {
  const { baseUrl: backendApiBaseUrl, source: backendSource } = resolveBackendApiBaseUrl()
  if (!backendApiBaseUrl) {
    return res.status(503).json({
      message: '远程后端未配置：请在部署环境设置 BACKEND_API_BASE_URL（例如 https://your-backend.example.com ）'
    })
  }

  const relativePath = normalizePathParam(req.query?.path)
  const upstreamUrl = new URL(`${backendApiBaseUrl}/${relativePath}`)

  const query = req.query && typeof req.query === 'object' ? req.query : {}
  Object.entries(query).forEach(([key, value]) => {
    if (key === 'path') {
      return
    }
    appendQuery(upstreamUrl.searchParams, key, value)
  })

  const method = String(req.method || 'GET').toUpperCase()
  const withBody = shouldSendBody(method)
  const body = buildUpstreamBody(req, withBody)

  const headers = {
    Accept: pickHeader(req, 'accept') || 'application/json',
    ...(body ? { 'Content-Type': pickHeader(req, 'content-type') || 'application/json' } : {})
  }

  const authorization = pickHeader(req, 'authorization')
  if (authorization) {
    headers.Authorization = authorization
  }

  let controller = null
  if (typeof AbortController !== 'undefined') {
    controller = new AbortController()
  }

  let abortTimer = null
  if (controller) {
    abortTimer = setTimeout(() => controller.abort(), DEFAULT_TIMEOUT_MS)
  }

  try {
    const upstreamResponse = await fetch(upstreamUrl.toString(), {
      method,
      headers,
      ...(body ? { body } : {}),
      ...(controller ? { signal: controller.signal } : {})
    })

    const text = await upstreamResponse.text()
    const contentType = upstreamResponse.headers.get('content-type') || ''

    if (contentType.toLowerCase().includes('application/json')) {
      let payload = null
      try {
        payload = text ? JSON.parse(text) : null
      } catch {
        payload = { message: text || '上游返回了非 JSON 内容' }
      }
      return res.status(upstreamResponse.status).json(payload)
    }

    res.status(upstreamResponse.status)
    if (contentType) {
      res.setHeader('Content-Type', contentType)
    }
    return res.send(text)
  } catch (error) {
    const message = error?.name === 'AbortError'
      ? `后端请求超时（>${DEFAULT_TIMEOUT_MS / 1000}s）`
      : (error?.message || '代理请求失败')
    return res.status(502).json({
      message,
      backend: backendApiBaseUrl,
      source: backendSource || 'unknown'
    })
  } finally {
    if (abortTimer) {
      clearTimeout(abortTimer)
    }
  }
}
