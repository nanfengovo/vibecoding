<template>
  <div class="knowledge-page">
    <div class="page-header">
      <h1>知识库</h1>
      <el-button type="primary" @click="createKb">新建知识库</el-button>
    </div>

    <div class="knowledge-layout">
      <aside class="card kb-list">
        <div
          v-for="kb in knowledgeBases"
          :key="kb.id"
          :class="['kb-item', { active: kb.id === activeKbId }]"
          @click="selectKb(kb.id)"
        >
          <strong>{{ kb.name }}</strong>
          <span>{{ kb.description || '暂无描述' }}</span>
        </div>
      </aside>

      <main class="workspace">
        <section class="card">
          <div class="section-title">Markdown 导入</div>
          <el-form label-position="top">
            <el-form-item label="标题">
              <el-input v-model="importForm.title" />
            </el-form-item>
            <el-form-item label="Markdown">
              <el-input v-model="importForm.markdown" type="textarea" :rows="8" />
            </el-form-item>
            <el-button type="primary" :disabled="!activeKbId" @click="importMarkdown">导入并分片</el-button>
          </el-form>
        </section>

        <section class="card">
          <div class="section-title">文档</div>
          <div class="glass-list-view" style="height: 260px; display: flex; flex-direction: column">
            <div class="list-header" style="flex: 0 0 auto">
              <div class="col-title" style="flex: 1">标题</div>
              <div class="col-source hide-mobile" style="width: 130px">来源</div>
              <div class="col-actions" style="width: 80px; justify-content: flex-end; display: flex">操作</div>
            </div>
            <div class="list-body" style="flex: 1 1 auto; overflow-y: auto" v-if="documents.length > 0">
              <div v-for="row in documents" :key="row.id" class="list-row clickable" :class="{ 'active': selectedDoc?.id === row.id }" @click="selectDoc(row)">
                <div class="col-title" style="flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">{{ row.title }}</div>
                <div class="col-source hide-mobile text-muted" style="width: 130px">{{ row.sourceType }}</div>
                <div class="col-actions" style="width: 80px; justify-content: flex-end; display: flex">
                  <el-button link type="primary" size="small" @click.stop="exportDoc(row)">导出</el-button>
                </div>
              </div>
            </div>
            <div v-else class="empty-state">暂无文档</div>
          </div>
        </section>

        <section class="card">
          <div class="section-title">从采集文档入库</div>
          <div class="glass-list-view" style="height: 220px; display: flex; flex-direction: column">
            <div class="list-header" style="flex: 0 0 auto">
              <div class="col-title" style="flex: 1">标题</div>
              <div class="col-symbol hide-mobile" style="width: 110px">标的</div>
              <div class="col-actions" style="width: 80px; justify-content: flex-end; display: flex">操作</div>
            </div>
            <div class="list-body" style="flex: 1 1 auto; overflow-y: auto" v-if="crawlerDocs.length > 0">
              <div v-for="row in crawlerDocs" :key="row.id" class="list-row">
                <div class="col-title" style="flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">{{ row.title }}</div>
                <div class="col-symbol hide-mobile text-muted" style="width: 110px">{{ row.symbol }}</div>
                <div class="col-actions" style="width: 80px; justify-content: flex-end; display: flex">
                  <el-button link type="primary" size="small" :disabled="!activeKbId" @click="importCrawler(row.id)">入库</el-button>
                </div>
              </div>
            </div>
            <div v-else class="empty-state">暂无采集文档</div>
          </div>
        </section>

        <section class="card chat-card">
          <div class="section-title">知识库 AI</div>
          <div class="chat-row">
            <el-input v-model="question" placeholder="只基于当前知识库提问" @keydown.enter.prevent="ask" />
          </div>
          <div class="chat-actions top-gap">
            <el-select v-model="selectedProviderId" placeholder="模型源" style="width: 180px">
              <el-option
                v-for="provider in aiProviders"
                :key="provider.id"
                :label="provider.name"
                :value="provider.id"
              />
            </el-select>
            <el-select v-model="selectedModel" placeholder="模型" style="width: 200px">
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
            <el-button type="primary" :loading="asking" :disabled="!activeKbId" @click="ask">提问</el-button>
            <el-button :disabled="!answer.trim()" @click="saveAnswerAsMemory">保存为记忆</el-button>
          </div>
          <div v-if="answer" class="answer" v-html="answerHtml" />
          <div v-if="references.length" class="references">
            <div v-for="ref in references" :key="ref.chunkId" class="reference">
              <strong>{{ ref.title }}</strong>
              <p>{{ ref.snippet }}</p>
            </div>
          </div>
        </section>
      </main>
    </div>

    <el-dialog v-model="kbDialogVisible" title="知识库" width="520px">
      <el-form label-width="80px">
        <el-form-item label="名称">
          <el-input v-model="kbForm.name" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="kbForm.description" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="kbDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="saveKb">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import { aiApi, configApi, crawlerApi, knowledgeApi } from '@/api'
