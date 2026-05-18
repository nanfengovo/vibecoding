import type { AiProviderConfig, SystemConfig } from '@/types'

export function createAiProvider(seed?: Partial<AiProviderConfig>, index = 0): AiProviderConfig {
  return {
    id: String(seed?.id || `provider-${index + 1}`),
    name: String(seed?.name || `模型源 ${index + 1}`),
    apiKey: String(seed?.apiKey || ''),
    baseUrl: String(seed?.baseUrl || '').trim() || 'https://api.openai.com/v1',
    model: String(seed?.model || '').trim() || 'gpt-5-mini'
  }
}

export function normalizeAiProviders(openAi?: SystemConfig['openAi']): AiProviderConfig[] {
  const rows = Array.isArray(openAi?.providers)
    ? openAi.providers.map((item, index) => createAiProvider(item, index))
    : []
  if (rows.length > 0) {
    return rows
  }

  return [
    createAiProvider({
      id: 'default',
      name: '默认模型源',
      apiKey: String(openAi?.apiKey || ''),
      baseUrl: String(openAi?.baseUrl || '').trim() || 'https://api.openai.com/v1',
      model: String(openAi?.model || '').trim() || 'gpt-5-mini'
    })
  ]
}

export function parseModelCandidates(raw: string): string[] {
  const list = String(raw || '')
    .split(/[\n,;|]+/g)
    .map((item) => item.trim())
    .filter(Boolean)
  return Array.from(new Set(list))
}

export function resolvePreferredProviderId(providers: AiProviderConfig[], preferredId?: string): string {
  if (providers.length === 0) {
    return ''
  }
  const preferred = String(preferredId || '').trim()
  if (preferred && providers.some((item) => item.id === preferred)) {
    return preferred
  }
  return providers[0].id
}
