<template>
  <div class="reader-viewer-page">
    <div class="reader-toolbar glass-panel">
      <div class="header-left">
        <el-button text @click="goBack" class="back-btn">
          <el-icon><Back /></el-icon>
          返回书架
        </el-button>
        <h1>{{ book?.title || '阅读器' }}</h1>
        <el-tag v-if="book" size="small" effect="dark" class="format-tag">{{ book.format }}</el-tag>
      </div>
      <div class="header-actions">
        <el-select
          v-if="toc.length > 0"
          v-model="tocLocator"
          placeholder="目录跳转"
          filterable
          clearable
          class="toc-select"
          @change="goToToc"
        >
          <el-option
            v-for="item in toc"
            :key="item.href"
            :label="item.label"
            :value="item.href"
          />
        </el-select>
        <el-button @click="prevPage" circle><el-icon><ArrowLeft /></el-icon></el-button>
        <el-button @click="nextPage" circle><el-icon><ArrowRight /></el-icon></el-button>
        <el-button v-if="isMobile" type="primary" circle class="mobile-ai-btn" @click="showMobilePanel = true">
          <el-icon><ChatDotRound /></el-icon>
        </el-button>
        <el-button v-else link type="primary" @click="showNotices = true">Notices</el-button>
      </div>
    </div>

    <div class="reader-layout">
      <section class="viewer fin-terminal-card" v-loading="loading">
        <div class="viewer-meta glass-panel">
          <div class="meta-item"><span>定位</span> {{ location.locator || '未定位' }}</div>
          <div class="meta-item" v-if="location.chapterTitle"><span>章节</span> {{ location.chapterTitle }}</div>
          <div class="meta-item" v-if="location.pageNumber"><span>页码</span> {{ location.pageNumber }}</div>
          <div class="meta-item" v-if="location.percentage !== null && location.percentage !== undefined">
            <span>进度</span> {{ Number(location.percentage).toFixed(2) }}%
          </div>
        </div>
        <div class="pdf-container-glass">
          <div ref="viewerHostRef" class="viewer-host" />
        </div>
      </section>

      <div v-if="isMobile && showMobilePanel" class="mobile-panel-mask" @click="showMobilePanel = false"></div>
      <aside class="side-panel fin-terminal-card" :class="{ 'is-mobile-open': showMobilePanel }">
        <div v-if="isMobile" class="mobile-panel-header">
          <h3>AI Copilot</h3>
          <el-button text circle @click="showMobilePanel = false"><el-icon><Close /></el-icon></el-button>
        </div>
        <div class="panel-scroll-area">
          <div class="copilot-section">
            <div class="section-title">选中文本</div>
            <el-input
              v-model="selectedText"
              type="textarea"
              :rows="4"
              class="copilot-input"
              placeholder="在左侧阅读区域选中文本将自动填充"
            />
            <el-input
              v-model="highlightNote"
              class="top-gap copilot-input"
              placeholder="添加笔记 (可选)"
            />
            <div class="inline-actions top-gap">
              <el-select v-model="highlightColor" style="width: 100px" size="small">
                <el-option label="黄色" value="yellow" />
                <el-option label="绿色" value="green" />
                <el-option label="蓝色" value="blue" />
                <el-option label="粉色" value="pink" />
              </el-select>
              <el-button type="primary" size="small" :disabled="!selectedText.trim()" @click="saveHighlight" plain>
                保存划线
              </el-button>
            </div>
          </div>

          <el-divider class="glass-divider" />

          <div class="copilot-section">
            <div class="section-title">AI 对话</div>
            <el-input
              v-model="question"
              type="textarea"
              :rows="3"
              class="copilot-input"
              placeholder="围绕选中文本提问"
            />
            <div class="inline-actions top-gap action-row">
              <el-select v-model="selectedProviderId" placeholder="模型源" size="small" style="width: 110px">
                <el-option v-for="provider in aiProviders" :key="provider.id" :label="provider.name" :value="provider.id" />
              </el-select>
              <el-select v-model="selectedModel" placeholder="模型" size="small" style="flex: 1">
                <el-option v-for="model in modelOptions" :key="model" :label="model" :value="model" />
              </el-select>
            </div>
            <div class="inline-actions top-gap action-row">
              <el-button class="main-ai-btn" type="primary" :loading="askingAi" :disabled="!selectedText.trim()" @click="askAi">
                <el-icon><ChatLineRound /></el-icon> 问 AI
              </el-button>
              <el-button size="small" :loading="optimizingPrompt" :disabled="!question.trim()" @click="optimizeQuestion">
                优化提示词
              </el-button>
            </div>
            
            <div v-if="aiAnswer" class="ai-answer top-gap glass-panel">
              <div class="label">AI 回复</div>
              <div class="answer-content" v-html="aiAnswerHtml" />
            </div>

            <div class="inline-actions top-gap save-actions">
              <el-button size="small" :disabled="!selectedText.trim()" @click="saveMemory">保存到记忆</el-button>
              <el-select v-model="selectedKbId" placeholder="知识库" size="small" style="width: 120px">
                <el-option v-for="kb in knowledgeBases" :key="kb.id" :label="kb.name" :value="kb.id" />
              </el-select>
              <el-button size="small" :disabled="!selectedText.trim() || !selectedKbId" @click="importToKnowledge">
                入库
              </el-button>
            </div>
          </div>

          <el-divider class="glass-divider" />

          <div class="copilot-section">
            <div class="section-title">本书划线笔记</div>
            <div class="highlights-list" v-if="highlights.length > 0">
              <div v-for="row in highlights" :key="row.id" class="highlight-row">
                <div class="highlight-content">
                  <div class="highlight-text">{{ row.selectedText }}</div>
                  <div class="highlight-note" v-if="row.note">{{ row.note }}</div>
                </div>
                <el-button link type="danger" @click="removeHighlight(row.id)"><el-icon><Delete /></el-icon></el-button>
              </div>
            </div>
            <div v-else class="empty-highlights">暂无划线</div>
          </div>
        </div>
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
import { ArrowLeft, ArrowRight, Back, ChatDotRound, ChatLineRound, Close, Delete } from '@element-plus/icons-vue'
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

