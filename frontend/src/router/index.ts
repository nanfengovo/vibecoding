import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('@/views/Login.vue'),
    meta: { public: true, title: '登录' }
  },
  {
    path: '/',
    component: () => import('@/layouts/MainLayout.vue'),
    redirect: '/dashboard',
    children: [
      {
        path: 'dashboard',
        name: 'Dashboard',
        component: () => import('@/views/Dashboard.vue'),
        meta: { title: '仪表盘', icon: 'Odometer' }
      },
      {
        path: 'watchlist',
        name: 'Watchlist',
        component: () => import('@/views/Watchlist.vue'),
        meta: { title: '股票监控', icon: 'View' }
      },
      {
        path: 'stock/:symbol',
        name: 'StockDetail',
        component: () => import('@/views/StockDetail.vue'),
        meta: { title: '股票详情', hidden: true }
      },
      {
        path: 'strategies',
        name: 'Strategies',
        component: () => import('@/views/Strategies.vue'),
        meta: { title: '策略中心', icon: 'Setting' }
      },
      {
        path: 'strategy/create',
        name: 'StrategyCreate',
        component: () => import('@/views/StrategyEditor.vue'),
        meta: { title: '创建策略', hidden: true }
      },
      {
        path: 'strategy/:id/edit',
        name: 'StrategyEdit',
        component: () => import('@/views/StrategyEditor.vue'),
        meta: { title: '编辑策略', hidden: true }
      },
      {
        path: 'backtest',
        name: 'Backtest',
        component: () => import('@/views/Backtest.vue'),
        meta: { title: '回测中心', icon: 'DataAnalysis' }
      },
      {
        path: 'trades',
        name: 'Trades',
        component: () => import('@/views/Trades.vue'),
        meta: { title: '交易记录', icon: 'List' }
      },
      {
        path: 'review',
        name: 'Review',
        component: () => import('@/views/Review.vue'),
        meta: { title: '复盘分析', icon: 'TrendCharts' }
      },
      {
        path: 'ai-chat',
        name: 'AiChat',
        component: () => import('@/views/AiChat.vue'),
        meta: { title: 'AI Chat', icon: 'ChatDotRound' }
      },
      {
        path: 'crawler',
        name: 'Crawler',
        component: () => import('@/views/Crawler.vue'),
        meta: { title: '信息采集', icon: 'Connection' }
      },
      {
        path: 'knowledge',
        name: 'KnowledgeBase',
        component: () => import('@/views/KnowledgeBase.vue'),
        meta: { title: '知识库', icon: 'Collection' }
      },
      {
        path: 'lowcode',
        name: 'LowCode',
        component: () => import('@/views/LowCodeWorkbench.vue'),
        meta: { title: '低代码平台', icon: 'Operation' }
      },
      {
        path: 'settings',
        name: 'Settings',
        component: () => import('@/views/Settings.vue'),
        meta: { title: '系统设置', icon: 'Tools', admin: true }
      },
      {
        path: 'users',
        name: 'Users',
        component: () => import('@/views/Users.vue'),
        meta: { title: '用户管理', icon: 'User', admin: true }
      }
    ]
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  if (to.meta.public) {
    if (to.path === '/login' && auth.isAuthenticated) {
      return '/dashboard'
    }
    return true
  }

  if (!auth.token) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }

  if (!auth.user) {
    try {
      await auth.refreshUser()
    } catch {
      auth.logout()
      return { path: '/login', query: { redirect: to.fullPath } }
    }
  }

  if (to.meta.admin && !auth.isAdmin) {
    return '/dashboard'
  }

  return true
})

export default router
