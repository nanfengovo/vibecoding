<template>
  <div class="reader-viewer-page">
    <div class="page-header">
      <div class="header-left">
        <el-button text @click="goBack">返回书架</el-button>
        <h1>{{ book?.title || '阅读器' }}</h1>
        <el-tag v-if="book" size="small">{{ book.format }}</el-tag>
      </div>
      <div class="header-actions">
        <el-select
          v-if="toc.length > 0"
          v-model="tocLocator"
          placeholder="目录跳转"
          filterable
          clearable
          style="width: 280px"
          @change="goToToc"
        >
          <el-option
            v-for="item in toc"
            :key="item.href"
            :label="item.label"
            :value="item.href"
          />
        </el-select>
        <el-button @click="prevPage">上一页</el-button>
        <el-button @click="nextPage">下一页</el-button>
        <el-button link type="primary" @click="showNotices = true">Open Source Notices</el-button>
      </div>
    </div>

    <div class="reader-layout">
      <section class="viewer card" v-loading="loading">
        <div class="viewer-meta">
          <span>定位：{{ location.locator || '未定位' }}</span>
          <span v-if="location.chapterTitle">章节：{{ location.chapterTitle }}</span>
          <span v-if="location.pageNumber">页码：{{ location.pageNumber }}</span>
          <span v-if="location.percentage !== null && location.percentage !== undefined">
            进度：{{ Number(location.percentage).toFixed(2) }}%
          </span>
        </div>
        <div ref="viewerHostRef" class="viewer-host" />
      </section>

      <aside class="side-panel">
        <section class="card">
          <div class="section-title">选中文本联动</div>
          <el-input
            v-model="selectedText"
            type="textarea"
            :rows="6"
            placeholder="在阅读区域选中内容后会自动填充"
          />
          <el-input
            v-model="highlightNote"
            class="top-gap"
            placeholder="可选：这段的理解/备注"
          />
          <div class="inline-actions top-gap">
            <el-select v-model="highlightColor" style="width: 120px">
              <el-option label="黄色" value="yellow" />
              <el-option label="绿色" value="green" />
              <el-option label="蓝色" value="blue" />
              <el-option label="粉色" value="pink" />
              <el-option label="橙色" value="orange" />
              <el-option label="紫色" value="purple" />
            </el-select>
            <el-button type="primary" :disabled="!selectedText.trim()" @click="saveHighlight">保存划线</el-button>
          </div>

          <el-divider />

          <el-input
            v-model="question"
            type="textarea"
            :rows="3"
            placeholder="围绕当前选中文本提问"
          />
          <div class="inline-actions top-gap">
            <el-select v-model="selectedProviderId" placeholder="模型源" style="width: 160px">
              <el-option
                v-for="provider in aiProviders"
                :key="provider.id"
                :label="provider.name"
                :value="provider.id"
              />
            </el-select>
            <el-select v-model="selectedModel" placeholder="模型" style="width: 180px">
              <el-option
                v-for="model in modelOptions"
                :key="model"
                :label="model"
                :value="model"
              />
            </el-select>
            <el-button :loading="optimizingPrompt" :disabled="!question.trim()" @click="optimizeQuestion">
              优化提示词
            </el-button>
          </div>
          <div class="inline-actions top-gap">
            <el-button type="primary" :loading="askingAi" :disabled="!selectedText.trim()" @click="askAi">
              问 AI
            </el-button>
            <el-button :disabled="!selectedText.trim()" @click="saveMemory">保存为记忆</el-button>
          </div>
          <div class="inline-actions top-gap">
            <el-select v-model="selectedKbId" placeholder="选择知识库" style="width: 180px">
              <el-option
                v-for="kb in knowledgeBases"
                :key="kb.id"
                :label="kb.name"
                :value="kb.id"
              />
            </el-select>
            <el-button :disabled="!selectedText.trim() || !selectedKbId" @click="importToKnowledge">
              入知识库
            </el-button>
          </div>
          <div v-if="aiAnswer" class="ai-answer top-gap">
            <div class="label">AI 回复</div>
            <div class="answer-content" v-html="aiAnswerHtml" />
          </div>
        </section>

        <section class="card">
          <div class="section-title">本书划线</div>
          <el-table :data="highlights" height="320">
            <el-table-column label="内容" min-width="200">
              <template #default="{ row }">
                <div class="highlight-text">{{ row.selectedText }}</div>
                <div class="highlight-note" v-if="row.note">{{ row.note }}</div>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="90">
              <template #default="{ row }">
                <el-button link type="danger" @click="removeHighlight(row.id)">删除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </section>
      </aside>
    </div>

    <el-dialog v-model="showNotices" title="Open Source Notices" width="680px">
      <p>
        This reader PoC references Koodo Reader ideas and attempts runtime integration with
        <a href="https://github.com/koodo-reader/koodo-reader" target="_blank" rel="noopener">koodo-reader/koodo-reader</a>.
      </p>
      <p>
        License: AGPL-3.0.
        <a href="https://github.com/koodo-reader/koodo-reader/blob/dev/LICENSE" target="_blank" rel="noopener">View License</a>
      </p>
      <p>Attribution and license copy are included in this repository under `third_party/koodo-reader/`.</p>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { aiApi, configApi, knowledgeApi, readerApi } from '@/api'
