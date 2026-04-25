<template>
  <div class="login-page">
    <section class="login-panel">
      <div class="brand">
        <div class="mark">
          <el-icon><TrendCharts /></el-icon>
        </div>
        <div>
          <h1>QuantTrading</h1>
          <p>登录后继续使用你的策略、AI 记忆和知识库。</p>
        </div>
      </div>

      <el-form class="login-form" label-position="top" @submit.prevent="submit">
        <el-form-item label="用户名">
          <el-input v-model="username" autocomplete="username" size="large" />
        </el-form-item>
        <el-form-item label="密码">
          <el-input
            v-model="password"
            type="password"
            autocomplete="current-password"
            size="large"
            show-password
            @keydown.enter.prevent="submit"
          />
        </el-form-item>
        <el-button type="primary" size="large" :loading="auth.loading" @click="submit">
          登录
        </el-button>
      </el-form>
    </section>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { TrendCharts } from '@element-plus/icons-vue'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const username = ref('')
const password = ref('')

async function submit() {
  if (!username.value.trim() || !password.value) {
    ElMessage.warning('请输入用户名和密码')
    return
  }

  try {
    await auth.login(username.value.trim(), password.value)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/dashboard'
    await router.replace(redirect)
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || '登录失败')
  }
}
</script>

<style scoped lang="scss">
.login-page {
  min-height: 100vh;
  display: grid;
  place-items: center;
  padding: 24px;
  background:
    linear-gradient(135deg, rgba(15, 23, 42, 0.88), rgba(30, 41, 59, 0.72)),
    url('/placeholder.jpg') center / cover;
}

.login-panel {
  width: min(420px, 100%);
  border-radius: 12px;
  padding: 28px;
  background: rgba(255, 255, 255, 0.94);
  box-shadow: 0 20px 60px rgba(15, 23, 42, 0.28);
}

.brand {
  display: flex;
  gap: 14px;
  align-items: center;
  margin-bottom: 24px;

  .mark {
    width: 48px;
    height: 48px;
    border-radius: 10px;
    display: grid;
    place-items: center;
    color: #fff;
    background: #2563eb;
    font-size: 26px;
  }

  h1 {
    margin: 0 0 4px;
    font-size: 26px;
    color: #111827;
  }

  p {
    margin: 0;
    color: #64748b;
    line-height: 1.5;
  }
}

.login-form {
  :deep(.el-button) {
    width: 100%;
  }
}
</style>
