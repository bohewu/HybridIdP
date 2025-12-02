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
	// Phase 10.6: Identity fields
	nationalId: '',
	passportNumber: '',
	residentCertificateNumber: '',
	identityDocumentType: 'None'
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
		// Phase 10.6: Identity fields
		nationalId: props.person.nationalId || '',
		passportNumber: props.person.passportNumber || '',
		residentCertificateNumber: props.person.residentCertificateNumber || '',
		identityDocumentType: props.person.identityDocumentType || 'None'
	}
}

// Real-time validation watchers
watch(() => formData.value.nationalId, (newValue) => {
	if (newValue && formData.value.identityDocumentType === 'NationalId') {
		const result = validateTaiwanNationalId(newValue)
		identityErrors.value.nationalId = result.error
	} else {
		identityErrors.value.nationalId = null
	}
})

watch(() => formData.value.passportNumber, (newValue) => {
	if (newValue && formData.value.identityDocumentType === 'Passport') {
		const result = validatePassportNumber(newValue)
		identityErrors.value.passportNumber = result.error
	} else {
		identityErrors.value.passportNumber = null
	}
})

watch(() => formData.value.residentCertificateNumber, (newValue) => {
	if (newValue && formData.value.identityDocumentType === 'ResidentCertificate') {
		const result = validateResidentCertificate(newValue)
		identityErrors.value.residentCertificateNumber = result.error
	} else {
		identityErrors.value.residentCertificateNumber = null
	}
})

// Clear validation errors when document type changes
watch(() => formData.value.identityDocumentType, () => {
	identityErrors.value.nationalId = null
	identityErrors.value.passportNumber = null
	identityErrors.value.residentCertificateNumber = null
})

