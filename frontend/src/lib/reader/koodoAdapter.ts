import ePub from 'epubjs'
import { getDocument, GlobalWorkerOptions, TextLayer } from 'pdfjs-dist'
import workerSrc from 'pdfjs-dist/build/pdf.worker.min.mjs?url'

export interface ReaderLocationState {
  locator: string
  chapterTitle: string
  pageNumber?: number | null
  percentage?: number | null
}

export interface ReaderSelectionState {
  text: string
  locator: string
  chapterTitle: string
}

export interface ReaderHighlightState {
  id?: number | string
  locator: string
  selectedText: string
  color?: string
}

export interface ReaderTocItem {
  label: string
  href: string
}

export interface ReaderAdapterOptions {
  format: string
  content: ArrayBuffer | string
  initialLocator?: string
  onLocationChange?: (value: ReaderLocationState) => void
  onSelection?: (value: ReaderSelectionState) => void
  disableKoodoRuntime?: boolean
}

interface ReaderEngine {
  mount(container: HTMLElement, initialLocator?: string): Promise<void>
  next(): Promise<void>
  prev(): Promise<void>
  goTo(locator: string): Promise<void>
  setHighlights(highlights: ReaderHighlightState[]): void
  getLocation(): ReaderLocationState
  getToc(): ReaderTocItem[]
  destroy(): void
}

type KoodoRuntime = {
  BookHelper: {
    getRendition: (content: ArrayBuffer, options: Record<string, unknown>, runtime: Record<string, unknown>) => any
  }
  runtime: Record<string, unknown>
}

let workerReady = false

function ensurePdfWorker() {
  if (!workerReady) {
    GlobalWorkerOptions.workerSrc = workerSrc
    workerReady = true
  }
}

function normalizeFormat(format: string): string {
  return String(format || '').trim().toUpperCase()
}

function clampPage(page: number, total: number): number {
  if (!Number.isFinite(page) || total <= 0) {
    return 1
  }
  return Math.max(1, Math.min(total, Math.floor(page)))
}

async function tryLoadKoodoRuntime(): Promise<KoodoRuntime | null> {
  try {
    const runtimeUrl = '/vendor/koodo/kookit.min.js'
    const mod = await import(/* @vite-ignore */ runtimeUrl)
    if (!mod?.BookHelper?.getRendition) {
      return null
    }
    return {
      BookHelper: mod.BookHelper,
      runtime: mod
    }
  } catch {
    return null
  }
}

class KoodoRuntimeEngine implements ReaderEngine {
  private readonly runtime: KoodoRuntime
  private readonly format: string
  private readonly content: ArrayBuffer
  private readonly onLocationChange?: (value: ReaderLocationState) => void
  private readonly onSelection?: (value: ReaderSelectionState) => void

  private rendition: any = null
  private container: HTMLElement | null = null
  private location: ReaderLocationState = {
    locator: '',
    chapterTitle: ''
  }
  private toc: ReaderTocItem[] = []
  private iframeDisposers: Array<() => void> = []

  constructor(runtime: KoodoRuntime, options: ReaderAdapterOptions) {
    this.runtime = runtime
    this.format = normalizeFormat(options.format)
    this.content = options.content as ArrayBuffer
    this.onLocationChange = options.onLocationChange
    this.onSelection = options.onSelection
  }