import { parseAiMarkdown } from '@/utils/aiMarkdown'
import type { AiProviderConfig, KnowledgeBase, ReaderBook, ReaderHighlight } from '@/types'
import {
  KoodoAdapter,
  type ReaderHighlightState,
  type ReaderLocationState,
  type ReaderTocItem
} from '@/lib/reader/koodoAdapter'
import { normalizeAiProviders, parseModelCandidates, resolvePreferredProviderId } from '@/lib/ai/providerModel'

const route = useRoute()
const router = useRouter()
const viewerHostRef = ref<HTMLElement | null>(null)

const loading = ref(false)
const askingAi = ref(false)
const optimizingPrompt = ref(false)
const book = ref<ReaderBook | null>(null)
const location = ref<ReaderLocationState>({
  locator: '',
  chapterTitle: '',
  pageNumber: null,
  percentage: null
})
const toc = ref<ReaderTocItem[]>([])
const tocLocator = ref('')
const selectedText = ref('')
const selectedLocator = ref('')
const highlightNote = ref('')
const highlightColor = ref('yellow')
const question = ref('请基于这段内容给出结构化解读和风险提示。')
const aiAnswer = ref('')
const highlights = ref<ReaderHighlight[]>([])
const knowledgeBases = ref<KnowledgeBase[]>([])
const selectedKbId = ref<number | null>(null)
const aiProviders = ref<AiProviderConfig[]>([])
const selectedProviderId = ref('')
const selectedModel = ref('')
const showNotices = ref(false)

let adapter: KoodoAdapter | null = null
let progressTimer: ReturnType<typeof setTimeout> | null = null

const bookId = computed(() => Number(route.params.bookId))
const aiAnswerHtml = computed(() => parseAiMarkdown(aiAnswer.value || '', 'reader-ai-answer').html)
const currentProvider = computed(() =>
  aiProviders.value.find((item) => item.id === selectedProviderId.value) || aiProviders.value[0] || null
)
const modelOptions = computed(() => {
  const models = parseModelCandidates(currentProvider.value?.model || '')
  return models.length > 0 ? models : ['gpt-5-mini']
})

watch(currentProvider, () => {
  if (!modelOptions.value.includes(selectedModel.value)) {
    selectedModel.value = modelOptions.value[0] || 'gpt-5-mini'
  }
})

function goBack() {
  router.push('/reader')
}

async function loadHighlights() {
  if (!bookId.value) {
    return
  }
  highlights.value = await readerApi.listHighlights(bookId.value)
  applyHighlightOverlay()
}

function applyHighlightOverlay() {
  if (!adapter) {
    return
  }

  const rows: ReaderHighlightState[] = highlights.value.map((item) => ({
    id: item.id,
    locator: item.locator || '',
    selectedText: item.selectedText || '',
    color: item.color || 'yellow'
  }))
  adapter.setHighlights(rows)
}

