<template>
  <div class="min-h-screen bg-gray-50 py-8">
    <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
      <div class="bg-white shadow-sm rounded-lg p-6">
        <h1 class="text-3xl font-bold text-gray-900 mb-4">
          Admin Portal
        </h1>
        <p class="text-gray-600 mb-6">
          Welcome to the HybridAuth IdP Administration Interface
        </p>
        
        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div class="border border-gray-200 rounded-lg p-4 hover:shadow-md transition-shadow">
            <h2 class="text-xl font-semibold text-gray-800 mb-2">OIDC Clients</h2>
            <p class="text-gray-600 text-sm">
              Manage OpenID Connect client applications
            </p>
          </div>
          
          <div class="border border-gray-200 rounded-lg p-4 hover:shadow-md transition-shadow">
            <h2 class="text-xl font-semibold text-gray-800 mb-2">OIDC Scopes</h2>
            <p class="text-gray-600 text-sm">
              Configure available authentication scopes
            </p>
          </div>
        </div>
        
        <div class="mt-6 p-4 bg-blue-50 border border-blue-200 rounded-lg">
          <p class="text-blue-800 font-medium">
            ✅ Vue 3 + Vite + Tailwind CSS is working!
          </p>
          <p class="text-blue-600 text-sm mt-1">
            Phase 3.3 scaffolding complete. Ready for Phase 3.4 implementation.
          </p>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
// This will be expanded in Phase 3.4 with client and scope management
</script>