  async mount(container: HTMLElement, initialLocator?: string): Promise<void> {
    this.container = container
    this.rendition = this.runtime.BookHelper.getRendition(
      this.content,
      {
        format: this.format,
        readerMode: this.format === 'PDF' ? 'scroll' : 'single',
        charset: '',
        animation: '',
        convertChinese: 'no',
        textOrientation: 'horizontal',
        parserRegex: '',
        isDarkMode: 'no',
        backgroundColor: '',
        isMobile: 'no',
        isIndent: 'no',
        isHyphenation: 'no',
        isStartFromEven: 'no',
        isAllowScript: 'no',
        isBionic: 'no',
        password: '',
        scale: 1,
        isConvertPDF: 'no',
        paraSpacingValue: '1.5',
        titleSizeValue: '1.2',
        isScannedPDF: 'no'
      },
      this.runtime.runtime
    )

    await this.rendition.renderTo(container)
    const chapters = this.rendition.getChapter?.() || []
    this.toc = Array.isArray(chapters)
      ? chapters
          .map((item: any, index: number) => ({
            label: String(item?.label || item?.title || `章节 ${index + 1}`),
            href: String(item?.href || index)
          }))
      : []

    this.bindSelectionEvents()
    if (initialLocator) {
      await this.goTo(initialLocator)
    } else {
      this.syncLocation()
    }
    this.rendition.on?.('rendered', () => {
      this.bindSelectionEvents()
      this.syncLocation()
    })
    this.rendition.on?.('relocated', () => {
      this.syncLocation()
    })
  }

  async next(): Promise<void> {
    if (!this.rendition?.next) {
      return
    }
    await this.rendition.next()
    this.syncLocation()
  }

  async prev(): Promise<void> {
    if (!this.rendition?.prev) {
      return
    }
    await this.rendition.prev()
    this.syncLocation()
  }

  async goTo(locator: string): Promise<void> {
    if (!locator || !this.rendition?.goToPosition) {
      return
    }
    await this.rendition.goToPosition(locator)
    this.syncLocation()
  }

  setHighlights(_highlights: ReaderHighlightState[]): void {}

  getLocation(): ReaderLocationState {
    return this.location
  }

  getToc(): ReaderTocItem[] {
    return this.toc
  }

  destroy(): void {
    this.iframeDisposers.forEach((dispose) => dispose())
    this.iframeDisposers = []
    try {
      this.rendition?.removeContent?.()
    } catch {
      // ignore cleanup errors
    }
    this.rendition = null
    this.container = null
  }

  private syncLocation() {
    try {
      const position = this.rendition?.getPosition?.() || {}
      const chapterTitle = String(position?.chapterTitle || '')
      const locator = JSON.stringify(position || {})
      const pageNumber = position?.chapterDocIndex != null
        ? Number(position.chapterDocIndex) + 1
        : null
      const percentage = position?.percentage != null
        ? Number(position.percentage) * 100
        : null

      this.location = {
        locator,
        chapterTitle,
        pageNumber: Number.isFinite(pageNumber as number) ? pageNumber : null,
        percentage: Number.isFinite(percentage as number) ? percentage : null
      }
      this.onLocationChange?.(this.location)
    } catch {
      // ignore location parse errors
    }
  }

  private bindSelectionEvents() {
    this.iframeDisposers.forEach((dispose) => dispose())
    this.iframeDisposers = []
    if (!this.container || !this.onSelection) {
      return
    }

    const iframes = Array.from(this.container.querySelectorAll('iframe'))
    if (iframes.length === 0) {
      const handler = () => {
        const text = window.getSelection()?.toString().trim() || ''
        if (!text) {
          return
        }
        const location = this.getLocation()
        this.onSelection?.({
          text,
          locator: location.locator,
          chapterTitle: location.chapterTitle
        })
      }
      this.container.addEventListener('mouseup', handler)
      this.iframeDisposers.push(() => this.container?.removeEventListener('mouseup', handler))
      return
    }

    iframes.forEach((iframe) => {
      const doc = iframe.contentDocument
      if (!doc) {
        return
      }
      const handler = () => {
        const text = doc.getSelection()?.toString().trim() || ''
        if (!text) {
          return
        }
        const location = this.getLocation()
        this.onSelection?.({
          text,
          locator: location.locator,
          chapterTitle: location.chapterTitle
        })
      }
      doc.addEventListener('mouseup', handler)
      this.iframeDisposers.push(() => doc.removeEventListener('mouseup', handler))
    })
  }
}

class EpubEngine implements ReaderEngine {
  private readonly content: ArrayBuffer
  private readonly onLocationChange?: (value: ReaderLocationState) => void
  private readonly onSelection?: (value: ReaderSelectionState) => void

