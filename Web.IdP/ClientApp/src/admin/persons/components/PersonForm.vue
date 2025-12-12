<script setup>
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'
import { useIdentityValidation } from '@/composables/useIdentityValidation'

const { t } = useI18n()
const { validateTaiwanNationalId, validatePassportNumber, validateResidentCertificate } = useIdentityValidation()

const props = defineProps({
	person: {
		type: Object,
		default: null
	}
})

const emit = defineEmits(['close', 'save'])

const isEdit = computed(() => !!props.person)
const saving = ref(false)
const error = ref(null)

// Identity validation errors
const identityErrors = ref({
	nationalId: null,
	passportNumber: null,
	residentCertificateNumber: null
})

// Form fields
const formData = ref({
	firstName: '',
	middleName: '',
	lastName: '',
	nickname: '',
	employeeId: '',
	department: '',
	jobTitle: '',
	email: '',
	phoneNumber: '',
	address: '',
	birthdate: '',
	gender: '',
	timeZone: '',
	locale: '',
	profileUrl: '',
	pictureUrl: '',
	website: '',
	// Phase 18: Lifecycle fields
	status: 'Active',
	startDate: '',
	endDate: '',
	// Phase 10.6: Identity fields
	nationalId: '',
	passportNumber: '',
	residentCertificateNumber: ''
})

// Load person data for edit
if (props.person) {
	formData.value = {
		firstName: props.person.firstName || '',
		middleName: props.person.middleName || '',
		lastName: props.person.lastName || '',
		nickname: props.person.nickname || '',
		employeeId: props.person.employeeId || '',
		department: props.person.department || '',
		jobTitle: props.person.jobTitle || '',
		email: props.person.email || '',
		phoneNumber: props.person.phoneNumber || '',
		address: props.person.address || '',
		birthdate: props.person.birthdate || '',
		gender: props.person.gender || '',
		timeZone: props.person.timeZone || '',
		locale: props.person.locale || '',
		profileUrl: props.person.profileUrl || '',
		pictureUrl: props.person.pictureUrl || '',
		website: props.person.website || '',
		// Phase 18: Lifecycle fields
		status: props.person.status || 'Active',
		startDate: props.person.startDate ? props.person.startDate.split('T')[0] : '',
		endDate: props.person.endDate ? props.person.endDate.split('T')[0] : '',
		// Phase 10.6: Identity fields
		nationalId: props.person.nationalId || '',
		passportNumber: props.person.passportNumber || '',
		residentCertificateNumber: props.person.residentCertificateNumber || ''
	}
}

// Real-time validation watchers - validate each field independently when it has a value
watch(() => formData.value.nationalId, (newValue) => {
	if (newValue && newValue !== '●●●●●●●●●●') {
		const result = validateTaiwanNationalId(newValue)
		identityErrors.value.nationalId = result.error
	} else {
		identityErrors.value.nationalId = null
	}
})

watch(() => formData.value.passportNumber, (newValue) => {
	if (newValue && newValue !== '●●●●●●●●●●') {
		const result = validatePassportNumber(newValue)
		identityErrors.value.passportNumber = result.error
	} else {
		identityErrors.value.passportNumber = null
	}
})

watch(() => formData.value.residentCertificateNumber, (newValue) => {
	if (newValue && newValue !== '●●●●●●●●●●') {
		const result = validateResidentCertificate(newValue)
		identityErrors.value.residentCertificateNumber = result.error
	} else {
		identityErrors.value.residentCertificateNumber = null
	}
})

