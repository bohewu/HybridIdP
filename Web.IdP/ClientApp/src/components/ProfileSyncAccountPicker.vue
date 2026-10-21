<script setup>
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'
import { useProfileAccountPicker } from '@/composables/useProfileAccountPicker'
const props = defineProps({ canRead: Boolean })
const emit = defineEmits(['close', 'selected'])
const { t } = useI18n()
const { search, page, items, total, selected, loading, error, load, toggle } = useProfileAccountPicker(() => props.canRead)
onMounted(() => load())
</script>
<template>
  <BaseModal :show="true" :title="t('native-fields.mapping.chooseAccounts')" size="xl" @close="emit('close')">
    <template #body>
      <p v-if="!canRead" role="status" class="text-sm text-gray-600">{{ t('native-fields.mapping.readUsersRequired') }}</p>
      <div v-else class="space-y-4">
        <p class="text-sm text-gray-600">{{ t('native-fields.mapping.pickerHelp') }}</p>
        <form class="flex flex-wrap gap-2" @submit.prevent="load(true)">
          <label class="min-w-0 flex-1 text-sm">{{ t('native-fields.mapping.search') }}<input v-model="search" data-test="account-search" :placeholder="t('native-fields.mapping.searchPlaceholder')" class="mt-1 w-full rounded-md border border-gray-300 px-3 py-2" /></label>
          <button type="submit" :disabled="loading" class="self-end rounded-md border border-gray-300 px-3 py-2 text-sm disabled:opacity-50">{{ t('native-fields.mapping.search') }}</button>
        </form>
        <p v-if="error" role="alert" class="text-sm text-red-700">{{ t('profile-sync.reasons.request_failed') }}</p>
        <p v-if="loading" role="status" class="text-sm text-gray-600">{{ t('native-fields.loading') }}</p>
        <p v-else-if="!items.length" class="text-sm text-gray-600">{{ t('native-fields.mapping.noAccounts') }}</p>
        <ul v-else class="divide-y divide-gray-100">
          <li v-for="account in items" :key="account.id" class="py-3">
            <label class="flex min-w-0 items-center gap-3 text-sm"><input type="checkbox" :checked="selected.some(row => row.id === account.id)" :disabled="selected.length >= 50 && !selected.some(row => row.id === account.id)" @change="toggle(account)" /><span class="min-w-0 break-all">{{ account.userName }}</span></label>
          </li>
        </ul>
        <div class="flex flex-wrap items-center justify-between gap-3 text-sm">
          <span role="status">{{ t('native-fields.mapping.selectedCount', { count: selected.length }) }}</span>
          <div class="flex gap-3"><button type="button" :disabled="loading || page === 0" @click="page--; load()">{{ t('native-fields.mapping.previous') }}</button><button type="button" :disabled="loading || (page + 1) * 10 >= total" @click="page++; load()">{{ t('native-fields.mapping.next') }}</button></div>
        </div>
      </div>
    </template>
    <template #footer><div class="flex w-full flex-wrap justify-end gap-2"><button type="button" class="rounded-md border border-gray-300 px-4 py-2 text-sm" @click="emit('close')">{{ t('native-fields.mapping.cancel') }}</button><button type="button" data-test="review-selected" :disabled="!canRead || loading || !selected.length" class="rounded-md bg-google-500 px-4 py-2 text-sm text-white disabled:opacity-50" @click="emit('selected', [...selected])">{{ t('native-fields.mapping.reviewSelected') }}</button></div></template>
  </BaseModal>
</template>