  private book: any = null
  private rendition: any = null
  private location: ReaderLocationState = {
    locator: '',
    chapterTitle: ''
  }
  private toc: ReaderTocItem[] = []

  constructor(options: ReaderAdapterOptions) {
    this.content = options.content as ArrayBuffer
    this.onLocationChange = options.onLocationChange
    this.onSelection = options.onSelection
  }

  async mount(container: HTMLElement, initialLocator?: string): Promise<void> {
    this.book = ePub(this.content)
    this.rendition = this.book.renderTo(container, {
      width: '100%',
      height: '100%',
      spread: 'none'
    })

    this.rendition.on('relocated', (location: any) => {
      const chapterTitle = String(location?.start?.href || '')
      const percentage = typeof location?.start?.percentage === 'number'
        ? Number(location.start.percentage) * 100
        : null
      this.location = {
        locator: String(location?.start?.cfi || ''),
        chapterTitle,
        percentage
      }
      this.onLocationChange?.(this.location)
    })

    this.rendition.on('selected', (cfiRange: string, contents: any) => {
      const selectedText = String(contents?.window?.getSelection?.()?.toString() || '').trim()
      if (!selectedText) {
        return
      }
      this.onSelection?.({
        text: selectedText,
        locator: cfiRange || this.location.locator,
        chapterTitle: this.location.chapterTitle
      })
    })

    await this.rendition.display(initialLocator || undefined)
    const navigation = await this.book.loaded?.navigation
    const toc = navigation?.toc || []
    this.toc = Array.isArray(toc)
      ? toc.map((item: any) => ({
          label: String(item?.label || item?.id || '章节'),
          href: String(item?.href || '')
        }))
      : []
  }

  async next(): Promise<void> {
    await this.rendition?.next?.()
  }

  async prev(): Promise<void> {
    await this.rendition?.prev?.()
  }

  async goTo(locator: string): Promise<void> {
    if (!locator) {
      return
    }
    await this.rendition?.display?.(locator)
  }

  setHighlights(_highlights: ReaderHighlightState[]): void {}

  getLocation(): ReaderLocationState {
    return this.location
  }

  getToc(): ReaderTocItem[] {
    return this.toc
  }

  destroy(): void {
    try {
      this.rendition?.destroy?.()
      this.book?.destroy?.()
    } catch {
      // ignore cleanup errors
    }
    this.rendition = null
    this.book = null
  }
}

class PdfEngine implements ReaderEngine {
  private readonly content: ArrayBuffer
  private readonly onLocationChange?: (value: ReaderLocationState) => void
  private readonly onSelection?: (value: ReaderSelectionState) => void

  private pdf: any = null
  private pageNumber = 1
  private totalPages = 1
  private location: ReaderLocationState = {
    locator: 'page:1',
    chapterTitle: '第 1 页',
    pageNumber: 1,
    percentage: 100
  }
  private toc: ReaderTocItem[] = []
  private container: HTMLElement | null = null
  private pageFrame: HTMLDivElement | null = null
  private canvas: HTMLCanvasElement | null = null
  private textLayer: HTMLDivElement | null = null
  private textLayerRenderer: InstanceType<typeof TextLayer> | null = null
  private selectionDisposer: (() => void) | null = null
  private highlights: ReaderHighlightState[] = []

  constructor(options: ReaderAdapterOptions) {
    this.content = options.content as ArrayBuffer
    this.onLocationChange = options.onLocationChange
    this.onSelection = options.onSelection
  }

