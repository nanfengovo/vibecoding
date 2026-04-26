<template>
  <div class="memory-page">
    <div class="page-header">
      <h1>记忆中心</h1>
      <el-button @click="loadMemories">刷新</el-button>
    </div>

    <section class="card filter-card">
      <div class="filter-row">
        <el-input v-model="filters.query" placeholder="搜索标题/内容/标签" clearable style="width: 260px" />
        <el-select v-model="filters.type" placeholder="记忆类型" clearable style="width: 160px">
          <el-option label="阅读摘录" value="reader_highlight" />
          <el-option label="知识库问答" value="knowledge_answer" />
          <el-option label="个股分析" value="stock_analysis" />
          <el-option label="AI Chat" value="ai_chat" />
          <el-option label="自动偏好" value="preference" />
        </el-select>
        <el-select v-model="filters.sourceType" placeholder="来源类型" clearable style="width: 170px">
          <el-option label="reader_highlight" value="reader_highlight" />
          <el-option label="knowledge_ai" value="knowledge_ai" />
          <el-option label="knowledge_document" value="knowledge_document" />
          <el-option label="stock_analysis" value="stock_analysis" />
          <el-option label="ai_chat" value="ai_chat" />
        </el-select>
        <el-select v-model="filters.knowledgeBaseId" placeholder="知识库" clearable style="width: 200px">
          <el-option
            v-for="kb in knowledgeBases"
            :key="kb.id"
            :label="kb.name"
            :value="kb.id"
          />
        </el-select>
        <el-button type="primary" @click="applyFilters">查询</el-button>
        <el-button @click="resetFilters">重置</el-button>
      </div>
    </section>

    <section class="card table-card" v-loading="loading">
      <el-table :data="rows" height="560">
        <el-table-column label="标题" min-width="220">
          <template #default="{ row }">
            <div class="title-cell">{{ row.title || '未命名记忆' }}</div>
            <div class="sub-meta">
              类型：{{ row.type || '-' }} · 来源：{{ row.sourceType || '-' }}
            </div>
          </template>
        </el-table-column>
        <el-table-column label="内容" min-width="320">
          <template #default="{ row }">
            <div class="content-cell">{{ row.content }}</div>
          </template>
        </el-table-column>
        <el-table-column label="关联" width="180">
          <template #default="{ row }">
            <div class="assoc-cell">
              <div>KB：{{ resolveKnowledgeBaseName(row.knowledgeBaseId) }}</div>
              <div>Doc：{{ row.knowledgeDocumentId || '-' }}</div>
              <div>状态：{{ row.syncStatus || '-' }}</div>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="来源" width="180">
          <template #default="{ row }">
            <div class="source-cell">
              <div class="source-type">{{ row.sourceType || '-' }}</div>
              <div class="source-actions">
                <el-button
                  v-if="isLinkableSource(row.sourceUrl)"
                  link
                  type="primary"
                  @click="openSource(row.sourceUrl)"
                >
                  查看来源
                </el-button>
                <el-button
                  v-if="row.knowledgeBaseId"
                  link
                  type="primary"
                  @click="goKnowledge(row.knowledgeBaseId)"
                >
                  查看知识库
                </el-button>
              </div>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="标签" width="160">
          <template #default="{ row }">
            <span class="tags-text">{{ row.tags || '-' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="更新时间" width="170">
          <template #default="{ row }">
            {{ formatTime(row.updatedAt) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="primary" @click="syncRow(row.id)">同步</el-button>
            <el-button link type="danger" @click="archiveRow(row.id)">归档</el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="pager-row">
        <el-pagination
          v-model:current-page="pagination.page"
          v-model:page-size="pagination.pageSize"
          :total="pagination.total"
          :page-sizes="[10, 20, 50, 100]"
          layout="total, sizes, prev, pager, next"
          @current-change="loadMemories"
          @size-change="loadMemories"
        />
      </div>
    </section>

    <el-dialog v-model="editVisible" title="编辑记忆" width="680px">
      <el-form label-width="96px">
        <el-form-item label="标题">
          <el-input v-model="editForm.title" />
        </el-form-item>
        <el-form-item label="内容">
          <el-input v-model="editForm.content" type="textarea" :rows="6" />
        </el-form-item>
        <el-form-item label="标签">
          <el-input v-model="editForm.tags" placeholder="逗号分隔" />
        </el-form-item>
        <el-form-item label="优先级">
          <el-input-number v-model="editForm.priority" :min="1" :max="10" />
        </el-form-item>
        <el-form-item label="知识库">
          <el-select v-model="editForm.knowledgeBaseId" placeholder="可选" clearable style="width: 220px">
            <el-option
              v-for="kb in knowledgeBases"
              :key="kb.id"
              :label="kb.name"
              :value="kb.id"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="saveEdit">保存并同步</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import dayjs from 'dayjs'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { aiApi, knowledgeApi } from '@/api'
import type { AiMemoryRecord, KnowledgeBase } from '@/types'

const router = useRouter()
const loading = ref(false)
const saving = ref(false)
const rows = ref<AiMemoryRecord[]>([])
const knowledgeBases = ref<KnowledgeBase[]>([])

const filters = reactive({
  query: '',
  type: '',
  sourceType: '',
  knowledgeBaseId: null as number | null
})

const pagination = reactive({
  page: 1,
  pageSize: 20,
  total: 0
})

const editVisible = ref(false)
const editForm = reactive({
  id: 0,
  title: '',
  content: '',
  tags: '',
  priority: 1,
  knowledgeBaseId: null as number | null
})

function formatTime(value: string) {
  return dayjs(value).format('YYYY-MM-DD HH:mm:ss')
}

function resolveKnowledgeBaseName(id?: number | null) {
  if (!id) {
    return '-'
  }
  const kb = knowledgeBases.value.find((item) => item.id === id)
  return kb?.name || `#${id}`
}

function isLinkableSource(sourceUrl?: string) {
  const value = String(sourceUrl || '').trim().toLowerCase()
  return value.startsWith('http://') || value.startsWith('https://')
}

function openSource(sourceUrl?: string) {
  const value = String(sourceUrl || '').trim()
  if (!value) {
    ElMessage.warning('该记忆没有来源链接')
    return
  }
  window.open(value, '_blank', 'noopener')
}

function goKnowledge(knowledgeBaseId?: number | null) {
  if (!knowledgeBaseId) {
    return
  }
  router.push({ path: '/knowledge', query: { kb: String(knowledgeBaseId) } })
}

async function loadKnowledgeBases() {
  try {
    knowledgeBases.value = await knowledgeApi.list()
  } catch {
    knowledgeBases.value = []
  }
}

async function loadMemories() {
  loading.value = true
  try {
    const result = await aiApi.listMemories({
      query: filters.query.trim() || undefined,
      type: filters.type || undefined,
      sourceType: filters.sourceType || undefined,
      knowledgeBaseId: filters.knowledgeBaseId || undefined,
      page: pagination.page,
      pageSize: pagination.pageSize
    })
    rows.value = result.items || []
    pagination.total = Number(result.total || 0)
    pagination.page = Number(result.page || pagination.page)
    pagination.pageSize = Number(result.pageSize || pagination.pageSize)
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || '记忆加载失败')
  } finally {
    loading.value = false
  }
}

function applyFilters() {
  pagination.page = 1
  void loadMemories()
}

function resetFilters() {
  filters.query = ''
  filters.type = ''
  filters.sourceType = ''
  filters.knowledgeBaseId = null
  pagination.page = 1
  void loadMemories()
}

function openEdit(row: AiMemoryRecord) {
  editForm.id = row.id
  editForm.title = row.title || ''
  editForm.content = row.content || ''
  editForm.tags = row.tags || ''
  editForm.priority = Number(row.priority || 1)
  editForm.knowledgeBaseId = row.knowledgeBaseId ?? null
  editVisible.value = true
}

async function saveEdit() {
  if (!editForm.id || !editForm.content.trim()) {
    ElMessage.warning('内容不能为空')
    return
  }

  saving.value = true
  try {
    await aiApi.updateMemory(editForm.id, {
      title: editForm.title.trim(),
      content: editForm.content.trim(),
      tags: editForm.tags.trim(),
      priority: editForm.priority,
      knowledgeBaseId: editForm.knowledgeBaseId || undefined
    })
    await aiApi.syncMemory(editForm.id)
    ElMessage.success('记忆已更新并同步')
    editVisible.value = false
    await loadMemories()
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || '保存失败')
  } finally {
    saving.value = false
  }
}

async function syncRow(id: number) {
  try {
    await aiApi.syncMemory(id)
    ElMessage.success('同步成功')
    await loadMemories()
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || '同步失败')
  }
}

async function archiveRow(id: number) {
  try {
    await aiApi.deleteMemory(id)
    ElMessage.success('已归档')
    await loadMemories()
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || '归档失败')
  }
}

onMounted(async () => {
  await loadKnowledgeBases()
  await loadMemories()
})
</script>

<style scoped lang="scss">
.filter-card {
  margin-bottom: 12px;
}

.filter-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.table-card {
  padding-bottom: 10px;
}

.title-cell {
  font-weight: 600;
}

.sub-meta {
  margin-top: 4px;
  color: var(--qt-text-secondary);
  font-size: 12px;
}

.content-cell {
  white-space: pre-wrap;
  line-height: 1.6;
  max-height: 120px;
  overflow: auto;
}

.assoc-cell {
  font-size: 12px;
  line-height: 1.5;
}

.source-cell {
  font-size: 12px;
  line-height: 1.4;
}

.source-type {
  color: var(--qt-text-secondary);
}

.source-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.tags-text {
  color: var(--qt-text-secondary);
}

.pager-row {
  margin-top: 12px;
  display: flex;
  justify-content: flex-end;
}

@media (max-width: 1200px) {
  .filter-row {
    align-items: stretch;
  }
}
</style>
