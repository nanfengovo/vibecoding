<template>
  <div class="settings">
    <div class="page-header">
      <h1>系统设置</h1>
    </div>

    <el-tabs v-model="activeTab" class="settings-tabs">
      <!-- 长桥API配置 -->
      <el-tab-pane label="长桥API" name="longbridge">
        <div class="card">
          <h3>长桥OpenAPI配置</h3>
          <el-form :model="config.longBridge" label-width="120px">
            <el-form-item label="App Key">
              <el-input v-model="config.longBridge.appKey" placeholder="输入App Key" />
            </el-form-item>
            <el-form-item label="App Secret">
              <el-input 
                v-model="config.longBridge.appSecret" 
                type="password" 
                placeholder="输入App Secret"
                show-password
              />
            </el-form-item>
            <el-form-item label="Access Token">
              <el-input 
                v-model="config.longBridge.accessToken" 
                type="password" 
                placeholder="输入Access Token"
                show-password
              />
            </el-form-item>
            <el-form-item label="API地址">
              <el-input v-model="config.longBridge.baseUrl" placeholder="https://openapi.longbridge.com" />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" @click="saveLongBridge">保存</el-button>
              <el-button :loading="testing.longbridge" @click="testLongBridge">测试连接</el-button>
            </el-form-item>
          </el-form>
          <el-alert
            title="Access Token 说明"
            type="info"
            show-icon
            :closable="false"
            style="margin-top: 16px"
          >
            <template #default>
              Legacy 模式需要同时填写 `App Key`、`App Secret`、`Access Token` 三项（均来自 User Center → application credential）。
              若使用 OAuth token，请在 Access Token 前加 `Bearer ` 前缀，并将 App Key/App Secret 留空。
            </template>
          </el-alert>
        </div>
      </el-tab-pane>

      <!-- 代理配置 -->
      <el-tab-pane label="代理设置" name="proxy">
        <div class="card">
          <h3>HTTP代理配置</h3>
          <el-form :model="config.proxy" label-width="120px">
            <el-form-item label="启用代理">
              <el-switch v-model="config.proxy.enabled" />
            </el-form-item>
            <template v-if="config.proxy.enabled">
              <el-form-item label="代理地址">
                <el-input v-model="config.proxy.host" placeholder="127.0.0.1" />
              </el-form-item>
              <el-form-item label="代理端口">
                <el-input-number v-model="config.proxy.port" :min="1" :max="65535" />
              </el-form-item>
              <el-form-item label="用户名">
                <el-input v-model="config.proxy.username" placeholder="可选" />
              </el-form-item>
              <el-form-item label="密码">
                <el-input 
                  v-model="config.proxy.password" 
                  type="password" 
                  placeholder="可选"
                  show-password
                />
              </el-form-item>
            </template>
            <el-form-item>
              <el-button type="primary" @click="saveProxy">保存</el-button>
            </el-form-item>
          </el-form>
        </div>
      </el-tab-pane>

      <!-- 邮件配置 -->
      <el-tab-pane label="邮件通知" name="email">
        <div class="card">
          <h3>邮件服务配置</h3>
          <el-form :model="config.email" label-width="120px">
            <el-form-item label="启用邮件">
              <el-switch v-model="config.email.enabled" />
            </el-form-item>
            <template v-if="config.email.enabled">
              <el-form-item label="SMTP服务器">
                <el-input v-model="config.email.smtpHost" placeholder="smtp.example.com" />
              </el-form-item>
              <el-form-item label="SMTP端口">
                <el-input-number v-model="config.email.smtpPort" :min="1" :max="65535" />
              </el-form-item>
              <el-form-item label="SSL/TLS">
                <el-switch v-model="config.email.useSsl" />
              </el-form-item>
              <el-form-item label="用户名">
                <el-input v-model="config.email.username" placeholder="邮箱账号" />
              </el-form-item>
              <el-form-item label="密码">
                <el-input 
                  v-model="config.email.password" 
                  type="password" 
                  placeholder="邮箱密码或授权码"
                  show-password
                />
              </el-form-item>
              <el-form-item label="发件人地址">
                <el-input v-model="config.email.fromAddress" placeholder="sender@example.com" />
              </el-form-item>
              <el-form-item label="收件人地址">
                <el-select
                  v-model="config.email.toAddresses"
                  multiple
                  filterable
                  allow-create
                  placeholder="输入收件人邮箱"
                  style="width: 100%"
                />
              </el-form-item>
            </template>
            <el-form-item>
              <el-button type="primary" @click="saveEmail">保存</el-button>
              <el-button 
                :loading="testing.email" 
                :disabled="!config.email.enabled"
                @click="testEmail"
              >
                发送测试邮件
              </el-button>
            </el-form-item>
          </el-form>
        </div>
      </el-tab-pane>

      <!-- 飞书配置 -->
      <el-tab-pane label="飞书通知" name="feishu">
        <div class="card">
          <h3>飞书机器人配置</h3>
          <el-form :model="config.feishu" label-width="120px">
            <el-form-item label="启用飞书">
              <el-switch v-model="config.feishu.enabled" />
            </el-form-item>
            <template v-if="config.feishu.enabled">
              <el-form-item label="Webhook地址">
                <el-input 
                  v-model="config.feishu.webhookUrl" 
                  placeholder="https://open.feishu.cn/open-apis/bot/v2/hook/xxx"
                />
              </el-form-item>
              <el-form-item label="签名密钥">
                <el-input 
                  v-model="config.feishu.signSecret" 
                  type="password" 
                  placeholder="可选，用于加签验证"
                  show-password
                />
              </el-form-item>
            </template>
            <el-form-item>
              <el-button type="primary" @click="saveFeishu">保存</el-button>
              <el-button 
                :loading="testing.feishu" 
                :disabled="!config.feishu.enabled"
                @click="testFeishu"
              >
                发送测试消息
              </el-button>
            </el-form-item>
          </el-form>
          <el-alert 
            title="如何获取Webhook地址" 
            type="info" 
            show-icon 
            :closable="false"
            style="margin-top: 16px"
          >
            <template #default>
              <ol style="margin: 0; padding-left: 20px;">
                <li>在飞书群聊中，点击设置 → 群机器人 → 添加机器人</li>
                <li>选择"自定义机器人"，获取Webhook地址</li>
                <li>如需加签验证，请复制签名密钥</li>
              </ol>
            </template>
          </el-alert>
        </div>
      </el-tab-pane>

      <!-- 企业微信配置 -->
      <el-tab-pane label="企业微信通知" name="wechat">
        <div class="card">
          <h3>企业微信机器人配置</h3>
          <el-form :model="config.wechat" label-width="120px">
            <el-form-item label="启用微信">
              <el-switch v-model="config.wechat.enabled" />
            </el-form-item>
            <template v-if="config.wechat.enabled">
              <el-form-item label="Webhook地址">
                <el-input 
                  v-model="config.wechat.webhookUrl" 
                  placeholder="https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=xxx"
                />
              </el-form-item>
            </template>
            <el-form-item>
              <el-button type="primary" @click="saveWechat">保存</el-button>
              <el-button 
                :loading="testing.wechat" 
                :disabled="!config.wechat.enabled"
                @click="testWechat"
              >
                发送测试消息
              </el-button>
            </el-form-item>
          </el-form>
          <el-alert 
            title="如何获取Webhook地址" 
            type="info" 
            show-icon 
            :closable="false"
            style="margin-top: 16px"
          >
            <template #default>
              <ol style="margin: 0; padding-left: 20px;">
                <li>在企业微信群聊中，点击右上角 → 添加群机器人</li>
                <li>创建机器人后，复制Webhook地址</li>
              </ol>
            </template>
          </el-alert>
        </div>
      </el-tab-pane>

      <!-- OpenAI配置 -->
      <el-tab-pane label="AI分析" name="openai">
        <div class="card">
          <h3>AI 模型配置</h3>
          <el-form :model="config.openAi" label-width="120px">
            <el-form-item label="启用AI分析">
              <el-switch v-model="config.openAi.enabled" />
            </el-form-item>
            <template v-if="config.openAi.enabled">
              <el-form-item label="默认模型源">
                <div class="ai-provider-toolbar">
                  <el-select v-model="config.openAi.activeProviderId" placeholder="选择默认模型源" style="width: 260px">
                    <el-option
                      v-for="provider in config.openAi.providers"
                      :key="provider.id"
                      :label="provider.name"
                      :value="provider.id"
                    />
                  </el-select>
                  <el-button @click="addAiProvider">新增模型源</el-button>
                </div>
              </el-form-item>
              <el-form-item label="模型源列表">
                <div class="ai-provider-list">
                  <div
                    v-for="(provider, index) in config.openAi.providers"
                    :key="provider.id"
                    class="ai-provider-item"
                  >
                    <div class="ai-provider-item-header">
                      <span>模型源 {{ index + 1 }}</span>
                      <el-button
                        link
                        type="danger"
                        :disabled="config.openAi.providers.length <= 1"
                        @click="removeAiProvider(provider.id)"
                      >
                        删除
                      </el-button>
                    </div>
                    <el-input v-model="provider.name" placeholder="显示名称，例如 OpenAI 主账号" class="provider-input" />
                    <el-input
                      v-model="provider.apiKey"
                      type="password"
                      show-password
                      placeholder="API Key"
                      class="provider-input"
                    />
                    <el-input
                      v-model="provider.baseUrl"
                      placeholder="Base URL，例如 https://api.openai.com/v1"
                      class="provider-input"
                    />
                    <el-input
                      v-model="provider.model"
                      type="textarea"
                      :rows="2"
                      placeholder="模型列表，支持逗号/分号/换行分隔（按顺序回退）"
                      class="provider-input"
                    />
                  </div>
                </div>
                <div class="hint-text">
                  支持同一家或不同厂商：每个模型源可配置独立 `API Key + Base URL + 模型列表`，分析时可选择使用。
                </div>
              </el-form-item>
            </template>
            <el-form-item>
              <el-button type="primary" @click="saveOpenAi">保存</el-button>
              <el-button
                :loading="testing.openai"
                :disabled="!config.openAi.enabled"
                @click="testOpenAi"
              >
                测试连接
              </el-button>
            </el-form-item>
          </el-form>
        </div>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { configApi } from '@/api'
