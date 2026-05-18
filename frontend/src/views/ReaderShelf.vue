<template>
  <div class="reader-shelf-page">
    <div class="page-header">
      <h1>图书阅读</h1>
      <div class="header-actions">
        <input
          ref="fileInputRef"
          type="file"
          class="hidden-input"
          accept=".epub,.pdf,application/epub+zip,application/pdf"
          @change="handleUpload"
        />
        <el-button type="primary" :loading="uploading" @click="triggerUpload">
          {{ uploading ? `上传中 ${uploadProgress}%` : '上传 EPUB/PDF' }}
        </el-button>
      </div>
    </div>

    <div class="glass-alert">
      <el-icon class="alert-icon"><InfoFilled /></el-icon>
      <span class="alert-content">支持上传 EPUB/PDF，也可以在“信息采集”页面把采集文档一键转为阅读材料。</span>
    </div>

    <div v-if="!loading && books.length === 0" class="empty-state glass-panel">
      暂无图书，请点击右上角上传。
    </div>

    <div v-else class="glass-list-view" v-loading="loading">
      <div class="list-header">
        <div class="col-title">标题</div>
        <div class="col-format">格式</div>
        <div class="col-source">来源</div>
        <div class="col-size">大小</div>
        <div class="col-time">更新时间</div>
        <div class="col-actions">操作</div>
      </div>
      
      <div class="list-body">
        <div 
          v-for="row in books" 
          :key="row.id" 
          class="list-row"
          @click="openBook(row.id)"
        >
          <div class="col-title">
            <el-icon class="book-icon"><Document /></el-icon>
            <span class="book-title">{{ row.title }}</span>
          </div>
          <div class="col-format"><span class="format-tag">{{ row.format }}</span></div>
          <div class="col-source">{{ row.sourceType }}</div>
          <div class="col-size">{{ formatSize(row.fileSize) }}</div>
          <div class="col-time">{{ formatTime(row.updatedAt) }}</div>
          <div class="col-actions" @click.stop>
            <el-button link type="primary" @click="openBook(row.id)">打开</el-button>
            <el-popconfirm title="确认删除这本书？" @confirm="removeBook(row.id)">
              <template #reference>
                <el-button link type="danger">删除</el-button>
              </template>
            </el-popconfirm>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { ElMessage } from 'element-plus'
import { Document, InfoFilled } from '@element-plus/icons-vue'
import { readerApi } from '@/api'
import type { ReaderBook } from '@/types'

const router = useRouter()
const books = ref<ReaderBook[]>([])
const loading = ref(false)
const uploading = ref(false)
const uploadProgress = ref(0)
const fileInputRef = ref<HTMLInputElement | null>(null)
const maxUploadBytes = Number(import.meta.env.VITE_READER_MAX_UPLOAD_BYTES || 100 * 1024 * 1024)

async function loadBooks() {
  loading.value = true
  try {
    books.value = await readerApi.listBooks()
  } catch (error: any) {
    const message = error?.response?.data?.message || '读取图书列表失败，请稍后重试'
    ElMessage.error(message)
  } finally {
    loading.value = false
  }
}

function triggerUpload() {
  fileInputRef.value?.click()
}

async function handleUpload(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) {
    return
  }

  const name = String(file.name || '').toLowerCase()
  if (!name.endsWith('.epub') && !name.endsWith('.pdf')) {
    ElMessage.warning('仅支持 EPUB 或 PDF 文件')
    target.value = ''
    return
  }
  if (file.size > maxUploadBytes) {
    ElMessage.warning(`文件过大，当前上限 ${(maxUploadBytes / (1024 * 1024)).toFixed(0)}MB`)
    target.value = ''
    return
  }

  uploading.value = true
  uploadProgress.value = 0
  try {
    const book = await readerApi.uploadBook(file, {
      onProgress: (percent) => {
        uploadProgress.value = percent
      }
    })
    ElMessage.success('图书上传成功')
    await loadBooks()
    openBook(book.id)
  } catch (error: any) {
    const status = Number(error?.response?.status || 0)
    const isTimeout = String(error?.code || '').toUpperCase() === 'ECONNABORTED'
    const message = status === 413
      ? `文件过大，请压缩后重试（当前上限 ${(maxUploadBytes / (1024 * 1024)).toFixed(0)}MB）`
      : isTimeout
        ? '上传超时：远程网络较慢，请换更小文件或稍后重试'
        : (error?.response?.data?.message || '图书上传失败')
    ElMessage.error(message)
  } finally {
    uploading.value = false
    uploadProgress.value = 0
    target.value = ''
  }
}