async function loadAiProviders() {
  try {
    const config = await configApi.get()
    aiProviders.value = normalizeAiProviders(config?.openAi)
    selectedProviderId.value = resolvePreferredProviderId(aiProviders.value, config?.openAi?.activeProviderId)
    if (!modelOptions.value.includes(selectedModel.value)) {
      selectedModel.value = modelOptions.value[0] || 'gpt-5-mini'
    }
  } catch {
    aiProviders.value = []
    selectedProviderId.value = ''
    selectedModel.value = 'gpt-5-mini'
  }
}

async function persistProgress() {
  if (!bookId.value || !book.value) {
    return
  }
  try {
    await readerApi.saveProgress(bookId.value, {
      locator: location.value.locator,
      chapterTitle: location.value.chapterTitle,
      pageNumber: location.value.pageNumber ?? null,
      percentage: location.value.percentage ?? null
    })
  } catch {
    // Ignore transient save failures.
  }
}

function scheduleProgressSave() {
  if (!bookId.value) {
    return
  }
  if (progressTimer) {
    clearTimeout(progressTimer)
  }
  progressTimer = setTimeout(() => {
    progressTimer = null
    void persistProgress()
  }, 900)
}

function flushProgressSave() {
  if (progressTimer) {
    clearTimeout(progressTimer)
    progressTimer = null
  }
  void persistProgress()
}

async function initViewer() {
  if (!bookId.value || !viewerHostRef.value) {
    return
  }
  loading.value = true
  try {
    book.value = await readerApi.getBook(bookId.value)
    const progress = await readerApi.getProgress(bookId.value)
    const blob = await readerApi.getBookContent(bookId.value)
    const format = String(book.value?.format || '').toUpperCase()
    const content = format === 'MD' ? await blob.text() : await blob.arrayBuffer()

    const createAdapter = async (disableKoodoRuntime: boolean) => {
      return await KoodoAdapter.create({
        format,
        content,
        disableKoodoRuntime,
        initialLocator: progress?.locator || '',
        onLocationChange: (nextLocation) => {
          location.value = nextLocation
          scheduleProgressSave()
        },
        onSelection: (selection) => {
          selectedText.value = selection.text
          selectedLocator.value = selection.locator
          if (selection.chapterTitle && !location.value.chapterTitle) {
            location.value = {
              ...location.value,
              chapterTitle: selection.chapterTitle
            }
          }
        }
      })
    }

    try {
      adapter = await createAdapter(false)
      await adapter.mount(viewerHostRef.value, progress?.locator || '')
    } catch (runtimeError) {
      adapter?.destroy()
      adapter = null
      adapter = await createAdapter(true)
      await adapter.mount(viewerHostRef.value, progress?.locator || '')
      ElMessage.warning('已切换到兼容阅读模式')
      console.warn('Reader runtime fallback to local engine:', runtimeError)
    }

    toc.value = adapter.getToc()
    if (progress) {
      location.value = {
        locator: progress.locator || location.value.locator,
        chapterTitle: progress.chapterTitle || location.value.chapterTitle,
        pageNumber: progress.pageNumber ?? location.value.pageNumber ?? null,
        percentage: progress.percentage ?? location.value.percentage ?? null
      }
    }

    await loadHighlights()
    knowledgeBases.value = await knowledgeApi.list()
    if (knowledgeBases.value.length > 0) {
      selectedKbId.value = knowledgeBases.value[0].id
    }
  } catch (error: any) {
    const status = Number(error?.response?.status || 0)
    const message = error?.response?.data?.message
      || error?.response?.data?.detail
      || error?.response?.data?.title
      || error?.message
      || ((status === 404 || status === 410)
        ? '图书内容不存在，可能文件已丢失，请删除后重新上传。'
        : '阅读器初始化失败')
    ElMessage.error(message)
    goBack()
  } finally {
    loading.value = false
  }
}

async function prevPage() {
  await adapter?.prev()
  if (adapter) {
    location.value = adapter.getLocation()
    applyHighlightOverlay()
    scheduleProgressSave()
  }
}

async function nextPage() {
  await adapter?.next()
  if (adapter) {
    location.value = adapter.getLocation()
    applyHighlightOverlay()
    scheduleProgressSave()
  }
}