import type { AiProviderConfig, SystemConfig } from '@/types'

const activeTab = ref('longbridge')
const testing = ref({
  longbridge: false,
  email: false,
  feishu: false,
  wechat: false,
  openai: false
})

function createAiProvider(seed?: Partial<AiProviderConfig>, index = 0): AiProviderConfig {
  const id = String(seed?.id || `provider-${Date.now()}-${index}-${Math.random().toString(36).slice(2, 8)}`).trim()
  return {
    id,
    name: String(seed?.name || `模型源 ${index + 1}`).trim() || `模型源 ${index + 1}`,
    apiKey: String(seed?.apiKey || '').trim(),
    baseUrl: String(seed?.baseUrl || '').trim() || 'https://api.openai.com/v1',
    model: String(seed?.model || '').trim() || 'gpt-5-mini'
  }
}

function normalizeOpenAiConfig(openAi: SystemConfig['openAi']): SystemConfig['openAi'] {
  const providers = Array.isArray(openAi?.providers) && openAi.providers.length > 0
    ? openAi.providers.map((item, index) => createAiProvider(item, index))
    : [createAiProvider({
      id: 'default',
      name: '默认模型源',
      apiKey: openAi?.apiKey,
      baseUrl: openAi?.baseUrl,
      model: openAi?.model
    })]

  const preferredId = String(openAi?.activeProviderId || '').trim()
  const active = providers.find((item) => item.id === preferredId) || providers[0]

  return {
    ...openAi,
    providers,
    activeProviderId: active?.id || '',
    apiKey: active?.apiKey || '',
    baseUrl: active?.baseUrl || 'https://api.openai.com/v1',
    model: active?.model || 'gpt-5-mini'
  }
}

