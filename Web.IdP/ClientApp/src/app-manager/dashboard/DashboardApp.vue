<template>
  <div class="max-w-5xl mx-auto py-8">
    <!-- Welcome Header -->
    <div class="mb-10 text-center">
      <h1 class="text-3xl font-normal text-gray-900 mb-2">{{ $t('app-manager.Welcome') }}, {{ userName }}</h1>
      <p class="text-base text-gray-600">{{ $t('app-manager.ManageYourApplicationsAndScopes') }}</p>
    </div>

    <!-- Stats Cards -->
    <div class="grid grid-cols-1 md:grid-cols-2 gap-6 mb-8">
      <!-- Clients Card -->
      <a href="/Admin/Clients" class="block p-6 bg-white border border-gray-200 rounded-lg hover:border-blue-300 transition-colors group">
        <div class="flex items-center gap-4">
          <div class="w-12 h-12 rounded-full bg-blue-50 flex items-center justify-center">
            <i class="bi bi-pc-display text-blue-600 text-xl"></i>
          </div>
          <div>
            <div class="text-sm text-gray-500">{{ $t('app-manager.MyClients') }}</div>
            <div class="text-2xl font-medium text-gray-900">{{ clientCount }}</div>
          </div>
        </div>
        <div class="mt-4 text-sm text-blue-600 group-hover:underline">
          {{ $t('app-manager.ManageClients') }} →
        </div>
      </a>

      <!-- Scopes Card -->
      <a href="/Admin/Scopes" class="block p-6 bg-white border border-gray-200 rounded-lg hover:border-green-300 transition-colors group">
        <div class="flex items-center gap-4">
          <div class="w-12 h-12 rounded-full bg-green-50 flex items-center justify-center">
            <i class="bi bi-shield-lock text-green-600 text-xl"></i>
          </div>
          <div>
            <div class="text-sm text-gray-500">{{ $t('app-manager.MyScopes') }}</div>
            <div class="text-2xl font-medium text-gray-900">{{ scopeCount }}</div>
          </div>
        </div>
        <div class="mt-4 text-sm text-green-600 group-hover:underline">
          {{ $t('app-manager.ManageScopes') }} →
        </div>
      </a>
    </div>

    <!-- Getting Started -->
    <div class="bg-white border border-gray-200 rounded-lg overflow-hidden">
      <div class="px-6 py-4 border-b border-gray-200">
        <h2 class="text-lg font-medium text-gray-900">{{ $t('app-manager.GettingStarted') }}</h2>
      </div>
      <div class="p-6">
        <div class="grid grid-cols-1 md:grid-cols-3 gap-8">
          <!-- Step 1 -->
          <div class="flex gap-4">
            <div class="flex-shrink-0">
              <div class="w-8 h-8 rounded-full bg-blue-100 text-blue-600 flex items-center justify-center font-medium text-sm">1</div>
            </div>
            <div>
              <h3 class="font-medium text-gray-900 mb-1">{{ $t('app-manager.CreateClient') }}</h3>
              <p class="text-sm text-gray-500">{{ $t('app-manager.CreateClientDescription') }}</p>
            </div>
          </div>

          <!-- Step 2 -->
          <div class="flex gap-4">
            <div class="flex-shrink-0">
              <div class="w-8 h-8 rounded-full bg-blue-100 text-blue-600 flex items-center justify-center font-medium text-sm">2</div>
            </div>
            <div>
              <h3 class="font-medium text-gray-900 mb-1">{{ $t('app-manager.ConfigureScopes') }}</h3>
              <p class="text-sm text-gray-500">{{ $t('app-manager.ConfigureScopesDescription') }}</p>
            </div>
          </div>

          <!-- Step 3 -->
          <div class="flex gap-4">
            <div class="flex-shrink-0">
              <div class="w-8 h-8 rounded-full bg-blue-100 text-blue-600 flex items-center justify-center font-medium text-sm">3</div>
            </div>
            <div>
              <h3 class="font-medium text-gray-900 mb-1">{{ $t('app-manager.IntegrateApplication') }}</h3>
              <p class="text-sm text-gray-500">{{ $t('app-manager.IntegrateApplicationDescription') }}</p>
            </div>
          </div>
        </div>

        <div class="mt-8 pt-6 border-t border-gray-100">
          <a href="/ApplicationManager/Docs/Quickstart" class="text-blue-600 text-sm font-medium hover:underline">
            <i class="bi bi-book mr-1"></i> {{ $t('app-manager.ViewGuide') }}
          </a>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'

const userName = ref('')
const clientCount = ref(0)
const scopeCount = ref(0)

onMounted(async () => {
  // Get data from the page's data attributes
  const appEl = document.getElementById('app')
  if (appEl) {
    userName.value = appEl.dataset.userName || ''
    clientCount.value = parseInt(appEl.dataset.clientCount || '0', 10)
    scopeCount.value = parseInt(appEl.dataset.scopeCount || '0', 10)
  }
})
</script>
