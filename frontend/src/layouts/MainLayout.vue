<template>
  <el-container
    class="main-layout"
    :class="{ 'is-mobile': isMobile, 'mobile-sidebar-open': isMobile && !sidebarCollapsed }"
  >
    <!-- 侧边栏 -->
    <el-aside :width="asideWidth" :class="['sidebar', { open: !sidebarCollapsed }]">
      <div class="logo">
        <el-icon :size="28"><TrendCharts /></el-icon>
        <span v-show="!sidebarCollapsed" class="logo-text">QuantTrading</span>
      </div>
      
      <el-menu
        :default-active="currentRoute"
        :collapse="sidebarCollapsed"
        router
        class="sidebar-menu"
        @select="handleMenuSelect"
      >
        <template v-for="route in menuRoutes" :key="route.fullPath">
          <el-menu-item :index="route.fullPath">
            <el-icon><component :is="route.meta?.icon" /></el-icon>
            <template #title>{{ route.meta?.title }}</template>
          </el-menu-item>
        </template>
      </el-menu>

      <div class="sidebar-footer">
        <el-button 
          :icon="sidebarCollapsed ? 'Expand' : 'Fold'" 
          text 
          @click="toggleSidebar"
        />
      </div>
    </el-aside>
    <div v-if="isMobile && !sidebarCollapsed" class="sidebar-mask" @click="toggleSidebar" />

    <!-- 主内容区 -->
    <el-container>
      <!-- 顶部导航 -->
      <el-header class="header">
        <div class="header-left">
          <el-button
            v-if="isMobile"
            text
            circle
            :icon="Menu"
            class="mobile-menu-btn"
            @click="toggleSidebar"
          />
          <el-breadcrumb separator="/">
            <el-breadcrumb-item :to="{ path: '/' }">首页</el-breadcrumb-item>
            <el-breadcrumb-item>{{ currentTitle }}</el-breadcrumb-item>
          </el-breadcrumb>
        </div>
        
        <div class="header-right">
          <!-- 搜索股票 -->
          <el-autocomplete
            v-model="searchQuery"
            :fetch-suggestions="searchStock"
            placeholder="搜索股票..."
            :prefix-icon="Search"
            class="stock-search"
            @select="handleSelectStock"
          >
            <template #default="{ item }">
              <div class="search-item">
                <span class="symbol">{{ item.symbol }}</span>
                <span class="name">{{ item.name }}</span>
              </div>
            </template>
          </el-autocomplete>

          <el-tooltip
            :content="appStore.theme === 'dark' ? '切换浅色' : '切换深色'"
            placement="bottom"
            :disabled="isMobile"
          >
            <el-button
              circle
              class="theme-toggle"
              :icon="appStore.theme === 'dark' ? Sunny : Moon"
              @click="appStore.toggleTheme"
            />
          </el-tooltip>

          <!-- 通知 -->
          <el-popover placement="bottom-end" :width="isMobile ? 280 : 320" trigger="click">
            <template #reference>
              <el-badge :value="unreadCount" :hidden="unreadCount === 0" class="notification-badge">
                <el-button :icon="Bell" circle />
              </el-badge>
            </template>
            <div class="notification-panel">
              <div class="notification-header">
                <span>通知</span>
                <el-button link type="primary" size="small" @click="clearNotifications">
                  清空
                </el-button>
              </div>
              <el-scrollbar max-height="300px">
                <div v-if="notifications.length === 0" class="notification-empty">
                  暂无通知
                </div>
                <div 
                  v-for="item in notifications" 
                  :key="item.id" 
                  class="notification-item"
                >
                  <el-icon :class="['notification-icon', item.type]">
                    <component :is="getNotificationIcon(item.type)" />
                  </el-icon>
                  <div class="notification-content">
                    <div class="notification-title">{{ item.title }}</div>
                    <div class="notification-message">{{ item.message }}</div>
                    <div class="notification-time">{{ formatTime(item.timestamp) }}</div>
                  </div>
                </div>
              </el-scrollbar>
            </div>
          </el-popover>

          <!-- 连接状态 -->
          <el-tooltip :content="connectionStatus" placement="bottom" :disabled="isMobile">
            <div class="connection-wrapper">
              <div :class="['connection-indicator', { connected: isConnected }]" />
            </div>
          </el-tooltip>

          <el-dropdown trigger="click" @command="handleUserCommand">
            <el-button class="user-button">
              <el-icon><UserFilled /></el-icon>
              <span class="user-name">{{ authUserName }}</span>
            </el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item disabled>
                  {{ authStore.user?.role === 'admin' ? '管理员' : '普通用户' }}
                </el-dropdown-item>
                <el-dropdown-item command="logout">退出登录</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </el-header>

      <!-- 内容区 -->
      <el-main class="main-content">
        <router-view v-slot="{ Component }">
          <transition name="fade" mode="out-in">
            <component :is="Component" />
          </transition>
        </router-view>
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Search, Bell, WarningFilled, SuccessFilled, InfoFilled, Moon, Sunny, Menu, UserFilled } from '@element-plus/icons-vue'
import { useAppStore } from '@/stores/app'
import { useAuthStore } from '@/stores/auth'
import { stockApi } from '@/api'
import signalRService from '@/api/signalr'
import dayjs from 'dayjs'
import relativeTime from 'dayjs/plugin/relativeTime'
import 'dayjs/locale/zh-cn'

