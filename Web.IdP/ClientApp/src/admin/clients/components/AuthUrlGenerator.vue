<script setup>
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps({
  clientId: {
    type: String,
    required: true
  },
  redirectUri: {
    type: String,
    default: ''
  },
  scopes: {
    type: Array,
    required: true
  }
})

const testConfig = ref({
  show: false,
  codeVerifier: '',
  codeChallenge: '',
  url: '',
  copiedVerifier: false,
  copiedUrl: false
})

const generateCodeVerifier = () => {
  const array = new Uint8Array(32)
  window.crypto.getRandomValues(array)
  return btoa(String.fromCharCode.apply(null, array))
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

const generateCodeChallenge = async (verifier) => {
  const encoder = new TextEncoder()
  const data = encoder.encode(verifier)
  const hash = await window.crypto.subtle.digest('SHA-256', data)
  const hashArray = new Uint8Array(hash)
  return btoa(String.fromCharCode.apply(null, hashArray))
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

const generateTestUrl = async () => {
  const verifier = generateCodeVerifier()
  const challenge = await generateCodeChallenge(verifier)
  
  const scopeString = props.scopes.includes('openid') 
    ? props.scopes.join(' ') 
    : 'openid ' + props.scopes.join(' ')
    
  // Use first line of redirect URI if multiple lines
  const targetRedirect = (props.redirectUri || '').split('\n')[0]?.trim() || ''
  
  if (!targetRedirect) {
    // Basic alert fallback, parent should handle validation presentation usually
    // but this component acts as a tool
    alert('Redirect URI is required for generating URL') 
    return
  }

  const params = new URLSearchParams({
    client_id: props.clientId,
    redirect_uri: targetRedirect,
    response_type: 'code',
    scope: scopeString,
    code_challenge: challenge,
    code_challenge_method: 'S256',
    prompt: 'consent'
  })

  testConfig.value.codeVerifier = verifier
  testConfig.value.codeChallenge = challenge
  testConfig.value.url = `${window.location.origin}/connect/authorize?${params.toString()}`
  testConfig.value.show = true
  testConfig.value.copiedVerifier = false
  testConfig.value.copiedUrl = false
}

const copyToClipboard = (text, type) => {
  navigator.clipboard.writeText(text).then(() => {
    if (type === 'verifier') {
      testConfig.value.copiedVerifier = true
      setTimeout(() => testConfig.value.copiedVerifier = false, 2000)
    } else {
      testConfig.value.copiedUrl = true
      setTimeout(() => testConfig.value.copiedUrl = false, 2000)
    }
  })
}
</script>

<template>
  <div class="border-t border-gray-200 pt-5 mt-5">
    <div class="flex items-center justify-between">
      <h3 class="text-sm font-medium text-gray-900">{{ $t('clients.form.generator.title') }}</h3>
      <button 
        type="button"
        @click="testConfig.show = !testConfig.show"
        class="text-google-500 hover:text-google-700 text-sm font-medium focus:outline-none"
      >
        {{ testConfig.show ? $t('clients.form.cancel') : $t('clients.form.generator.generateButton') }}
      </button>
    </div>
    
    <div v-if="testConfig.show" class="mt-4 bg-gray-50 rounded-md p-4 space-y-4">
      <p class="text-xs text-gray-500">{{ $t('clients.form.generator.description') }}</p>
      
      <div class="flex justify-end">
         <button 
            type="button"
            @click="generateTestUrl"
            class="inline-flex items-center px-3 py-1.5 border border-transparent text-xs font-medium rounded-md text-google-600 bg-google-100 hover:bg-google-100 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-google-500"
          >
           <svg class="h-4 w-4 mr-1.5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
             <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 10V3L4 14h7v7l9-11h-7z" />
           </svg>
           {{ $t('clients.form.generator.generateButton') }}
         </button>
      </div>
      
      <div v-if="testConfig.url" class="space-y-4 animate-fade-in">
         <!-- Code Verifier -->
         <div>
           <label class="block text-xs font-medium text-gray-700 mb-1">
             {{ $t('clients.form.generator.codeVerifier') }}
             <span class="text-gray-400 font-normal">- {{ $t('clients.form.generator.codeVerifierHelp') }}</span>
           </label>
           <div class="flex">
             <input 
               type="text" 
               readonly 
               :value="testConfig.codeVerifier" 
               class="block w-full rounded-l-md border-gray-300 bg-white text-gray-600 shadow-sm sm:text-xs h-8 px-2 focus:ring-google-500 focus:border-google-500"
             />
             <button 
               type="button"
               @click="copyToClipboard(testConfig.codeVerifier, 'verifier')"
               class="inline-flex items-center px-4 whitespace-nowrap rounded-r-md border border-l-0 border-gray-300 bg-gray-50 text-gray-500 sm:text-xs hover:bg-gray-100"
             >
               {{ testConfig.copiedVerifier ? $t('clients.form.generator.copied') : $t('clients.form.generator.copy') }}
             </button>
           </div>
         </div>
         
         <!-- URL -->
         <div>
           <label class="block text-xs font-medium text-gray-700 mb-1">
             {{ $t('clients.form.generator.generatedUrl') }}
             <span class="text-gray-400 font-normal">- {{ $t('clients.form.generator.generatedUrlHelp') }}</span>
           </label>
           <div class="relative rounded-md shadow-sm">
             <textarea 
               readonly 
               :value="testConfig.url" 
               rows="3"
               class="block w-full rounded-md border-gray-300 bg-white text-gray-600 shadow-sm sm:text-xs px-2 py-1.5 pr-12 focus:ring-google-500 focus:border-google-500"
             ></textarea>
             <div class="absolute top-2 right-2">
                <button 
                 type="button"
                 @click="copyToClipboard(testConfig.url, 'url')"
                 class="inline-flex items-center px-3 py-1 border border-gray-300 shadow-sm text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 focus:outline-none whitespace-nowrap"
               >
                 {{ testConfig.copiedUrl ? $t('clients.form.generator.copied') : $t('clients.form.generator.copy') }}
               </button>
             </div>
           </div>
         </div>
      </div>
    </div>
  </div>
</template>