const handleSubmit = async () => {
	error.value = null

	// Basic validation
	if (!formData.value.firstName?.trim()) {
		error.value = t('persons.errors.firstNameRequired')
		return
	}
	if (!formData.value.lastName?.trim()) {
		error.value = t('persons.errors.lastNameRequired')
		return
	}

	// Email format validation
	if (formData.value.email?.trim()) {
		const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
		if (!emailRegex.test(formData.value.email.trim())) {
			error.value = t('persons.errors.invalidEmail')
			return
		}
	}

	// Identity document validation - validate each filled field independently
	// Skip validation if value is masked placeholder (●●●●●●●●●●) - means existing value unchanged
	const MASKED_PLACEHOLDER = '●●●●●●●●●●'
	
	const nationalId = formData.value.nationalId?.trim()
	if (nationalId && nationalId !== MASKED_PLACEHOLDER) {
		const result = validateTaiwanNationalId(nationalId)
		if (!result.valid) {
			error.value = result.error
			return
		}
	}
	
	const passportNumber = formData.value.passportNumber?.trim()
	if (passportNumber && passportNumber !== MASKED_PLACEHOLDER) {
		const result = validatePassportNumber(passportNumber)
		if (!result.valid) {
			error.value = result.error
			return
		}
	}
	
	const residentCertNumber = formData.value.residentCertificateNumber?.trim()
	if (residentCertNumber && residentCertNumber !== MASKED_PLACEHOLDER) {
		const result = validateResidentCertificate(residentCertNumber)
		if (!result.valid) {
			error.value = result.error
			return
		}
	}

	saving.value = true

	try {
		const url = isEdit.value
			? `/api/admin/people/${props.person.id}`
			: '/api/admin/people'

		const method = isEdit.value ? 'PUT' : 'POST'
		
		// Convert empty date strings to null for proper DateTime? parsing
		// Also clear masked PID placeholders (●●●●●●●●●●) - empty means keep existing value
		const MASKED_PLACEHOLDER = '●●●●●●●●●●'
		const payload = {
			...formData.value,
			startDate: formData.value.startDate || null,
			endDate: formData.value.endDate || null,
			birthdate: formData.value.birthdate || null,
			// Clear masked values - empty string means keep existing on backend
			nationalId: formData.value.nationalId === MASKED_PLACEHOLDER ? '' : formData.value.nationalId,
			passportNumber: formData.value.passportNumber === MASKED_PLACEHOLDER ? '' : formData.value.passportNumber,
			residentCertificateNumber: formData.value.residentCertificateNumber === MASKED_PLACEHOLDER ? '' : formData.value.residentCertificateNumber
		}
		
		const response = await fetch(url, {
			method,
			headers: {
				'Content-Type': 'application/json'
			},
			body: JSON.stringify(payload)
		})

		if (!response.ok) {
			const errorText = await response.text()
			let errorMessage = t('persons.errors.saveFailed', { message: '' })
			
			// Try to parse as JSON and extract meaningful error info
			try {
				const errorJson = JSON.parse(errorText)
				if (errorJson.title) {
					errorMessage = errorJson.title
				}
				// If there are validation errors, show the first one
				if (errorJson.errors) {
					const firstError = Object.values(errorJson.errors).flat()[0]
					if (firstError) {
						errorMessage = String(firstError)
					}
				}
			} catch {
				// Not JSON, use the text if it's short
				if (errorText && errorText.length < 200) {
					errorMessage = errorText
				}
			}
			
			throw new Error(errorMessage)
		}

		emit('save')
	} catch (e) {
		error.value = e.message || t('persons.errors.saveFailed', { message: '' })
		console.error('Error saving person:', e)
	} finally {
		saving.value = false
	}
}

const handleClose = () => {
	emit('close')
}
</script>