dayjs.extend(relativeTime)
dayjs.locale('zh-cn')

const route = useRoute()
const router = useRouter()
const appStore = useAppStore()
const authStore = useAuthStore()

const searchQuery = ref('')
const isConnected = ref(false)
const isMobile = ref(false)

const sidebarCollapsed = computed(() => appStore.sidebarCollapsed)
const notifications = computed(() => appStore.notifications)
const unreadCount = computed(() => notifications.value.length)
const asideWidth = computed(() => {
  if (isMobile.value) {
    return '220px'
  }

  return sidebarCollapsed.value ? '64px' : '240px'
})

const currentRoute = computed(() => route.path)
const currentTitle = computed(() => route.meta?.title || '')

const menuRoutes = computed(() => {
  const rootRoute = router.options.routes.find((item) => Array.isArray(item.children) && item.children.length > 0)
  const routes = rootRoute?.children || []
  return routes
    .filter(r => !r.meta?.hidden)
    .filter(r => !r.meta?.admin || authStore.isAdmin)
    .map((r) => ({
      ...r,
      fullPath: r.path.startsWith('/') ? r.path : `/${r.path}`
    }))
})

const connectionStatus = computed(() => 
  isConnected.value ? '实时连接正常' : '实时连接断开'
)
const authUserName = computed(() => authStore.user?.displayName || authStore.user?.username || '用户')

function toggleSidebar() {
  appStore.toggleSidebar()
}

function handleMenuSelect() {
  if (isMobile.value && !sidebarCollapsed.value) {
    appStore.toggleSidebar()
  }
}

function syncViewport() {
  if (typeof window === 'undefined') {
    return
  }

  const mobile = window.innerWidth <= 960
  isMobile.value = mobile
  if (mobile && !appStore.sidebarCollapsed) {
    appStore.sidebarCollapsed = true
  }
}

async function searchStock(query: string, cb: (results: any[]) => void) {
  if (!query || query.length < 1) {
    cb([])
    return
  }
  try {
    const results = await stockApi.search(query)
    cb(results)
  } catch {
    cb([])
  }
}

function handleSelectStock(item: any) {
  router.push(`/stock/${item.symbol}`)
  searchQuery.value = ''
}

function clearNotifications() {
  appStore.clearNotifications()
}

async function handleUserCommand(command: string) {
  if (command === 'logout') {
    authStore.logout()
    await router.replace('/login')
  }
}

function getNotificationIcon(type: string) {
  switch (type) {
    case 'success': return SuccessFilled
    case 'warning': return WarningFilled
    case 'error': return WarningFilled
    default: return InfoFilled
  }
}

function formatTime(timestamp: string) {
  return dayjs(timestamp).fromNow()
}

onMounted(async () => {
  appStore.applyTheme()
  syncViewport()
  window.addEventListener('resize', syncViewport)

  // 连接SignalR
  await signalRService.connect()
  isConnected.value = true
  
  // 初始化监听
  appStore.initSignalRListeners()
  
  // 加载数据
  appStore.fetchStrategies()
  appStore.fetchWatchlist()
})

onUnmounted(() => {
  window.removeEventListener('resize', syncViewport)
  signalRService.disconnect()
})
</script>

<style lang="scss" scoped>
.main-layout {
  height: 100vh;
  background: var(--qt-bg);
}

.sidebar {
  background: var(--qt-sidebar-bg);
  display: flex;
  flex-direction: column;
  transition: width 0.3s ease, transform 0.3s ease;
  overflow: hidden;
  z-index: 1100;
}

.logo {
  height: 64px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  color: #fff;
  border-bottom: 1px solid var(--qt-sidebar-border);
  flex-shrink: 0;

  .logo-text {
    font-size: 18px;
    font-weight: 600;
    white-space: nowrap;
  }
}

.sidebar-menu {
  flex: 1;
  background: transparent;
  border: none;
  overflow-y: auto;

  :deep(.el-menu-item) {
    color: #9ca3af;
    
    &:hover {
      background: #374151;
      color: #fff;
    }
    
    &.is-active {
      background: #1a56db;
      color: #fff;
    }
  }
}