  async mount(container: HTMLElement, initialLocator?: string): Promise<void> {
    this.container = container
    this.pdf = await this.openDocument()
    this.totalPages = Number(this.pdf.numPages || 1)
    this.toc = Array.from({ length: this.totalPages }, (_, index) => ({
      label: `第 ${index + 1} 页`,
      href: `page:${index + 1}`
    }))

    container.innerHTML = ''
    const wrapper = document.createElement('div')
    wrapper.className = 'reader-pdf-wrapper'
    wrapper.style.display = 'block'

    this.pageFrame = document.createElement('div')
    this.pageFrame.className = 'reader-pdf-page'

    this.canvas = document.createElement('canvas')
    this.canvas.className = 'reader-pdf-canvas'
    this.pageFrame.appendChild(this.canvas)

    this.textLayer = document.createElement('div')
    this.textLayer.className = 'textLayer reader-pdf-text-layer'
    this.pageFrame.appendChild(this.textLayer)
    wrapper.appendChild(this.pageFrame)
    container.appendChild(wrapper)

    const initialPage = initialLocator?.startsWith('page:')
      ? Number(initialLocator.replace('page:', ''))
      : 1
    await this.renderPage(clampPage(initialPage, this.totalPages))
    this.bindSelection()
  }

  async next(): Promise<void> {
    await this.renderPage(clampPage(this.pageNumber + 1, this.totalPages))
  }

  async prev(): Promise<void> {
    await this.renderPage(clampPage(this.pageNumber - 1, this.totalPages))
  }

  async goTo(locator: string): Promise<void> {
    if (!locator?.startsWith('page:')) {
      return
    }
    const page = Number(locator.replace('page:', ''))
    await this.renderPage(clampPage(page, this.totalPages))
  }

  setHighlights(highlights: ReaderHighlightState[]): void {
    this.highlights = Array.isArray(highlights) ? highlights : []
    this.applyHighlights()
  }

  getLocation(): ReaderLocationState {
    return this.location
  }

  getToc(): ReaderTocItem[] {
    return this.toc
  }

  destroy(): void {
    this.selectionDisposer?.()
    this.selectionDisposer = null
    this.textLayerRenderer?.cancel()
    this.textLayerRenderer = null
    this.container = null
    this.pageFrame = null
    this.canvas = null
    this.textLayer = null
    this.pdf = null
  }

  private async renderPage(pageNumber: number): Promise<void> {
    if (!this.pdf || !this.container || !this.pageFrame || !this.canvas || !this.textLayer) {
      return
    }

    const page = await this.pdf.getPage(pageNumber)
    const baseViewport = page.getViewport({ scale: 1 })
    const availableWidth = Math.max(320, this.container.clientWidth - 28)
    const scale = Math.max(0.85, Math.min(1.75, availableWidth / baseViewport.width))
    const viewport = page.getViewport({ scale })
    const outputScale = window.devicePixelRatio || 1

    this.pageFrame.style.width = `${viewport.width}px`
    this.pageFrame.style.height = `${viewport.height}px`
    this.textLayer.style.setProperty('--total-scale-factor', String(scale))
    this.textLayer.innerHTML = ''
    this.textLayerRenderer?.cancel()
    this.textLayerRenderer = null

    this.canvas.width = Math.floor(viewport.width * outputScale)
    this.canvas.height = Math.floor(viewport.height * outputScale)
    this.canvas.style.width = `${viewport.width}px`
    this.canvas.style.height = `${viewport.height}px`
    const context = this.canvas.getContext('2d')
    if (!context) {
      return
    }

    await page.render({
      canvasContext: context,
      viewport,
      transform: outputScale === 1 ? undefined : [outputScale, 0, 0, outputScale, 0, 0]
    }).promise
    const textContent = await page.getTextContent()
    this.textLayerRenderer = new TextLayer({
      textContentSource: textContent,
      container: this.textLayer,
      viewport
    })
    await this.textLayerRenderer.render()
    this.applyHighlights()

    this.pageNumber = pageNumber
    const percentage = this.totalPages > 0
      ? Number(((pageNumber / this.totalPages) * 100).toFixed(2))
      : null
    this.location = {
      locator: `page:${pageNumber}`,
      chapterTitle: `第 ${pageNumber} 页`,
      pageNumber,
      percentage
    }
    this.onLocationChange?.(this.location)
  }