const config = ref<SystemConfig>({
  longBridge: {
    appKey: '',
    appSecret: '',
    accessToken: '',
    baseUrl: 'https://openapi.longbridge.com'
  },
  proxy: {
    enabled: false,
    host: '127.0.0.1',
    port: 7890
  },
  email: {
    enabled: false,
    smtpHost: '',
    smtpPort: 587,
    username: '',
    password: '',
    fromAddress: '',
    toAddresses: [],
    useSsl: true
  },
  feishu: {
    enabled: false,
    webhookUrl: ''
  },
  wechat: {
    enabled: false,
    webhookUrl: ''
  },
  openAi: {
    enabled: false,
    apiKey: '',
    baseUrl: 'https://api.openai.com/v1',
    model: 'gpt-5-mini',
    providers: [
      {
        id: 'default',
        name: '默认模型源',
        apiKey: '',
        baseUrl: 'https://api.openai.com/v1',
        model: 'gpt-5-mini'
      }
    ],
    activeProviderId: 'default'
  }
})

function getApiErrorMessage(error: unknown, fallback: string) {
  const responseMessage = (error as any)?.response?.data?.message
  if (typeof responseMessage === 'string' && responseMessage.trim()) {
    return responseMessage
  }

  const message = (error as Error | undefined)?.message
  if (typeof message === 'string' && message.trim()) {
    return message
  }

  return fallback
}

async function loadConfig() {
  try {
    const data = await configApi.get()
    config.value = {
      ...config.value,
      ...data,
      openAi: normalizeOpenAiConfig({
        ...config.value.openAi,
        ...(data?.openAi || {})
      } as SystemConfig['openAi'])
    }
  } catch (error) {
    console.error('Failed to load config:', error)
  }
}

async function saveLongBridge() {
  try {
    await configApi.update({ longBridge: config.value.longBridge })
    ElMessage.success('长桥配置已保存')
  } catch {
    ElMessage.error('保存失败')
  }
}