const isMobile = ref(false)
const showMobilePanel = ref(false)

let adapter: KoodoAdapter | null = null
let progressTimer: ReturnType<typeof setTimeout> | null = null

function checkViewport() {
  isMobile.value = window.innerWidth <= 960
}

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
  checkViewport()
  window.addEventListener('resize', checkViewport)
  await nextTick()
  await loadAiProviders()
  await initViewer()
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', checkViewport)
  flushProgressSave()
  adapter?.destroy()
  adapter = null
})
</script>

<style scoped lang="scss">
.reader-viewer-page {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.reader-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 24px;
  margin-bottom: 16px;
  border-radius: var(--qt-radius-lg);

  .header-left {
    display: flex;
    align-items: center;
    gap: 16px;

    h1 {
      font-size: 18px;
      font-weight: 600;
      margin: 0;
      color: var(--qt-text);
    }
    
    .back-btn {
      color: var(--qt-text-secondary);
      &:hover { color: var(--qt-text); }
    }
  }

  .header-actions {
    display: flex;
    align-items: center;
    gap: 12px;
    
    .toc-select {
      width: 220px;
    }
  }
}

.reader-layout {
  display: flex;
  flex: 1;
  gap: 16px;
  min-height: 0;
}

.viewer {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
  border: 1px solid var(--qt-border);
}

.viewer-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  padding: 10px 16px;
  margin-bottom: 12px;
  border-radius: var(--qt-radius-md);
  
  .meta-item {
    font-size: 13px;
    color: var(--qt-text);
    
    span {
      color: var(--qt-text-muted);
      margin-right: 4px;
    }
  }
}

.pdf-container-glass {
  flex: 1;
  background: rgba(0, 0, 0, 0.2);
  border-radius: var(--qt-radius-md);
  padding: 16px;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.viewer-host {
  flex: 1;
  background: #ffffff; /* 保持 PDF 区域白色 */
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
  overflow: auto;
}

/* 右侧 AI 面板 */
.side-panel {
  width: 380px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 0; /* 让滚动区域充满 */
}

.panel-scroll-area {
  flex: 1;
  overflow-y: auto;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.copilot-section {
  display: flex;
  flex-direction: column;
}

.section-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--qt-text);
  margin-bottom: 12px;
  display: flex;
  align-items: center;
  gap: 6px;
}

.copilot-input {
  :deep(.el-textarea__inner), :deep(.el-input__wrapper) {
    background: var(--qt-bg-soft);
    border-color: transparent;
    &:focus {
      background: var(--qt-surface);
      border-color: var(--qt-primary);
    }
  }
}

.top-gap {
  margin-top: 12px;
}

.inline-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.action-row {
  display: flex;
  width: 100%;
}

.main-ai-btn {
  flex: 1;
  font-weight: 600;
}

.save-actions {
  justify-content: space-between;
  border-top: 1px dashed var(--qt-border);
  padding-top: 12px;
}

.ai-answer {
  border: 1px solid var(--qt-border-glow);
  padding: 12px;
  
  .label {
    font-size: 12px;
    font-weight: 600;
    color: var(--qt-primary);
    margin-bottom: 8px;
  }
  
  .answer-content {
    font-size: 13px;
    line-height: 1.6;
    color: var(--qt-text);
  }
}

.glass-divider {
  border-color: var(--qt-border);
  margin: 8px 0;
}

.highlights-list {
  display: flex;
  flex-direction: column;
  gap: 8px;

  .highlight-row {
    display: flex;
    align-items: flex-start;
    gap: 8px;
    padding: 8px 0;
    border-bottom: 1px solid var(--qt-border);

    &:last-child {
      border-bottom: none;
    }

    .highlight-content {
      flex: 1;
      min-width: 0;
    }
  }
}

.empty-highlights {
  text-align: center;
  color: var(--qt-text-muted);
  padding: 24px 0;
  font-size: 13px;
}

.highlight-text {
  font-size: 13px;
  color: var(--qt-text);
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
  background: rgba(255, 255, 255, 0.05);
  padding: 8px;
  border-radius: 4px;
  border-left: 3px solid var(--qt-primary);
}

.highlight-note {
  margin-top: 6px;
  font-size: 12px;
  color: var(--qt-text-secondary);
  padding-left: 11px;
}

.mobile-panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  border-bottom: 1px solid var(--qt-border);
  background: var(--qt-surface);
  
  h3 {
    margin: 0;
    font-size: 16px;
    font-weight: 600;
  }
}

.mobile-panel-mask {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  z-index: 1999;
}

@media (max-width: 960px) {
  .reader-layout {
    flex-direction: column;
  }
  
  .side-panel {
    position: fixed;
    bottom: 0;
    left: 0;
    right: 0;
    width: 100%;
    height: 80vh;
    z-index: 2000;
    border-radius: 20px 20px 0 0;
    transform: translateY(100%);
    transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
    
    &.is-mobile-open {
      transform: translateY(0);
      box-shadow: 0 -10px 40px rgba(0, 0, 0, 0.3);
    }
  }

  .reader-toolbar {
    padding: 8px 12px;
    .toc-select {
      width: 140px;
    }
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