  private bindSelection() {
    if (!this.textLayer || !this.onSelection) {
      return
    }
    const handler = () => {
      const text = this.readSelectionText()
      if (!text) {
        return
      }
      this.onSelection?.({
        text,
        locator: this.location.locator,
        chapterTitle: this.location.chapterTitle
      })
    }
    this.textLayer.addEventListener('mouseup', handler)
    this.textLayer.addEventListener('keyup', handler)
    document.addEventListener('selectionchange', handler)
    this.selectionDisposer = () => {
      this.textLayer?.removeEventListener('mouseup', handler)
      this.textLayer?.removeEventListener('keyup', handler)
      document.removeEventListener('selectionchange', handler)
    }
  }

  private readSelectionText(): string {
    if (!this.textLayer) {
      return ''
    }
    const selection = window.getSelection()
    if (!selection || selection.rangeCount === 0) {
      return ''
    }
    const anchorNode = selection.anchorNode
    const focusNode = selection.focusNode
    if (!anchorNode || !focusNode) {
      return ''
    }
    if (!this.textLayer.contains(anchorNode) && !this.textLayer.contains(focusNode)) {
      return ''
    }
    return selection
      .toString()
      .replace(/\u00a0/g, ' ')
      .replace(/[ \t]+\n/g, '\n')
      .replace(/[ \t]{2,}/g, ' ')
      .trim()
  }

  private applyHighlights() {
    if (!this.textLayer) {
      return
    }

    this.clearHighlightMarks()
    const pageLocator = `page:${this.pageNumber}`
    const pageHighlights = this.highlights.filter((item) =>
      String(item?.locator || '').trim().toLowerCase() === pageLocator
    )
    if (pageHighlights.length === 0) {
      return
    }

    const spans = Array.from(this.textLayer.querySelectorAll<HTMLSpanElement>('span'))
      .filter((node) => !node.classList.contains('endOfContent'))
    if (spans.length === 0) {
      return
    }

    const segments = spans
      .map((span) => ({
        span,
        text: this.normalizeText(span.textContent || '')
      }))
      .filter((segment) => segment.text.length > 0)

    if (segments.length === 0) {
      return
    }

    let cursor = 0
    const mapped = segments.map((segment) => {
      const start = cursor
      const end = cursor + segment.text.length
      cursor = end
      return {
        ...segment,
        start,
        end
      }
    })
    const fullText = mapped.map((segment) => segment.text).join('')
    if (!fullText) {
      return
    }

    pageHighlights.forEach((highlight) => {
      const target = this.normalizeText(highlight.selectedText || '')
      if (!target) {
        return
      }

      const start = fullText.indexOf(target)
      if (start < 0) {
        return
      }
      const end = start + target.length
      const color = this.resolveHighlightColor(highlight.color)

      mapped
        .filter((segment) => segment.end > start && segment.start < end)
        .forEach((segment) => {
          segment.span.dataset.readerHighlight = '1'
          segment.span.classList.add('reader-pdf-saved-highlight')
          segment.span.style.backgroundColor = color
          segment.span.style.borderRadius = '2px'
        })
    })
  }

  private clearHighlightMarks() {
    if (!this.textLayer) {
      return
    }

    const nodes = this.textLayer.querySelectorAll<HTMLSpanElement>('span[data-reader-highlight="1"]')
    nodes.forEach((node) => {
      node.removeAttribute('data-reader-highlight')
      node.classList.remove('reader-pdf-saved-highlight')
      node.style.removeProperty('background-color')
      node.style.removeProperty('border-radius')
    })
  }

  private normalizeText(value: string): string {
    return String(value || '').replace(/\s+/g, '')
  }

  private resolveHighlightColor(color?: string): string {
    switch (String(color || 'yellow').toLowerCase()) {
      case 'green':
        return 'rgb(34 197 94 / 34%)'
      case 'blue':
        return 'rgb(59 130 246 / 30%)'
      case 'pink':
        return 'rgb(244 114 182 / 30%)'
      case 'orange':
        return 'rgb(251 146 60 / 35%)'
      case 'purple':
        return 'rgb(168 85 247 / 28%)'
      default:
        return 'rgb(250 204 21 / 40%)'
    }
  }