const handleSubmit = async () => {
	error.value = null

	// Basic validation
	if (!formData.value.firstName?.trim()) {
		error.value = t('admin.persons.errors.firstNameRequired')
		return
	}
	if (!formData.value.lastName?.trim()) {
		error.value = t('admin.persons.errors.lastNameRequired')
		return
	}

	// Identity document validation
	if (formData.value.identityDocumentType !== 'None') {
		if (formData.value.identityDocumentType === 'NationalId') {
			if (!formData.value.nationalId?.trim()) {
				error.value = t('admin.persons.validation.nationalIdRequired')
				return
			}
			const result = validateTaiwanNationalId(formData.value.nationalId)
			if (!result.valid) {
				error.value = result.error
				return
			}
		} else if (formData.value.identityDocumentType === 'Passport') {
			if (!formData.value.passportNumber?.trim()) {
				error.value = t('admin.persons.validation.passportRequired')
				return
			}
			const result = validatePassportNumber(formData.value.passportNumber)
			if (!result.valid) {
				error.value = result.error
				return
			}
		} else if (formData.value.identityDocumentType === 'ResidentCertificate') {
			if (!formData.value.residentCertificateNumber?.trim()) {
				error.value = t('admin.persons.validation.residentCertRequired')
				return
			}
			const result = validateResidentCertificate(formData.value.residentCertificateNumber)
			if (!result.valid) {
				error.value = result.error
				return
			}
		}
	}

	saving.value = true

	try {
		const url = isEdit.value
			? `/api/admin/people/${props.person.id}`
			: '/api/admin/people'

		const method = isEdit.value ? 'PUT' : 'POST'

		const response = await fetch(url, {
			method,
			headers: {
				'Content-Type': 'application/json'
			},
			body: JSON.stringify(formData.value)
		})

		if (!response.ok) {
			const errorData = await response.text()
			throw new Error(errorData || `HTTP error! status: ${response.status}`)
		}

		emit('save')
	} catch (e) {
		error.value = t('admin.persons.errors.saveFailed', { message: e.message })
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
	<BaseModal :show="true" :title="isEdit ? t('admin.persons.editPerson') : t('admin.persons.createPerson')" size="xl"
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
									{{ t('admin.persons.form.firstName') }} <span class="text-red-500">*</span>
								</label>
								<input id="firstName" v-model="formData.firstName" type="text" required
									:placeholder="t('admin.persons.form.firstName')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>

							<div>
								<label for="middleName" class="block text-sm font-medium text-gray-700">
									{{ t('admin.persons.form.middleName') }}
								</label>
								<input id="middleName" v-model="formData.middleName" type="text"
									:placeholder="t('admin.persons.form.middleName')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>

							<div>
								<label for="lastName" class="block text-sm font-medium text-gray-700">
									{{ t('admin.persons.form.lastName') }} <span class="text-red-500">*</span>
								</label>
								<input id="lastName" v-model="formData.lastName" type="text" required
									:placeholder="t('admin.persons.form.lastName')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>
						</div>

						<!-- Nickname -->
						<div>
							<label for="nickname" class="block text-sm font-medium text-gray-700">
								{{ t('admin.persons.form.nickname') }}
							</label>
							<input id="nickname" v-model="formData.nickname" type="text"
								:placeholder="t('admin.persons.form.nickname')"
								class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
						</div>

						<!-- Employment Info -->
						<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
							<div>
								<label for="employeeId" class="block text-sm font-medium text-gray-700">
									{{ t('admin.persons.form.employeeId') }}
								</label>
								<input id="employeeId" v-model="formData.employeeId" type="text"
									:placeholder="t('admin.persons.form.employeeId')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>

							<div>
								<label for="department" class="block text-sm font-medium text-gray-700">
									{{ t('admin.persons.form.department') }}
								</label>
								<input id="department" v-model="formData.department" type="text"
									:placeholder="t('admin.persons.form.department')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>

							<div>
								<label for="jobTitle" class="block text-sm font-medium text-gray-700">
									{{ t('admin.persons.form.jobTitle') }}
								</label>
								<input id="jobTitle" v-model="formData.jobTitle" type="text"
									:placeholder="t('admin.persons.form.jobTitle')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
							</div>
						</div>

						<!-- Personal Info (Optional) -->
						<div class="border-t pt-4">
							<h4 class="text-sm font-medium text-gray-700 mb-3">{{ t('admin.persons.form.personalInfo')
								}}</h4>
							<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
								<div>
									<label for="gender" class="block text-sm font-medium text-gray-700">
										{{ t('admin.persons.form.gender') }}
									</label>
									<input id="gender" v-model="formData.gender" type="text"
										:placeholder="t('admin.persons.form.gender')"
										class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
								</div>

								<div>
									<label for="birthdate" class="block text-sm font-medium text-gray-700">
										{{ t('admin.persons.form.birthdate') }}
									</label>
									<input id="birthdate" v-model="formData.birthdate" type="date"
										class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
								</div>
							</div>

							<div class="mt-4">
								<label for="address" class="block text-sm font-medium text-gray-700">
									{{ t('admin.persons.form.address') }}
								</label>
								<textarea id="address" v-model="formData.address" rows="2"
									:placeholder="t('admin.persons.form.address')"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"></textarea>
							</div>

							<div class="grid grid-cols-1 gap-4 sm:grid-cols-2 mt-4">
								<div>
									<label for="timeZone" class="block text-sm font-medium text-gray-700">
										{{ t('admin.persons.form.timeZone') }}
									</label>
									<input id="timeZone" v-model="formData.timeZone" type="text"
										:placeholder="t('admin.persons.form.timeZone')"
										class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
								</div>

								<div>
									<label for="locale" class="block text-sm font-medium text-gray-700">
										{{ t('admin.persons.form.locale') }}
									</label>
									<input id="locale" v-model="formData.locale" type="text"
										:placeholder="t('admin.persons.form.locale')"
										class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm" />
								</div>
							</div>
						</div>

						<!-- Phase 10.6: Identity Verification -->
						<div class="border-t pt-4">
							<h4 class="text-sm font-medium text-gray-700 mb-3">{{ t('admin.persons.form.identityVerification')
								}}</h4>
							
							<div class="mb-4">
								<label for="identityDocumentType" class="block text-sm font-medium text-gray-700">
									{{ t('admin.persons.form.identityDocumentType') }}
								</label>
								<select id="identityDocumentType" v-model="formData.identityDocumentType"
									class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm">
									<option value="None">{{ t('admin.persons.form.documentTypes.none') }}</option>
									<option value="NationalId">{{ t('admin.persons.form.documentTypes.nationalId') }}</option>
									<option value="Passport">{{ t('admin.persons.form.documentTypes.passport') }}</option>
									<option value="ResidentCertificate">{{ t('admin.persons.form.documentTypes.residentCertificate') }}</option>
								</select>
							</div>

							<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
								<div>
									<label for="nationalId" class="block text-sm font-medium text-gray-700">
										{{ t('admin.persons.form.nationalId') }}
									</label>
									<input id="nationalId" v-model="formData.nationalId" type="text"
										:placeholder="t('admin.persons.form.nationalIdPlaceholder')"
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
									<p v-else class="mt-1 text-xs text-gray-500">{{ t('admin.persons.form.nationalIdHint') }}</p>
								</div>

								<div>
									<label for="passportNumber" class="block text-sm font-medium text-gray-700">
										{{ t('admin.persons.form.passportNumber') }}
									</label>
									<input id="passportNumber" v-model="formData.passportNumber" type="text"
										:placeholder="t('admin.persons.form.passportPlaceholder')"
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
									<p v-else class="mt-1 text-xs text-gray-500">{{ t('admin.persons.form.passportHint') }}</p>
								</div>

								<div>
									<label for="residentCertificateNumber" class="block text-sm font-medium text-gray-700">
										{{ t('admin.persons.form.residentCertificate') }}
									</label>
									<input id="residentCertificateNumber" v-model="formData.residentCertificateNumber" type="text"
										:placeholder="t('admin.persons.form.residentCertPlaceholder')"
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
									<p v-else class="mt-1 text-xs text-gray-500">{{ t('admin.persons.form.residentCertHint') }}</p>
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
											{{ t('admin.persons.form.identityVerified') }}
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
				{{ saving ? t('admin.persons.saving') : t('admin.persons.save') }}
			</button>
			<button type="button" @click="handleClose" :disabled="saving"
				class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed">
				{{ t('admin.persons.cancel') }}
			</button>
		</template>
	</BaseModal>
</template>
