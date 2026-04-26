<template>
  <div class="users-page">
    <div class="page-header">
      <h1>用户管理</h1>
      <el-button type="primary" @click="dialogVisible = true">创建用户</el-button>
    </div>

    <section class="card">
      <div class="glass-list-view" v-loading="loading">
        <div class="list-header">
          <div class="col-username" style="flex: 1">用户名</div>
          <div class="col-display" style="flex: 1">显示名</div>
          <div class="col-role" style="width: 120px">角色</div>
          <div class="col-created hide-mobile" style="flex: 1">创建时间</div>
          <div class="col-login hide-mobile" style="flex: 1">最后登录</div>
        </div>
        <div class="list-body" v-if="users.length > 0">
          <div v-for="row in users" :key="row.username" class="list-row">
            <div class="col-username" style="flex: 1; font-weight: 500">{{ row.username }}</div>
            <div class="col-display text-muted" style="flex: 1">{{ row.displayName || '-' }}</div>
            <div class="col-role" style="width: 120px">
              <el-tag :type="row.role === 'admin' ? 'danger' : 'info'" class="glass-tag" size="small">{{ row.role }}</el-tag>
            </div>
            <div class="col-created hide-mobile number-font text-muted" style="flex: 1">{{ formatTime(row.createdAt) }}</div>
            <div class="col-login hide-mobile number-font text-muted" style="flex: 1">{{ formatTime(row.lastLoginAt) }}</div>
          </div>
        </div>
        <div v-else class="empty-state">暂无用户数据</div>
      </div>
    </section>

    <el-dialog v-model="dialogVisible" title="创建用户" width="520px">
      <el-form label-width="82px">
        <el-form-item label="用户名">
          <el-input v-model="form.username" />
        </el-form-item>
        <el-form-item label="显示名">
          <el-input v-model="form.displayName" />
        </el-form-item>
        <el-form-item label="密码">
          <el-input v-model="form.password" type="password" show-password />
        </el-form-item>
        <el-form-item label="角色">
          <el-select v-model="form.role">
            <el-option label="普通用户" value="user" />
            <el-option label="管理员" value="admin" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="create">创建</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { authApi } from '@/api'
import type { AuthUser } from '@/types'

const users = ref<AuthUser[]>([])
const loading = ref(false)
const dialogVisible = ref(false)
const form = reactive({
  username: '',
  displayName: '',
  password: '',
  role: 'user'
})

async function loadUsers() {
  loading.value = true
  try {
    users.value = await authApi.listUsers()
  } finally {
    loading.value = false
  }
}

async function create() {
  if (!form.username.trim() || !form.password) {
    ElMessage.warning('请输入用户名和密码')
    return
  }
  await authApi.createUser({
    username: form.username.trim(),
    displayName: form.displayName.trim(),
    password: form.password,
    role: form.role
  })
  Object.assign(form, { username: '', displayName: '', password: '', role: 'user' })
  dialogVisible.value = false
  ElMessage.success('用户已创建')
  await loadUsers()
}

function formatTime(val?: string) {
  if (!val) return '-'
  return dayjs(val).format('YYYY-MM-DD HH:mm:ss')
}

onMounted(loadUsers)
</script>

<style scoped lang="scss">
.card {
  padding: 16px;
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
  }
}

@media (max-width: 960px) {
  .glass-list-view {
    .hide-mobile {
      display: none !important;
    }
  }
}
</style>