async function goToToc(href?: string) {
  if (!href || !adapter) {
    return
  }
  await adapter.goTo(href)
  location.value = adapter.getLocation()
  applyHighlightOverlay()
  scheduleProgressSave()
}

async function saveHighlight() {
  if (!bookId.value || !selectedText.value.trim()) {
    ElMessage.warning('请先在阅读区选中内容')
    return
  }
  await readerApi.createHighlight(bookId.value, {
    selectedText: selectedText.value.trim(),
    locator: selectedLocator.value || location.value.locator,
    chapterTitle: location.value.chapterTitle || '',
    note: highlightNote.value.trim(),
    color: highlightColor.value
  })
  ElMessage.success('已保存并高亮')
  highlightNote.value = ''
  await loadHighlights()
}

async function removeHighlight(highlightId: number) {
  if (!bookId.value) {
    return
  }
  await readerApi.deleteHighlight(bookId.value, highlightId)
  ElMessage.success('划线已删除')
  await loadHighlights()
}

async function askAi() {
  if (!book.value || !selectedText.value.trim()) {
    ElMessage.warning('请先选中文本再提问')
    return
  }
  const ask = question.value.trim()
  if (!ask) {
    ElMessage.warning('请输入问题')
    return
  }

  askingAi.value = true
  try {
    const result = await aiApi.chat({
      question: ask,
      providerId: selectedProviderId.value || undefined,
      model: selectedModel.value || undefined,
      readerContext: {
        bookId: book.value.id,
        title: book.value.title,
        format: book.value.format,
        locator: selectedLocator.value || location.value.locator,
        selectedText: selectedText.value.trim()
      }
    })
    aiAnswer.value = result.content || ''
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || 'AI 联动失败，请稍后重试')
  } finally {
    askingAi.value = false
  }
}

async function optimizeQuestion() {
  const ask = question.value.trim()
  if (!ask) {
    ElMessage.warning('请先输入问题')
    return
  }
  if (optimizingPrompt.value) {
    return
  }

  optimizingPrompt.value = true
  try {
    const result = await aiApi.optimizePrompt({
      question: ask,
      providerId: selectedProviderId.value || undefined,
      model: selectedModel.value || undefined,
      scene: 'reader',
      contextText: selectedText.value.trim() || undefined
    })
    const optimized = String(result?.optimizedPrompt || '').trim()
    if (!optimized) {
      throw new Error('优化结果为空')
    }
    question.value = optimized
    ElMessage.success('提示词已优化')
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || error?.message || '提示词优化失败')
  } finally {
    optimizingPrompt.value = false
  }
}

async function saveMemory() {
  if (!book.value || !selectedText.value.trim()) {
    ElMessage.warning('请先选中文本')
    return
  }

  await aiApi.createMemory({
    type: 'reader_highlight',
    title: `阅读摘录：${book.value.title}`,
    content: selectedText.value.trim(),
    tags: `reader,${book.value.format.toLowerCase()}`,
    sourceType: 'reader_highlight',
    sourceUrl: `reader://book/${book.value.id}#${encodeURIComponent(selectedLocator.value || location.value.locator)}`,
    sourceRef: `reader:${book.value.id}:${selectedLocator.value || location.value.locator}`,
    providerId: selectedProviderId.value || undefined,
    model: selectedModel.value || undefined,
    knowledgeBaseId: selectedKbId.value || undefined
  })
  ElMessage.success('已保存到 AI 记忆')
}

async function importToKnowledge() {
  if (!book.value || !selectedKbId.value || !selectedText.value.trim()) {
    ElMessage.warning('请先选中文本并选择知识库')
    return
  }

  const locator = selectedLocator.value || location.value.locator
  const markdown = `# ${book.value.title} - 阅读摘录\n\n> ${selectedText.value.replace(/\n/g, '\n> ')}\n\n- 定位：${locator}\n- 章节：${location.value.chapterTitle || '未知'}`
  await knowledgeApi.importMarkdown(selectedKbId.value, {
    title: `${book.value.title} - 摘录`,
    markdown,
    sourceType: 'reader_highlight',
    sourceUrl: `reader://book/${book.value.id}#${encodeURIComponent(locator)}`
  })
  ElMessage.success('已导入知识库')
}

