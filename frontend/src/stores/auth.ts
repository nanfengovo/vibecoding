import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { AUTH_TOKEN_KEY, AUTH_USER_KEY, authApi } from '@/api'
import type { AuthUser } from '@/types'

function readUser(): AuthUser | null {
  if (typeof localStorage === 'undefined') {
    return null
  }

  const raw = localStorage.getItem(AUTH_USER_KEY)
  if (!raw) {
    return null
  }

  try {
    return JSON.parse(raw) as AuthUser
  } catch {
    return null
  }
}

export const useAuthStore = defineStore('auth', () => {
  const token = ref(typeof localStorage === 'undefined' ? '' : localStorage.getItem(AUTH_TOKEN_KEY) || '')
  const user = ref<AuthUser | null>(readUser())
  const loading = ref(false)

  const isAuthenticated = computed(() => Boolean(token.value && user.value))
  const isAdmin = computed(() => user.value?.role === 'admin')

  function setSession(nextToken: string, nextUser: AuthUser) {
    token.value = nextToken
    user.value = nextUser
    localStorage.setItem(AUTH_TOKEN_KEY, nextToken)
    localStorage.setItem(AUTH_USER_KEY, JSON.stringify(nextUser))
  }

  async function login(username: string, password: string) {
    loading.value = true
    try {
      const result = await authApi.login({ username, password })
      setSession(result.token, result.user)
      return result.user
    } finally {
      loading.value = false
    }
  }

  async function refreshUser() {
    if (!token.value) {
      return null
    }

    const nextUser = await authApi.me()
    user.value = nextUser
    localStorage.setItem(AUTH_USER_KEY, JSON.stringify(nextUser))
    return nextUser
  }

  function logout() {
    token.value = ''
    user.value = null
    localStorage.removeItem(AUTH_TOKEN_KEY)
    localStorage.removeItem(AUTH_USER_KEY)
  }

  return {
    token,
    user,
    loading,
    isAuthenticated,
    isAdmin,
    login,
    refreshUser,
    logout
  }
})