import type { AiKnowledgeReference, AiProviderConfig, CrawlerDocument, KnowledgeBase, KnowledgeDocument } from '@/types'
import { parseAiMarkdown } from '@/utils/aiMarkdown'
import { normalizeAiProviders, parseModelCandidates, resolvePreferredProviderId } from '@/lib/ai/providerModel'

const knowledgeBases = ref<KnowledgeBase[]>([])
const route = useRoute()
const documents = ref<KnowledgeDocument[]>([])
const crawlerDocs = ref<CrawlerDocument[]>([])
const activeKbId = ref<number | null>(null)
const selectedDoc = ref<KnowledgeDocument | null>(null)
const kbDialogVisible = ref(false)
const asking = ref(false)
const optimizingPrompt = ref(false)
const question = ref('')
const answer = ref('')
const references = ref<AiKnowledgeReference[]>([])
const aiProviders = ref<AiProviderConfig[]>([])
const selectedProviderId = ref('')
const selectedModel = ref('')
const kbForm = reactive({ name: '', description: '' })
const importForm = reactive({ title: '', markdown: '' })

const answerHtml = computed(() => parseAiMarkdown(answer.value || '', 'kb-answer').html)
const currentProvider = computed(() =>
  aiProviders.value.find((item) => item.id === selectedProviderId.value) || aiProviders.value[0] || null
)
const modelOptions = computed(() => {
  const list = parseModelCandidates(currentProvider.value?.model || '')
  return list.length > 0 ? list : ['gpt-5-mini']
})

watch(currentProvider, () => {
  if (!modelOptions.value.includes(selectedModel.value)) {
    selectedModel.value = modelOptions.value[0] || 'gpt-5-mini'
  }
})

async function loadKbs() {
  knowledgeBases.value = await knowledgeApi.list()
  const queryKbId = Number(route.query.kb)
  const preferredKb = Number.isFinite(queryKbId) && queryKbId > 0
    ? knowledgeBases.value.find((item) => item.id === queryKbId)
    : null
  if (preferredKb) {
    activeKbId.value = preferredKb.id
  }
  if (!activeKbId.value && knowledgeBases.value.length) {
    activeKbId.value = knowledgeBases.value[0].id
  }
  await loadDocuments()
}

async function loadDocuments() {
  if (!activeKbId.value) {
    documents.value = []
    return
  }
  documents.value = await knowledgeApi.listDocuments(activeKbId.value)
}

function selectKb(id: number) {
  activeKbId.value = id
  selectedDoc.value = null
  void loadDocuments()
}

function createKb() {
  kbForm.name = '我的知识库'
  kbForm.description = ''
  kbDialogVisible.value = true
}

async function saveKb() {
  if (!kbForm.name.trim()) {
    ElMessage.warning('请输入知识库名称')
    return
  }
  const kb = await knowledgeApi.create({ name: kbForm.name.trim(), description: kbForm.description.trim() })
  activeKbId.value = kb.id
  kbDialogVisible.value = false
  await loadKbs()
}

async function importMarkdown() {
  if (!activeKbId.value || !importForm.markdown.trim()) {
    ElMessage.warning('请选择知识库并输入 Markdown')
    return
  }
  await knowledgeApi.importMarkdown(activeKbId.value, {
    title: importForm.title,
    markdown: importForm.markdown
  })
  importForm.title = ''
  importForm.markdown = ''
  ElMessage.success('已导入知识库')
  await loadDocuments()
}

async function importCrawler(id: number) {
  if (!activeKbId.value) {
    return
  }
  await knowledgeApi.importCrawlerDocument(activeKbId.value, id)
  ElMessage.success('采集文档已入库')
  await loadDocuments()
}

function selectDoc(row: KnowledgeDocument) {
  selectedDoc.value = row
  importForm.title = row.title
  importForm.markdown = row.markdown
}

async function exportDoc(row: KnowledgeDocument) {
  if (!activeKbId.value) {
    return
  }
  const blob = await knowledgeApi.exportDocument(activeKbId.value, row.id)
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `${row.title || 'document'}.md`
  anchor.click()
  URL.revokeObjectURL(url)
}