<template>
	<BaseModal :show="true" :title="isEdit ? t('persons.editPerson') : t('persons.createPerson')" size="xl"
		:show-close-icon="true" :close-on-backdrop="false" :close-on-esc="true" :loading="saving" @close="handleClose">
		<template #body>
			<form @submit.prevent="handleSubmit">
				<!-- Error Message -->
				<div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
					<p class="text-sm text-red-700">{{ error }}</p>
				</div>

				<div class="max-h-[65vh] overflow-y-auto px-1">
					<div class="space-y-4">
						<!-- Name Section -->
						<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
							<div>
								<label for="firstName" class="block text-sm font-medium text-gray-700">
									{{ t('persons.form.firstName') }} <span class="text-red-500">*</span>
								</label>
								<input id="firstName" v-model="formData.firstName" type="text" required
									:placeholder="t('persons.form.firstName')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>

							<div>
								<label for="middleName" class="block text-sm font-medium text-gray-700">
									{{ t('persons.form.middleName') }}
								</label>
								<input id="middleName" v-model="formData.middleName" type="text"
									:placeholder="t('persons.form.middleName')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>

							<div>
								<label for="lastName" class="block text-sm font-medium text-gray-700">
									{{ t('persons.form.lastName') }} <span class="text-red-500">*</span>
								</label>
								<input id="lastName" v-model="formData.lastName" type="text" required
									:placeholder="t('persons.form.lastName')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>
						</div>

						<!-- Nickname -->
						<div>
							<label for="nickname" class="block text-sm font-medium text-gray-700">
								{{ t('persons.form.nickname') }}
							</label>
							<input id="nickname" v-model="formData.nickname" type="text"
								:placeholder="t('persons.form.nickname')"
								class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
						</div>

						<!-- Contact Information -->
						<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
							<div>
								<label for="email" class="block text-sm font-medium text-gray-700">
									{{ t('persons.form.email') }}
								</label>
								<input id="email" v-model="formData.email" type="email"
									:placeholder="t('persons.form.email')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>

							<div>
								<label for="phoneNumber" class="block text-sm font-medium text-gray-700">
									{{ t('persons.form.phoneNumber') }}
								</label>
								<input id="phoneNumber" v-model="formData.phoneNumber" type="tel"
									:placeholder="t('persons.form.phoneNumber')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>
						</div>

						<!-- Employment Info -->
						<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
							<div>
								<label for="employeeId" class="block text-sm font-medium text-gray-700">
									{{ t('persons.form.employeeId') }}
								</label>
								<input id="employeeId" v-model="formData.employeeId" type="text"
									:placeholder="t('persons.form.employeeId')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>

							<div>
								<label for="department" class="block text-sm font-medium text-gray-700">
									{{ t('persons.form.department') }}
								</label>
								<input id="department" v-model="formData.department" type="text"
									:placeholder="t('persons.form.department')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>

							<div>
								<label for="jobTitle" class="block text-sm font-medium text-gray-700">
									{{ t('persons.form.jobTitle') }}
								</label>
								<input id="jobTitle" v-model="formData.jobTitle" type="text"
									:placeholder="t('persons.form.jobTitle')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>
						</div>

						<!-- Phase 18: Lifecycle Info -->
						<div class="border-t pt-4">
							<h4 class="text-sm font-medium text-gray-700 mb-3">{{ t('persons.form.lifecycleInfo') }}</h4>
							<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
								<div>
									<label for="status" class="block text-sm font-medium text-gray-700">
										{{ t('persons.form.status') }}
									</label>
									<select id="status" v-model="formData.status"
										class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm">
										<option value="Pending">{{ t('persons.statuses.pending') }}</option>
										<option value="Active">{{ t('persons.statuses.active') }}</option>
										<option value="Suspended">{{ t('persons.statuses.suspended') }}</option>
										<option value="Resigned">{{ t('persons.statuses.resigned') }}</option>
										<option value="Terminated">{{ t('persons.statuses.terminated') }}</option>
									</select>
								</div>

								<div>
									<label for="startDate" class="block text-sm font-medium text-gray-700">
										{{ t('persons.form.startDate') }}
									</label>
									<input id="startDate" v-model="formData.startDate" type="date"
										class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
								</div>

								<div>
									<label for="endDate" class="block text-sm font-medium text-gray-700">
										{{ t('persons.form.endDate') }}
									</label>
									<input id="endDate" v-model="formData.endDate" type="date"
										class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
								</div>
							</div>
						</div>

						<!-- Personal Info (Optional) -->
						<div class="border-t pt-4">
							<h4 class="text-sm font-medium text-gray-700 mb-3">{{ t('persons.form.personalInfo')
								}}</h4>
							<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
								<div>
									<label for="gender" class="block text-sm font-medium text-gray-700">
										{{ t('persons.form.gender') }}
									</label>
									<input id="gender" v-model="formData.gender" type="text"
										:placeholder="t('persons.form.gender')"
										class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
								</div>

								<div>
									<label for="birthdate" class="block text-sm font-medium text-gray-700">
										{{ t('persons.form.birthdate') }}
									</label>
									<input id="birthdate" v-model="formData.birthdate" type="date"
										class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
								</div>
							</div>

							<div class="mt-4">
								<label for="address" class="block text-sm font-medium text-gray-700">
									{{ t('persons.form.address') }}
								</label>
								<textarea id="address" v-model="formData.address" rows="2"
									:placeholder="t('persons.form.address')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"></textarea>
							</div>

							<div class="grid grid-cols-1 gap-4 sm:grid-cols-2 mt-4">
								<div>
									<label for="timeZone" class="block text-sm font-medium text-gray-700">
										{{ t('persons.form.timeZone') }}
									</label>
									<input id="timeZone" v-model="formData.timeZone" type="text"
										:placeholder="t('persons.form.timeZone')"
										class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
								</div>

								<div>
									<label for="locale" class="block text-sm font-medium text-gray-700">
										{{ t('persons.form.locale') }}
									</label>
									<input id="locale" v-model="formData.locale" type="text"
										:placeholder="t('persons.form.locale')"
										class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
								</div>
							</div>
						</div>

						<!-- Phase 10.6: Identity Verification -->
						<div class="border-t pt-4">
							<h4 class="text-sm font-medium text-gray-700 mb-3">{{ t('persons.form.identityVerification')
								}}</h4>
							
							<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
								<div>
									<label for="nationalId" class="block text-sm font-medium text-gray-700">
										{{ t('persons.form.nationalId') }}
									</label>
									<input id="nationalId" v-model="formData.nationalId" type="text"
										:placeholder="t('persons.form.nationalIdPlaceholder')"
										maxlength="20"
										:class="[
											'mt-1 block w-full border rounded-md shadow-sm py-2 px-3 focus:outline-none sm:text-sm',
											identityErrors.nationalId 
												? 'border-red-300 focus:ring-red-500 focus:border-red-500' 
												: 'border-gray-300 focus:ring-indigo-500 focus:border-indigo-500'
										]" />
									<p v-if="identityErrors.nationalId" class="mt-1 text-xs text-red-600">
										{{ identityErrors.nationalId }}
									</p>
									<p v-else class="mt-1 text-xs text-gray-500">{{ t('persons.form.nationalIdHint') }}</p>
								</div>

								<div>
									<label for="passportNumber" class="block text-sm font-medium text-gray-700">
										{{ t('persons.form.passportNumber') }}
									</label>
									<input id="passportNumber" v-model="formData.passportNumber" type="text"
										:placeholder="t('persons.form.passportPlaceholder')"
										maxlength="20"
										:class="[
											'mt-1 block w-full border rounded-md shadow-sm py-2 px-3 focus:outline-none sm:text-sm',
											identityErrors.passportNumber 
												? 'border-red-300 focus:ring-red-500 focus:border-red-500' 
												: 'border-gray-300 focus:ring-indigo-500 focus:border-indigo-500'
										]" />
									<p v-if="identityErrors.passportNumber" class="mt-1 text-xs text-red-600">
										{{ identityErrors.passportNumber }}
									</p>
									<p v-else class="mt-1 text-xs text-gray-500">{{ t('persons.form.passportHint') }}</p>
								</div>

								<div>
									<label for="residentCertificateNumber" class="block text-sm font-medium text-gray-700">
										{{ t('persons.form.residentCertificate') }}
									</label>
									<input id="residentCertificateNumber" v-model="formData.residentCertificateNumber" type="text"
										:placeholder="t('persons.form.residentCertPlaceholder')"
										maxlength="20"
										:class="[
											'mt-1 block w-full border rounded-md shadow-sm py-2 px-3 focus:outline-none sm:text-sm',
											identityErrors.residentCertificateNumber 
												? 'border-red-300 focus:ring-red-500 focus:border-red-500' 
												: 'border-gray-300 focus:ring-indigo-500 focus:border-indigo-500'
										]" />
									<p v-if="identityErrors.residentCertificateNumber" class="mt-1 text-xs text-red-600">
										{{ identityErrors.residentCertificateNumber }}
									</p>
									<p v-else class="mt-1 text-xs text-gray-500">{{ t('persons.form.residentCertHint') }}</p>
								</div>
							</div>

							<div v-if="isEdit && props.person.identityVerifiedAt" class="mt-4 bg-green-50 border-l-4 border-green-400 p-3">
								<div class="flex">
									<div class="flex-shrink-0">
										<svg class="h-5 w-5 text-green-400" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor">
											<path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd" />
										</svg>
									</div>
									<div class="ml-3">
										<p class="text-sm text-green-700">
											{{ t('persons.form.identityVerified') }}
											<span class="font-medium">{{ new Date(props.person.identityVerifiedAt).toLocaleDateString() }}</span>
										</p>
									</div>
								</div>
							</div>
						</div>
					</div>
				</div>
			</form>
		</template>

		<template #footer>
			<button type="submit" @click="handleSubmit" :disabled="saving"
				class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed">
				<svg v-if="saving" class="animate-spin -ml-1 mr-2 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg"
					fill="none" viewBox="0 0 24 24">
					<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
					<path class="opacity-75" fill="currentColor"
						d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z">
					</path>
				</svg>
				{{ saving ? t('persons.saving') : t('persons.save') }}
			</button>
			<button type="button" @click="handleClose" :disabled="saving"
				class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed">
				{{ t('persons.cancel') }}
			</button>
		</template>
	</BaseModal>
</template>