onMounted(async () => {
  await nextTick()
  await loadAiProviders()
  await initViewer()
})

onBeforeUnmount(() => {
  flushProgressSave()
  adapter?.destroy()
  adapter = null
})
</script>

<style scoped lang="scss">
.reader-layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 420px;
  gap: 16px;
}

.card {
  padding: 16px;
}

.viewer {
  min-height: calc(100vh - 180px);
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  gap: 10px;
}

.viewer-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  color: var(--qt-text-secondary);
  font-size: 13px;
}

.viewer-host {
  min-height: 420px;
  border: 1px solid var(--qt-border);
  border-radius: 8px;
  padding: 10px;
  background: color-mix(in srgb, var(--qt-card-bg) 86%, #fff 14%);
  overflow: auto;
}

.side-panel {
  display: grid;
  gap: 16px;
}

.section-title {
  font-weight: 700;
  margin-bottom: 10px;
}

.top-gap {
  margin-top: 10px;
}

.inline-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.ai-answer {
  border: 1px solid var(--qt-border);
  border-radius: 8px;
  padding: 10px;

  .label {
    font-weight: 700;
    margin-bottom: 8px;
  }

  .answer-content {
    line-height: 1.75;
  }
}

.highlight-text {
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.6;
}

.highlight-note {
  margin-top: 6px;
  color: var(--qt-text-secondary);
  font-size: 12px;
}

@media (max-width: 1200px) {
  .reader-layout {
    grid-template-columns: 1fr;
  }

  .viewer {
    min-height: 520px;
  }
}
</style>

<style lang="scss">
.reader-pdf-wrapper {
  width: 100%;
  overflow: auto;
}

.reader-pdf-page {
  position: relative;
  margin: 0 auto;
  background: #fff;
  border: 1px solid var(--qt-border);
  border-radius: 8px;
  box-shadow: 0 8px 24px rgb(15 23 42 / 8%);
  overflow: hidden;
}

.reader-pdf-canvas {
  position: absolute;
  inset: 0;
  display: block;
  z-index: 1;
}

.reader-pdf-text-layer {
  z-index: 2;
  pointer-events: auto;
  user-select: text;
}

.reader-pdf-text-layer,
.reader-pdf-text-layer * {
  user-select: text;
}

.reader-pdf-text-layer.textLayer {
  --min-font-size: 1;
  --text-scale-factor: calc(var(--total-scale-factor) * var(--min-font-size));
  --min-font-size-inv: calc(1 / var(--min-font-size));
  position: absolute;
  inset: 0;
  overflow: clip;
  line-height: 1;
  text-align: initial;
  text-size-adjust: none;
  transform-origin: 0 0;
  caret-color: CanvasText;
}

.reader-pdf-text-layer.textLayer :is(span, br) {
  position: absolute;
  color: transparent;
  white-space: pre;
  cursor: text;
  transform-origin: 0% 0%;
}

.reader-pdf-text-layer.textLayer > :not(.markedContent),
.reader-pdf-text-layer.textLayer .markedContent span:not(.markedContent) {
  z-index: 1;
  --font-height: 0;
  --scale-x: 1;
  --rotate: 0deg;
  font-size: calc(var(--text-scale-factor) * var(--font-height));
  transform: rotate(var(--rotate)) scaleX(var(--scale-x)) scale(var(--min-font-size-inv));
}

.reader-pdf-text-layer.textLayer .markedContent {
  display: contents;
}

.reader-pdf-text-layer.textLayer span[role='img'] {
  user-select: none;
  cursor: default;
}

.reader-pdf-text-layer.textLayer ::selection {
  background: color-mix(in srgb, var(--el-color-primary), transparent 70%);
}

.reader-pdf-text-layer.textLayer .reader-pdf-saved-highlight {
  box-decoration-break: clone;
  -webkit-box-decoration-break: clone;
}

.reader-pdf-text-layer.textLayer br::selection {
  background: transparent;
}

.reader-pdf-text-layer.textLayer .endOfContent {
  display: block;
  position: absolute;
  inset: 100% 0 0;
  z-index: 0;
  user-select: none;
  cursor: default;
}
</style>
