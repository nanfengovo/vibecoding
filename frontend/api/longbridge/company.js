import { normalizeSymbol } from './_http.js'

function normalizeLocale(acceptLanguage) {
  const raw = String(acceptLanguage || '').toLowerCase()
  if (raw.includes('zh-hk') || raw.includes('zh-tw')) {
    return 'zh-HK'
  }
  if (raw.includes('zh')) {
    return 'zh-CN'
  }
  return 'en'
}

function parseFrontmatter(text) {
  const content = String(text || '')
  if (!content.startsWith('---\n')) {
    return { frontmatter: {}, body: content }
  }

  const end = content.indexOf('\n---\n', 4)
  if (end <= 0) {
    return { frontmatter: {}, body: content }
  }

  const rawFrontmatter = content.slice(4, end)
  const body = content.slice(end + 5)
  const frontmatter = {}

  rawFrontmatter.split('\n').forEach((line) => {
    const match = line.match(/^([A-Za-z0-9_]+):\s*(.*)$/)
    if (!match) {
      return
    }

    const key = match[1]
    const value = String(match[2] || '').trim().replace(/^"|"$/g, '')
    if (key) {
      frontmatter[key] = value
    }
  })

  return { frontmatter, body }
}

function stripMarkdown(value) {
  return String(value || '')
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/!\[[^\]]*]\([^)]*\)/g, ' ')
    .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
    .replace(/^#{1,6}\s+/gm, '')
    .replace(/^\s*[-*]\s+/gm, '')
    .replace(/\*\*([^*]+)\*\*/g, '$1')
    .replace(/__([^_]+)__/g, '$1')
    .replace(/\|/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

function extractHeadingTitle(body) {
  const firstHeading = String(body || '')
    .split('\n')
    .map((line) => line.trim())
    .find((line) => /^#\s+/.test(line))
  return firstHeading ? firstHeading.replace(/^#\s+/, '').trim() : ''
}

function extractOverviewSection(body) {
  const lines = String(body || '').split('\n')
  const start = lines.findIndex((line) => /^##\s*(公司概览|公司概況|公司概况|公司簡介|Company Overview)/i.test(line.trim()))
  const startIndex = start >= 0 ? start + 1 : 0

  let endIndex = lines.length
  for (let i = startIndex; i < lines.length; i += 1) {
    if (/^##\s+/.test(lines[i].trim())) {
      endIndex = i
      break
    }
  }

  const raw = lines.slice(startIndex, endIndex).join('\n')
  const noTable = raw
    .split('\n')
    .filter((line) => !line.trim().startsWith('|'))
    .join('\n')
  const overview = stripMarkdown(noTable)
  if (overview) {
    return overview
  }

  return stripMarkdown(raw)
}

function extractTableFields(body) {
  const lines = String(body || '').split('\n')
  const fields = []

  lines.forEach((line) => {
    const trimmed = line.trim()
    if (!trimmed.startsWith('|') || !trimmed.endsWith('|')) {
      return
    }

    const cols = trimmed
      .split('|')
      .map((part) => part.trim())
      .filter(Boolean)

    if (cols.length < 2) {
      return
    }

    const key = stripMarkdown(cols[0])
    const value = stripMarkdown(cols[1])
    if (!key || !value) {
      return
    }

    const normalizedKey = key.toLowerCase()
    if (
      normalizedKey === 'item'
      || normalizedKey === 'detail'
      || normalizedKey === '指标'
      || normalizedKey === '项目'
      || /^-+$/.test(key)
      || /^-+$/.test(value)
    ) {
      return
    }

    fields.push({ key, value })
  })

  return fields.slice(0, 14)
}

function extractCurrentIndustry(frontmatter, fields) {
  const fromFrontmatter = String(frontmatter?.industry || '').trim()
  if (fromFrontmatter) {
    return fromFrontmatter
  }

  const matched = (fields || []).find((item) => {
    const key = String(item?.key || '').trim().toLowerCase()
    return key === '行业' || key === 'industry'
  })

  return String(matched?.value || '').trim()
}

function parseMarkdownTableRow(line) {
  const trimmed = String(line || '').trim()
  if (!trimmed.startsWith('|') || !trimmed.endsWith('|')) {
    return []
  }

  return trimmed
    .slice(1, -1)
    .split('|')
    .map((item) => stripMarkdown(item).trim())
}

function isMarkdownTableSeparatorRow(cells) {
  return Array.isArray(cells) && cells.length > 0 && cells.every((cell) => /^:?-{2,}:?$/.test(String(cell || '').trim()))
}

function getHeaderIndex(headers, aliases) {
  const loweredAliases = aliases.map((item) => String(item).toLowerCase())
  return headers.findIndex((header) => loweredAliases.includes(String(header || '').toLowerCase()))
}

function readCell(cells, index) {
  if (!Array.isArray(cells) || index < 0 || index >= cells.length) {
    return ''
  }

  return String(cells[index] || '').trim()
}

function extractPeerSymbolFromName(name) {
  const matched = String(name || '').match(/\(([A-Z0-9.-]{2,})\)/i)
  return matched ? String(matched[1] || '').trim().toUpperCase() : ''
}

function extractIndustryPeers(body) {
  const lines = String(body || '').split('\n')
  const sectionStart = lines.findIndex((line) => /^##\s*(同业比较|同行比较|Peer Comparison)/i.test(line.trim()))
  if (sectionStart < 0) {
    return []
  }

  let sectionEnd = lines.length
  for (let i = sectionStart + 1; i < lines.length; i += 1) {
    if (/^##\s+/.test(lines[i].trim())) {
      sectionEnd = i
      break
    }
  }

  const section = lines.slice(sectionStart + 1, sectionEnd)
  let header = []
  let rowStart = -1
  for (let i = 0; i + 1 < section.length; i += 1) {
    const headerCells = parseMarkdownTableRow(section[i])
    const separatorCells = parseMarkdownTableRow(section[i + 1])
    if (headerCells.length === 0 || separatorCells.length === 0) {
      continue
    }
    if (!isMarkdownTableSeparatorRow(separatorCells)) {
      continue
    }
    header = headerCells
    rowStart = i + 2
    break
  }

  if (rowStart < 0 || header.length === 0) {
    return []
  }

  const rankIndex = getHeaderIndex(header, ['排名', 'rank'])
  const nameIndex = getHeaderIndex(header, ['名称', 'name'])
  const profitIndex = getHeaderIndex(header, ['盈利', 'profit'])
  const growthIndex = getHeaderIndex(header, ['成长', 'growth'])
  const operationIndex = getHeaderIndex(header, ['运营', 'operation'])
  const safetyIndex = getHeaderIndex(header, ['财务安全', '安全', 'security', 'financial safety'])
  const cashFlowIndex = getHeaderIndex(header, ['现金流', 'cash', 'cash flow'])
  const ratingIndex = getHeaderIndex(header, ['评级', 'rating'])

  const peers = []
  for (let i = rowStart; i < section.length; i += 1) {
    const cells = parseMarkdownTableRow(section[i])
    if (cells.length === 0 || isMarkdownTableSeparatorRow(cells)) {
      continue
    }

    const name = readCell(cells, nameIndex)
    const symbol = extractPeerSymbolFromName(name)
    if (!name && !symbol) {
      continue
    }

    peers.push({
      rank: readCell(cells, rankIndex),
      name,
      symbol,
      profit: readCell(cells, profitIndex),
      growth: readCell(cells, growthIndex),
      operation: readCell(cells, operationIndex),
      financialSafety: readCell(cells, safetyIndex),
      cashFlow: readCell(cells, cashFlowIndex),
      rating: readCell(cells, ratingIndex)
    })
  }

  return peers
}

async function fetchMarkdown(symbol, locale) {
  const locales = Array.from(new Set([locale, 'zh-CN', 'en']))
  let lastError = null

  for (const currentLocale of locales) {
    const url = `https://longbridge.com/${currentLocale}/quote/${encodeURIComponent(symbol)}.md`
    try {
      const response = await fetch(url, {
        headers: { Accept: 'text/markdown' }
      })
      if (!response.ok) {
        lastError = new Error(`HTTP ${response.status}`)
        continue
      }
      const text = await response.text()
      if (!String(text || '').trim()) {
        lastError = new Error('empty markdown')
        continue
      }

      return { url, text }
    } catch (error) {
      lastError = error
    }
  }

  throw lastError || new Error('failed to fetch markdown profile')
}

export default async function handler(req, res) {
  if (req.method !== 'POST') {
    res.setHeader('Allow', 'POST')
    return res.status(405).json({ message: 'Method Not Allowed' })
  }

  const body = req.body && typeof req.body === 'object' ? req.body : {}
  const symbol = normalizeSymbol(body.symbol || '')
  if (!symbol) {
    return res.status(400).json({ message: 'symbol is required' })
  }

  try {
    const locale = normalizeLocale(req.headers['accept-language'])
    const { url, text } = await fetchMarkdown(symbol, locale)
    const { frontmatter, body: markdownBody } = parseFrontmatter(text)

    const title = String(frontmatter?.title || extractHeadingTitle(markdownBody) || symbol).trim()
    const overview = extractOverviewSection(markdownBody)
    const fields = extractTableFields(markdownBody)
    const currentIndustry = extractCurrentIndustry(frontmatter, fields)
    const industryPeers = extractIndustryPeers(markdownBody)

    return res.status(200).json({
      symbol,
      title,
      overview,
      sourceUrl: url,
      currentIndustry,
      industryPeers,
      fields
    })
  } catch (error) {
    return res.status(502).json({
      message: error?.message || '公司信息请求失败'
    })
  }
}