async function ask() {
  if (!activeKbId.value || !question.value.trim()) {
    ElMessage.warning('请输入问题')
    return
  }
  asking.value = true
  try {
    const result = await knowledgeApi.chat(activeKbId.value, {
      question: question.value.trim(),
      providerId: selectedProviderId.value || undefined,
      model: selectedModel.value || undefined
    })
    answer.value = result.content
    references.value = result.references || []
    if (result.model) {
      selectedModel.value = result.model
    }
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || '知识库问答失败')
  } finally {
    asking.value = false
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
    const contextText = selectedDoc.value?.markdown
      ? selectedDoc.value.markdown.slice(0, 2400)
      : undefined
    const result = await aiApi.optimizePrompt({
      question: ask,
      providerId: selectedProviderId.value || undefined,
      model: selectedModel.value || undefined,
      scene: 'knowledge',
      contextText,
      knowledgeBaseId: activeKbId.value || undefined
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

async function saveAnswerAsMemory() {
  if (!answer.value.trim()) {
    ElMessage.warning('暂无可保存的 AI 回答')
    return
  }

  try {
    await aiApi.createMemory({
      type: 'knowledge_answer',
      title: `知识库问答：${question.value.trim().slice(0, 32) || '未命名问题'}`,
      content: answer.value.trim(),
      tags: 'knowledge,ai_answer',
      priority: 2,
      sourceType: 'knowledge_ai',
      sourceUrl: activeKbId.value ? `knowledge://base/${activeKbId.value}` : '',
      sourceRef: activeKbId.value
        ? `knowledge:${activeKbId.value}:q:${question.value.trim().slice(0, 80)}`
        : `knowledge:q:${question.value.trim().slice(0, 80)}`,
      providerId: selectedProviderId.value || undefined,
      model: selectedModel.value || undefined,
      knowledgeBaseId: activeKbId.value || undefined
    })
    ElMessage.success('已保存到 AI 记忆')
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || '保存记忆失败')
  }
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

onMounted(async () => {
  await loadAiProviders()
  await loadKbs()
  crawlerDocs.value = await crawlerApi.listDocuments()
})
</script>

<style scoped lang="scss">
.knowledge-layout {
  display: grid;
  grid-template-columns: 260px minmax(0, 1fr);
  gap: 16px;
}

.kb-list {
  padding: 10px;
}

.kb-item {
  display: grid;
  gap: 4px;
  padding: 10px;
  border-radius: 8px;
  cursor: pointer;

  span {
    color: var(--qt-text-muted);
    font-size: 12px;
  }

  &.active {
    background: color-mix(in srgb, var(--qt-card-bg) 84%, #3b82f6 16%);
    outline: 1px solid #3b82f6;
  }
}

.workspace {
  display: grid;
  gap: 16px;
}

.card {
  padding: 16px;
}

.section-title {
  font-weight: 700;
  margin-bottom: 12px;
}

.chat-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 10px;
}

.chat-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.top-gap {
  margin-top: 10px;
}

.answer {
  margin-top: 14px;
  line-height: 1.75;
}

.references {
  display: grid;
  gap: 8px;
  margin-top: 12px;
}

.reference {
  border: 1px solid var(--qt-border);
  border-radius: 8px;
  padding: 10px;

  p {
    margin: 6px 0 0;
    color: var(--qt-text-secondary);
  }
}

/* Glass List View */
.glass-list-view {
  background: transparent;
  border: 1px solid var(--qt-border);
  border-radius: 8px;
  overflow: hidden;

  .list-header {
    display: flex;
    align-items: center;
    padding: 12px 16px;
    border-bottom: 1px solid var(--qt-border);
    font-size: 13px;
    font-weight: 600;
    color: var(--qt-text-secondary);
    background: rgba(0, 0, 0, 0.15);
  }

  .list-row {
    display: flex;
    align-items: center;
    padding: 14px 16px;
    border-bottom: 1px solid var(--qt-border);
    font-size: 14px;
    color: var(--qt-text);
    transition: all 0.2s ease;

    &:last-child {
      border-bottom: none;
    }

    &:hover {
      background: color-mix(in srgb, #3b82f6 10%, transparent 90%);
    }

    &.clickable {
      cursor: pointer;
    }

    &.active {
      background: rgba(59, 130, 246, 0.2);
    }
  }

  .text-muted { color: var(--qt-text-muted); }
  .number-font { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }

  .empty-state {
    padding: 40px;
    text-align: center;
    color: var(--qt-text-muted);
    flex: 1;
    display: flex;
    align-items: center;
    justify-content: center;
  }
}

@media (max-width: 960px) {
  .knowledge-layout {
    grid-template-columns: 1fr;
  }

  .chat-row {
    grid-template-columns: 1fr;
  }

  .glass-list-view {
    .hide-mobile {
      display: none !important;
    }
  }
}
</style>