.sidebar-footer {
  padding: 12px;
  border-top: 1px solid var(--qt-sidebar-border);
  display: flex;
  justify-content: center;

  :deep(.el-button) {
    color: #9ca3af;
    
    &:hover {
      color: #fff;
    }
  }
}

.header {
  background: var(--qt-header-bg);
  border-bottom: 1px solid var(--qt-border);
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px;
  height: 64px;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
  margin-left: auto;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}

.stock-search {
  width: 260px;
  flex: 0 1 260px;
  max-width: 100%;

  :deep(.el-input__wrapper) {
    border-radius: 20px;
  }
}

.theme-toggle {
  border: 1px solid color-mix(in srgb, #3b82f6 45%, transparent);
  background: linear-gradient(135deg, #eff6ff 0%, #dbeafe 100%);
  color: #1d4ed8;
  box-shadow: 0 6px 18px rgba(59, 130, 246, 0.22);
  transition: transform 0.2s ease, box-shadow 0.2s ease;

  &:hover {
    transform: translateY(-1px);
    box-shadow: 0 10px 22px rgba(59, 130, 246, 0.3);
  }
}

:global(html.dark) .theme-toggle {
  border-color: color-mix(in srgb, #38bdf8 55%, transparent);
  background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%);
  color: #fde047;
  box-shadow: 0 6px 18px rgba(15, 23, 42, 0.5);
}

.search-item {
  display: flex;
  align-items: center;
  gap: 12px;

  .symbol {
    font-weight: 600;
    color: #1a56db;
  }

  .name {
    color: var(--qt-text-secondary);
    font-size: 13px;
  }
}

.notification-badge {
  :deep(.el-badge__content) {
    top: 8px;
    right: 8px;
  }
}

.notification-panel {
  color: var(--qt-text-primary);

  .notification-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding-bottom: 12px;
    border-bottom: 1px solid var(--qt-border);
    margin-bottom: 12px;
    font-weight: 600;
  }

  .notification-empty {
    text-align: center;
    color: var(--qt-text-muted);
    padding: 24px 0;
  }

  .notification-item {
    display: flex;
    gap: 12px;
    padding: 12px 0;
    border-bottom: 1px solid var(--qt-border);

    &:last-child {
      border-bottom: none;
    }
  }

  .notification-icon {
    font-size: 20px;

    &.success { color: #10b981; }
    &.warning { color: #f59e0b; }
    &.error { color: #ef4444; }
    &.info { color: #6b7280; }
  }

  .notification-content {
    flex: 1;
    min-width: 0;
  }

  .notification-title {
    font-weight: 500;
    margin-bottom: 4px;
  }

  .notification-message {
    font-size: 13px;
    color: var(--qt-text-secondary);
    margin-bottom: 4px;
  }

  .notification-time {
    font-size: 12px;
    color: var(--qt-text-muted);
  }
}

.connection-indicator {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: #ef4444;

  &.connected {
    background: #10b981;
  }
}

.connection-wrapper {
  display: flex;
  align-items: center;
  justify-content: center;
}

.user-button {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  max-width: 150px;
}

.user-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.main-content {
  background: var(--qt-bg);
  padding: 24px;
  overflow-y: auto;
}

.mobile-menu-btn {
  color: var(--qt-text-secondary);
}

.sidebar-mask {
  position: fixed;
  inset: 0;
  z-index: 1090;
  background: rgba(15, 23, 42, 0.35);
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

@media (max-width: 960px) {
  .sidebar {
    position: fixed;
    left: 0;
    top: 0;
    bottom: 0;
    width: 220px !important;
    transform: translateX(-100%);
    box-shadow: 0 8px 30px rgba(15, 23, 42, 0.45);
  }

  .sidebar.open {
    transform: translateX(0);
  }

  .header {
    min-height: 56px;
    height: auto;
    padding: 0 12px;
    gap: 8px;
    flex-wrap: wrap;
    align-content: flex-start;
    padding-top: 8px;
    padding-bottom: 8px;
  }

  .header-left,
  .header-right {
    width: 100%;
  }

  .header-left {
    justify-content: space-between;
  }

  .header-right {
    gap: 8px;
    align-items: center;
  }

  .stock-search {
    width: auto;
    min-width: 0;
    flex: 1 1 auto;
    order: 1;

    :deep(.el-input__wrapper) {
      height: 34px;
    }
  }

  .theme-toggle {
    order: 2;
  }

  .notification-badge {
    order: 3;
  }

  .connection-wrapper {
    display: none;
  }

  .user-button {
    order: 4;
    max-width: 120px;
  }

  :deep(.el-breadcrumb__item:last-child .el-breadcrumb__inner) {
    max-width: 130px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .main-content {
    padding: 12px;
  }
}
</style>
