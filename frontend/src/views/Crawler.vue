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
        <div class="glass-list-view" v-loading="loadingSources" style="height: 420px; display: flex; flex-direction: column">
          <div class="list-header" style="flex: 0 0 auto">
            <div class="col-name" style="flex: 1.5">名称</div>
            <div class="col-type hide-mobile" style="width: 140px">类型</div>
            <div class="col-symbol hide-mobile" style="width: 100px">标的</div>
            <div class="col-freq hide-mobile" style="width: 80px">频率</div>
            <div class="col-status" style="width: 60px">状态</div>
            <div class="col-actions" style="width: 160px; justify-content: flex-end; display: flex">操作</div>
          </div>
          <div class="list-body" style="flex: 1 1 auto; overflow-y: auto" v-if="sources.length > 0">
            <div v-for="row in sources" :key="row.id" class="list-row">
              <div class="col-name" style="flex: 1.5; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">
                {{ row.name }}
              </div>
              <div class="col-type hide-mobile text-muted" style="width: 140px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">{{ row.type }}</div>
              <div class="col-symbol hide-mobile" style="width: 100px">{{ row.symbol }}</div>
              <div class="col-freq hide-mobile text-muted number-font" style="width: 80px">{{ row.crawlIntervalMinutes }}m</div>
              <div class="col-status" style="width: 60px">
                <el-tag :type="row.isEnabled ? 'success' : 'info'" size="small" class="glass-tag">{{ row.isEnabled ? '启用' : '停用' }}</el-tag>
              </div>
              <div class="col-actions" style="width: 160px; justify-content: flex-end; display: flex; gap: 4px">
                <el-button link type="primary" size="small" @click="run(row)">抓取</el-button>
                <el-button link size="small" @click="edit(row)">编辑</el-button>
                <el-button link size="small" @click="loadDocuments(row.id)">文档</el-button>
                <el-popconfirm title="删除这个来源？" @confirm="remove(row.id)">
                  <template #reference>
                    <el-button link type="danger" size="small">删除</el-button>
                  </template>
                </el-popconfirm>
              </div>
            </div>
          </div>
          <div v-else class="empty-state">暂无来源数据</div>
        </div>
      </section>

      <section class="card">
        <div class="section-title">采集文档</div>
        <div class="glass-list-view" v-loading="loadingDocs" style="height: 388px; display: flex; flex-direction: column">
          <div class="list-header" style="flex: 0 0 auto">
            <div class="col-title" style="flex: 1">标题</div>
            <div class="col-symbol hide-mobile" style="width: 80px">标的</div>
            <div class="col-time" style="width: 140px">入库时间</div>
            <div class="col-actions" style="width: 80px; justify-content: flex-end; display: flex">操作</div>
          </div>
          <div class="list-body" style="flex: 1 1 auto; overflow-y: auto" v-if="documents.length > 0">
            <div v-for="row in documents" :key="row.id" class="list-row clickable" :class="{ 'active': selectedDoc?.id === row.id }" @click="selectDoc(row)">
              <div class="col-title" style="flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">{{ row.title }}</div>
              <div class="col-symbol hide-mobile" style="width: 80px">{{ row.symbol }}</div>
              <div class="col-time number-font text-muted" style="width: 140px">{{ formatTime(row.createdAt) }}</div>
              <div class="col-actions" style="width: 80px; justify-content: flex-end; display: flex">
                <el-button link type="primary" size="small" @click.stop="importAsReader(row.id)">转阅读</el-button>
              </div>
            </div>
          </div>
          <div v-else class="empty-state">暂无文档</div>
        </div>
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

  .glass-tag {
    background: rgba(255, 255, 255, 0.05);
    border: 1px solid var(--qt-border);
  }

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
  .content-grid {
    grid-template-columns: 1fr;
  }
  
  .glass-list-view {
    .hide-mobile {
      display: none !important;
    }
  }
}
</style>
