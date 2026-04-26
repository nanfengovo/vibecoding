<template>
  <div class="crawler-page">
    <div class="page-header">
      <h1>信息采集</h1>
      <el-button type="primary" @click="openCreate">新增来源</el-button>
    </div>

    <el-alert
      class="crawler-guide"
      type="info"
      :closable="false"
      show-icon
      title="来源是可重复抓取的信息入口；频率决定定时采集间隔；系统按内容哈希去重。请尊重 robots、版权和站点反爬规则，失败时先看任务状态、HTTP 状态和 URL 是否可访问。"
    />

    <div class="content-grid">
      <section class="card">
        <el-table :data="sources" v-loading="loadingSources" height="420">
          <el-table-column prop="name" label="名称" min-width="150" />
          <el-table-column prop="type" label="类型" width="140" />
          <el-table-column prop="symbol" label="标的" width="120" />
          <el-table-column label="频率" width="110">
            <template #default="{ row }">{{ row.crawlIntervalMinutes }} 分钟</template>
          </el-table-column>
          <el-table-column label="状态" width="90">
            <template #default="{ row }">
              <el-tag :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '启用' : '停用' }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="230" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" @click="run(row)">抓取</el-button>
              <el-button link @click="edit(row)">编辑</el-button>
              <el-button link @click="loadDocuments(row.id)">文档</el-button>
              <el-popconfirm title="删除这个来源？" @confirm="remove(row.id)">
                <template #reference>
                  <el-button link type="danger">删除</el-button>
                </template>
              </el-popconfirm>
            </template>
          </el-table-column>
        </el-table>
      </section>

      <section class="card">
        <div class="section-title">采集文档</div>
        <el-table :data="documents" v-loading="loadingDocs" height="420" @row-click="selectDoc">
          <el-table-column prop="title" label="标题" min-width="200" />
          <el-table-column prop="symbol" label="标的" width="110" />
          <el-table-column prop="createdAt" label="入库时间" width="170">
            <template #default="{ row }">{{ formatTime(row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="140" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" @click.stop="importAsReader(row.id)">转为阅读材料</el-button>
            </template>
          </el-table-column>
        </el-table>
      </section>
    </div>

    <section class="card preview" v-if="selectedDoc">
      <div class="section-title">{{ selectedDoc.title }}</div>
      <pre>{{ selectedDoc.markdown }}</pre>
    </section>

    <el-dialog v-model="dialogVisible" :title="form.id ? '编辑来源' : '新增来源'" width="620px">
      <el-form label-width="96px">
        <el-form-item label="名称">
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="类型">
          <el-select v-model="form.type">
            <el-option label="长桥新闻" value="longbridge_news" />
            <el-option label="长桥股票 Markdown" value="longbridge_quote" />
            <el-option label="RSS" value="rss" />
            <el-option label="Markdown URL" value="markdown" />
            <el-option label="网页" value="web" />
          </el-select>
        </el-form-item>
        <el-form-item label="URL/RSS">
          <el-input v-model="form.url" placeholder="长桥新闻可留空；外部来源填写 URL" />
        </el-form-item>
        <el-form-item label="目标标的">
          <el-input v-model="form.symbol" placeholder="如 AAPL.US / 00700.HK / 600519.SH" />
        </el-form-item>
        <el-form-item label="标签">
          <el-input v-model="form.tags" />
        </el-form-item>
        <el-form-item label="抓取频率">
          <el-input-number v-model="form.crawlIntervalMinutes" :min="15" :max="10080" />
        </el-form-item>
        <el-form-item label="最大页数">
          <el-input-number v-model="form.maxPages" :min="1" :max="30" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.isEnabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { ElMessage } from 'element-plus'
import { crawlerApi, readerApi } from '@/api'
import type { CrawlerDocument, CrawlerSource } from '@/types'

const router = useRouter()
const sources = ref<CrawlerSource[]>([])
const documents = ref<CrawlerDocument[]>([])
const selectedDoc = ref<CrawlerDocument | null>(null)
const loadingSources = ref(false)
const loadingDocs = ref(false)
const dialogVisible = ref(false)
const form = reactive<Partial<CrawlerSource>>({})

function resetForm(seed?: Partial<CrawlerSource>) {
  Object.assign(form, {
    id: seed?.id,
    name: seed?.name || '长桥资讯',
    type: seed?.type || 'longbridge_news',
    url: seed?.url || '',
    symbol: seed?.symbol || '',
    tags: seed?.tags || '',
    isEnabled: seed?.isEnabled ?? true,
    crawlIntervalMinutes: seed?.crawlIntervalMinutes || 360,
    maxPages: seed?.maxPages || 5
  })
}

async function loadSources() {
  loadingSources.value = true
  try {
    sources.value = await crawlerApi.listSources()
  } finally {
    loadingSources.value = false
  }
}

async function loadDocuments(sourceId?: number) {
  loadingDocs.value = true
  try {
    documents.value = await crawlerApi.listDocuments({ sourceId })
    selectedDoc.value = documents.value[0] || null
  } finally {
    loadingDocs.value = false
  }
}

function openCreate() {
  resetForm()
  dialogVisible.value = true
}

function edit(row: CrawlerSource) {
  resetForm(row)
  dialogVisible.value = true
}

async function save() {
  if (!String(form.name || '').trim()) {
    ElMessage.warning('请输入来源名称')
    return
  }

  if (form.id) {
    await crawlerApi.updateSource(Number(form.id), form)
  } else {
    await crawlerApi.createSource(form)
  }
  dialogVisible.value = false
  ElMessage.success('来源已保存')
  await loadSources()
}

async function run(row: CrawlerSource) {
  const job = await crawlerApi.runSource(row.id)
  ElMessage[job.status === 'failed' ? 'error' : 'success'](
    job.status === 'failed' ? job.errorMessage || '抓取失败' : `已保存 ${job.documentsSaved} 篇文档`
  )
  await loadSources()
  await loadDocuments(row.id)
}

async function remove(id: number) {
  await crawlerApi.deleteSource(id)
  ElMessage.success('来源已删除')
  await loadSources()
}

async function importAsReader(crawlerDocumentId: number) {
  const book = await readerApi.importCrawlerDocument(crawlerDocumentId)
  ElMessage.success('已转为阅读材料')
  router.push(`/reader/${book.id}`)
}

function selectDoc(row: CrawlerDocument) {
  selectedDoc.value = row
}

function formatTime(value: string) {
  return dayjs(value).format('YYYY-MM-DD HH:mm')
}

onMounted(async () => {
  await loadSources()
  await loadDocuments()
})
</script>

<style scoped lang="scss">
.crawler-guide {
  margin-bottom: 16px;
}

.content-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 16px;
}

.card {
  padding: 16px;
}

.section-title {
  font-weight: 700;
  margin-bottom: 12px;
}

.preview {
  margin-top: 16px;

  pre {
    white-space: pre-wrap;
    overflow-wrap: anywhere;
    max-height: 360px;
    overflow: auto;
    margin: 0;
    color: var(--qt-text-primary);
  }
}

@media (max-width: 960px) {
  .content-grid {
    grid-template-columns: 1fr;
  }
}
</style>
