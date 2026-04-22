export interface AiMarkdownHeading {
  id: string
  title: string
  level: number
}

export interface AiMarkdownResult {
  html: string
  toc: AiMarkdownHeading[]
}

function escapeHtml(raw: string): string {
  return raw
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

function safeLink(url: string): string {
  const trimmed = String(url || '').trim()
  if (/^https?:\/\//i.test(trimmed)) {
    return trimmed
  }
  return '#'
}

function formatInlineMarkdown(raw: string): string {
  const escaped = escapeHtml(raw)
  return escaped
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_match, label, url) => {
      const safeUrl = safeLink(url)
      return `<a href="${safeUrl}" target="_blank" rel="noopener noreferrer">${label}</a>`
    })
    .replace(/`([^`]+?)`/g, '<code>$1</code>')
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/~~(.+?)~~/g, '<del>$1</del>')
    .replace(/\*(.+?)\*/g, '<em>$1</em>')
}

function headingId(prefix: string, title: string, index: number): string {
  const normalized = title
    .toLowerCase()
    .replace(/[^\w\u4e00-\u9fa5-]+/g, '-')
    .replace(/-{2,}/g, '-')
    .replace(/^-+|-+$/g, '')

  return `${prefix}-${normalized || 'section'}-${index}`
}

export function parseAiMarkdown(raw: string, tocPrefix = 'ai'): AiMarkdownResult {
  if (!String(raw || '').trim()) {
    return { html: '', toc: [] }
  }

  const lines = raw.split(/\r?\n/)
  const toc: AiMarkdownHeading[] = []
  const htmlParts: string[] = []
  const paragraphLines: string[] = []
  let inUnorderedList = false
  let inOrderedList = false
  let inCodeBlock = false
  let codeLanguage = ''
  const codeLines: string[] = []
  let headingIndex = 0

  const flushParagraph = () => {
    if (paragraphLines.length === 0) {
      return
    }

    const paragraph = paragraphLines.map((item) => formatInlineMarkdown(item)).join('<br/>')
    htmlParts.push(`<p>${paragraph}</p>`)
    paragraphLines.length = 0
  }

  const closeLists = () => {
    if (inUnorderedList) {
      htmlParts.push('</ul>')
      inUnorderedList = false
    }

    if (inOrderedList) {
      htmlParts.push('</ol>')
      inOrderedList = false
    }
  }

  const flushCodeBlock = () => {
    if (!inCodeBlock) {
      return
    }

    const languageClass = codeLanguage ? ` class="language-${escapeHtml(codeLanguage)}"` : ''
    const code = escapeHtml(codeLines.join('\n'))
    htmlParts.push(`<pre><code${languageClass}>${code}</code></pre>`)
    inCodeBlock = false
    codeLanguage = ''
    codeLines.length = 0
  }

  for (const line of lines) {
    const trimmed = line.trim()

    const codeFence = trimmed.match(/^```([\w-]+)?$/)
    if (codeFence) {
      flushParagraph()
      closeLists()

      if (inCodeBlock) {
        flushCodeBlock()
      } else {
        inCodeBlock = true
        codeLanguage = codeFence[1] || ''
      }
      continue
    }

    if (inCodeBlock) {
      codeLines.push(line)
      continue
    }

    if (!trimmed) {
      flushParagraph()
      closeLists()
      continue
    }

    const headingMatch = trimmed.match(/^(#{1,6})\s+(.+)$/)
    if (headingMatch) {
      flushParagraph()
      closeLists()
      const level = headingMatch[1].length
      const title = headingMatch[2].trim()
      const id = headingId(tocPrefix, title, headingIndex++)
      toc.push({ id, title, level })
      htmlParts.push(`<h${level} id="${id}">${formatInlineMarkdown(title)}</h${level}>`)
      continue
    }

    const quoteMatch = trimmed.match(/^>\s?(.+)$/)
    if (quoteMatch) {
      flushParagraph()
      closeLists()
      htmlParts.push(`<blockquote>${formatInlineMarkdown(quoteMatch[1])}</blockquote>`)
      continue
    }

    const unorderedMatch = trimmed.match(/^[-*]\s+(.+)$/)
    if (unorderedMatch) {
      flushParagraph()
      if (inOrderedList) {
        htmlParts.push('</ol>')
        inOrderedList = false
      }

      if (!inUnorderedList) {
        htmlParts.push('<ul>')
        inUnorderedList = true
      }

      htmlParts.push(`<li>${formatInlineMarkdown(unorderedMatch[1])}</li>`)
      continue
    }

    const orderedMatch = trimmed.match(/^\d+\.\s+(.+)$/)
    if (orderedMatch) {
      flushParagraph()
      if (inUnorderedList) {
        htmlParts.push('</ul>')
        inUnorderedList = false
      }

      if (!inOrderedList) {
        htmlParts.push('<ol>')
        inOrderedList = true
      }

      htmlParts.push(`<li>${formatInlineMarkdown(orderedMatch[1])}</li>`)
      continue
    }

    paragraphLines.push(trimmed)
  }

  flushParagraph()
  closeLists()
  flushCodeBlock()

  return {
    html: htmlParts.join(''),
    toc
  }
}
