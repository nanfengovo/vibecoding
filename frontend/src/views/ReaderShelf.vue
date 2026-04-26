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

    <el-alert
      class="reader-tips"
      type="info"
      :closable="false"
      show-icon
      title="支持上传 EPUB/PDF，也可以在“信息采集”页面把采集文档一键转为阅读材料。"
    />

    <section class="card">
      <el-table :data="books" v-loading="loading" height="520">
        <el-table-column prop="title" label="标题" min-width="220" />
        <el-table-column prop="format" label="格式" width="100" />
        <el-table-column prop="sourceType" label="来源" width="120" />
        <el-table-column label="大小" width="130">
          <template #default="{ row }">{{ formatSize(row.fileSize) }}</template>
        </el-table-column>
        <el-table-column label="更新时间" width="180">
          <template #default="{ row }">{{ formatTime(row.updatedAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="180" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openBook(row.id)">打开</el-button>
            <el-popconfirm title="确认删除这本书？" @confirm="removeBook(row.id)">
              <template #reference>
                <el-button link type="danger">删除</el-button>
              </template>
            </el-popconfirm>
          </template>
        </el-table-column>
      </el-table>
    </section>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { ElMessage } from 'element-plus'
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
.reader-tips {
  margin-bottom: 16px;
}

.card {
  padding: 16px;
}

.hidden-input {
  display: none;
}
</style>
