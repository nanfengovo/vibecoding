import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'

const routes: RouteRecordRaw[] = [
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
        path: 'lowcode',
        name: 'LowCode',
        component: () => import('@/views/LowCodeWorkbench.vue'),
        meta: { title: '低代码平台', icon: 'Operation' }
      },
      {
        path: 'settings',
        name: 'Settings',
        component: () => import('@/views/Settings.vue'),
        meta: { title: '系统设置', icon: 'Tools' }
      }
    ]
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

export default router