async function openBook(bookId: number) {
  const target = `/reader/${bookId}`
  try {
    await router.push(target)
  } catch {
    // Fallback for stale chunk/runtime mismatch after hot redeploy.
    window.location.assign(target)
  }
}

async function removeBook(bookId: number) {
  await readerApi.deleteBook(bookId)
  ElMessage.success('图书已删除')
  await loadBooks()
}

function formatSize(size: number) {
  const value = Number(size || 0)
  if (value < 1024) {
    return `${value} B`
  }
  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KB`
  }
  return `${(value / (1024 * 1024)).toFixed(2)} MB`
}

function formatTime(value: string) {
  return dayjs(value).format('YYYY-MM-DD HH:mm')
}

onMounted(async () => {
  await loadBooks()
})
</script>

<style scoped lang="scss">
.page-header {
  margin-bottom: 24px;
}

.glass-alert {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  background: color-mix(in srgb, #3b82f6 10%, var(--qt-card-bg) 90%);
  border: 1px solid color-mix(in srgb, #3b82f6 30%, var(--qt-border) 70%);
  border-radius: 10px;
  margin-bottom: 24px;
  backdrop-filter: blur(12px);

  .alert-icon {
    font-size: 18px;
    color: #3b82f6;
  }

  .alert-content {
    font-size: 13px;
    color: var(--qt-text);
  }
}

.empty-state {
  text-align: center;
  padding: 60px 0;
  color: var(--qt-text-muted);
}

.glass-list-view {
  background: var(--qt-surface-glass);
  backdrop-filter: blur(16px);
  border: 1px solid var(--qt-border);
  border-radius: 12px;
  overflow: hidden;

  .list-header {
    display: flex;
    align-items: center;
    padding: 14px 20px;
    border-bottom: 1px solid var(--qt-border);
    font-size: 12px;
    font-weight: 600;
    color: var(--qt-text-secondary);
    background: rgba(0, 0, 0, 0.1);
  }

  .list-row {
    display: flex;
    align-items: center;
    padding: 14px 20px;
    border-bottom: 1px solid var(--qt-border);
    font-size: 13px;
    color: var(--qt-text);
    transition: all 0.2s ease;
    cursor: pointer;

    &:last-child {
      border-bottom: none;
    }

    &:hover {
      background: color-mix(in srgb, #3b82f6 8%, transparent 92%);
      box-shadow: inset 0 0 0 1px color-mix(in srgb, #3b82f6 20%, transparent 80%);
    }
  }

  /* 栅格分列 */
  .col-title {
    flex: 1;
    min-width: 200px;
    display: flex;
    align-items: center;
    gap: 12px;
    font-weight: 500;
    
    .book-icon {
      font-size: 16px;
      color: #3b82f6;
    }

    .book-title {
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
  }

  .col-format {
    width: 100px;
    
    .format-tag {
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid var(--qt-border);
      padding: 2px 8px;
      border-radius: 6px;
      font-size: 11px;
      color: var(--qt-text-secondary);
      text-transform: uppercase;
    }
  }

  .col-source {
    width: 120px;
    color: var(--qt-text-secondary);
  }

  .col-size {
    width: 120px;
    color: var(--qt-text-secondary);
    font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  }

  .col-time {
    width: 160px;
    color: var(--qt-text-muted);
    font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  }

  .col-actions {
    width: 120px;
    text-align: right;
  }
}

.hidden-input {
  display: none;
}

@media (max-width: 960px) {
  .glass-list-view {
    .col-source, .col-size, .col-time {
      display: none;
    }
    .col-title {
      min-width: 0;
    }
  }
}
</style>