async function testLongBridge() {
  testing.value.longbridge = true
  try {
    await configApi.update({ longBridge: config.value.longBridge })
    await configApi.testLongBridge()
    ElMessage.success('连接成功')
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '连接失败，请检查配置'))
  } finally {
    testing.value.longbridge = false
  }
}

async function saveProxy() {
  try {
    await configApi.update({ proxy: config.value.proxy })
    ElMessage.success('代理配置已保存')
  } catch {
    ElMessage.error('保存失败')
  }
}

async function saveEmail() {
  try {
    await configApi.update({ email: config.value.email })
    ElMessage.success('邮件配置已保存')
  } catch {
    ElMessage.error('保存失败')
  }
}

async function testEmail() {
  testing.value.email = true
  try {
    await configApi.testEmail()
    ElMessage.success('测试邮件已发送')
  } catch {
    ElMessage.error('发送失败，请检查配置')
  } finally {
    testing.value.email = false
  }
}

async function saveFeishu() {
  try {
    await configApi.update({ feishu: config.value.feishu })
    ElMessage.success('飞书配置已保存')
  } catch {
    ElMessage.error('保存失败')
  }
}

async function testFeishu() {
  testing.value.feishu = true
  try {
    await configApi.testFeishu()
    ElMessage.success('测试消息已发送')
  } catch {
    ElMessage.error('发送失败，请检查配置')
  } finally {
    testing.value.feishu = false
  }
}

async function saveWechat() {
  try {
    await configApi.update({ wechat: config.value.wechat })
    ElMessage.success('微信配置已保存')
  } catch {
    ElMessage.error('保存失败')
  }
}

async function testWechat() {
  testing.value.wechat = true
  try {
    await configApi.testWechat()
    ElMessage.success('测试消息已发送')
  } catch {
    ElMessage.error('发送失败，请检查配置')
  } finally {
    testing.value.wechat = false
  }
}

function syncOpenAiLegacyFields() {
  config.value.openAi = normalizeOpenAiConfig(config.value.openAi)
}

function addAiProvider() {
  const next = createAiProvider(undefined, config.value.openAi.providers.length)
  config.value.openAi.providers.push(next)
  if (!config.value.openAi.activeProviderId) {
    config.value.openAi.activeProviderId = next.id
  }
  syncOpenAiLegacyFields()
}

function removeAiProvider(providerId: string) {
  const providers = config.value.openAi.providers
  if (providers.length <= 1) {
    return
  }

  const nextProviders = providers.filter((item) => item.id !== providerId)
  config.value.openAi.providers = nextProviders
  if (!nextProviders.some((item) => item.id === config.value.openAi.activeProviderId)) {
    config.value.openAi.activeProviderId = nextProviders[0]?.id || ''
  }
  syncOpenAiLegacyFields()
}

async function saveOpenAi() {
  try {
    syncOpenAiLegacyFields()
    await configApi.update({ openAi: config.value.openAi })
    ElMessage.success('OpenAI 配置已保存')
  } catch {
    ElMessage.error('保存失败')
  }
}

async function testOpenAi() {
  testing.value.openai = true
  try {
    syncOpenAiLegacyFields()
    await configApi.update({ openAi: config.value.openAi })
    const result: any = await configApi.testOpenAi()
    ElMessage.success(result?.message || 'OpenAI 连接成功')
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '连接失败，请检查配置'))
  } finally {
    testing.value.openai = false
  }
}

onMounted(() => {
  loadConfig()
})
</script>

<style lang="scss" scoped>
.settings {
  .settings-tabs {
    :deep(.el-tabs__content) {
      padding-top: 20px;
    }
  }

  .card {
    background: var(--qt-card-bg);
    border-radius: 8px;
    padding: 24px;
    border: 1px solid var(--qt-border);

    h3 {
      font-size: 16px;
      font-weight: 600;
      margin: 0 0 20px;
      color: var(--qt-text-primary);
    }
  }

  .hint-text {
    margin-top: 8px;
    color: var(--qt-text-muted);
    font-size: 12px;
    line-height: 1.6;
  }

  .ai-provider-toolbar {
    display: flex;
    gap: 10px;
    align-items: center;
    flex-wrap: wrap;
  }

  .ai-provider-list {
    width: 100%;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .ai-provider-item {
    border: 1px solid var(--qt-border);
    border-radius: 8px;
    padding: 12px;
    background: color-mix(in srgb, var(--qt-card-bg) 92%, #64748b 8%);
  }

  .ai-provider-item-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 8px;
    font-weight: 600;
    color: var(--qt-text-primary);
  }

  .provider-input + .provider-input {
    margin-top: 8px;
  }
}
</style>