  private async openDocument() {
    ensurePdfWorker()
    try {
      const loadingTask = getDocument({ data: this.content })
      return await loadingTask.promise
    } catch (error) {
      // Some production nginx/browser combinations fail to load the module worker.
      // Fallback keeps PoC usable by parsing on main thread.
      console.warn('PDF worker init failed, retrying with disableWorker mode.', error)
      const fallbackParams: any = { data: this.content, disableWorker: true }
      const fallbackTask = getDocument(fallbackParams)
      return await fallbackTask.promise
    }
  }
}

class MarkdownEngine implements ReaderEngine {
  private readonly markdown: string
  private readonly onSelection?: (value: ReaderSelectionState) => void
  private location: ReaderLocationState = {
    locator: 'md:0',
    chapterTitle: '',
    pageNumber: null,
    percentage: null
  }
  private selectionDisposer: (() => void) | null = null

  constructor(options: ReaderAdapterOptions) {
    this.markdown = String(options.content || '')
    this.onSelection = options.onSelection
  }

  async mount(container: HTMLElement): Promise<void> {
    container.innerHTML = ''
    const pre = document.createElement('pre')
    pre.className = 'reader-md-content'
    pre.style.whiteSpace = 'pre-wrap'
    pre.style.wordBreak = 'break-word'
    pre.style.lineHeight = '1.8'
    pre.style.margin = '0'
    pre.style.padding = '16px'
    pre.style.border = '1px solid var(--qt-border)'
    pre.style.borderRadius = '8px'
    pre.style.background = 'var(--qt-card-bg)'
    pre.textContent = this.markdown
    container.appendChild(pre)

    if (this.onSelection) {
      const handler = () => {
        const text = window.getSelection()?.toString().trim() || ''
        if (!text) {
          return
        }
        this.onSelection?.({
          text,
          locator: this.location.locator,
          chapterTitle: this.location.chapterTitle
        })
      }
      pre.addEventListener('mouseup', handler)
      this.selectionDisposer = () => pre.removeEventListener('mouseup', handler)
    }
  }

  async next(): Promise<void> {}
  async prev(): Promise<void> {}
  async goTo(_locator: string): Promise<void> {}
  setHighlights(_highlights: ReaderHighlightState[]): void {}

  getLocation(): ReaderLocationState {
    return this.location
  }

  getToc(): ReaderTocItem[] {
    return []
  }

  destroy(): void {
    this.selectionDisposer?.()
    this.selectionDisposer = null
  }
}

export class KoodoAdapter {
  private readonly engine: ReaderEngine

  private constructor(engine: ReaderEngine) {
    this.engine = engine
  }

  static async create(options: ReaderAdapterOptions): Promise<KoodoAdapter> {
    const format = normalizeFormat(options.format)
    if (
      !options.disableKoodoRuntime
      && (format === 'EPUB' || format === 'MD')
      && options.content instanceof ArrayBuffer
    ) {
      const runtime = await tryLoadKoodoRuntime()
      if (runtime) {
        try {
          return new KoodoAdapter(new KoodoRuntimeEngine(runtime, options))
        } catch {
          // fall through to local engines
        }
      }
    }

    if (format === 'EPUB' && options.content instanceof ArrayBuffer) {
      return new KoodoAdapter(new EpubEngine(options))
    }
    if (format === 'PDF' && options.content instanceof ArrayBuffer) {
      return new KoodoAdapter(new PdfEngine(options))
    }
    return new KoodoAdapter(new MarkdownEngine(options))
  }

  mount(container: HTMLElement, initialLocator?: string): Promise<void> {
    return this.engine.mount(container, initialLocator)
  }

  next(): Promise<void> {
    return this.engine.next()
  }

  prev(): Promise<void> {
    return this.engine.prev()
  }

  goTo(locator: string): Promise<void> {
    return this.engine.goTo(locator)
  }

  setHighlights(highlights: ReaderHighlightState[]): void {
    this.engine.setHighlights(highlights)
  }

  getLocation(): ReaderLocationState {
    return this.engine.getLocation()
  }

  getToc(): ReaderTocItem[] {
    return this.engine.getToc()
  }

  destroy(): void {
    this.engine.destroy()
  }
}
