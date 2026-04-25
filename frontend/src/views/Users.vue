<template>
  <div class="users-page">
    <div class="page-header">
      <h1>用户管理</h1>
      <el-button type="primary" @click="dialogVisible = true">创建用户</el-button>
    </div>

    <section class="card">
      <el-table :data="users" v-loading="loading">
        <el-table-column prop="username" label="用户名" />
        <el-table-column prop="displayName" label="显示名" />
        <el-table-column prop="role" label="角色" width="120">
          <template #default="{ row }">
            <el-tag :type="row.role === 'admin' ? 'danger' : 'info'">{{ row.role }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="createdAt" label="创建时间" />
        <el-table-column prop="lastLoginAt" label="最后登录" />
      </el-table>
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

onMounted(loadUsers)
</script>

<style scoped lang="scss">
.card {
  padding: 16px;
}
</style>
