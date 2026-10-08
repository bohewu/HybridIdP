<script setup>
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps({
  modelValue: { type: Object, required: true },
  properties: { type: Object, default: () => ({}) },
  depth: { type: Number, default: 1 },
  totalNodes: { type: Number, default: 1 }
})
const emit = defineEmits(['update:modelValue'])
const { t } = useI18n()
const isGroup = computed(() => ['All', 'Any'].includes(props.modelValue.operator))
const propertyType = computed(() => props.properties[props.modelValue.property])
const leafOperators = computed(() => propertyType.value === 'StringArray' ? ['Contains']
  : propertyType.value === 'Boolean' ? ['Equals', 'NotEquals'] : ['Equals', 'NotEquals', 'StartsWith', 'Contains'])
const operators = computed(() => [...leafOperators.value,
  ...(props.depth < 4 && (isGroup.value || props.totalNodes < 32) ? ['All', 'Any'] : [])])
const leaf = () => ({ operator: 'Equals', property: '', value: '' })
const update = changes => emit('update:modelValue', { ...props.modelValue, ...changes })
function setOperator(operator) {
  if (['All', 'Any'].includes(operator)) {
    emit('update:modelValue', { operator, children: isGroup.value ? props.modelValue.children : [props.modelValue] })
  } else if (isGroup.value) emit('update:modelValue', { ...leaf(), operator })
  else update({ operator })
}
function setProperty(property) {
  const type = props.properties[property]
  const operator = type === 'StringArray' ? 'Contains'
    : type === 'Boolean' && !['Equals', 'NotEquals'].includes(props.modelValue.operator) ? 'Equals' : props.modelValue.operator
  update({ property, operator, value: type === 'Boolean' ? false : '' })
}
function setChild(index, child) {
  const children = [...props.modelValue.children]
  children[index] = child
  update({ children })
}
function addChild(group) {
  update({ children: [...props.modelValue.children, group ? { operator: 'All', children: [leaf()] } : leaf()] })
}
</script>

<template>
  <fieldset class="rounded-md border border-gray-200 bg-gray-50 p-3 space-y-3" data-test-id="condition-node">
    <label class="block text-sm text-gray-700">
      {{ t('claims.form.ruleOperator') }}
      <select :value="modelValue.operator" @change="setOperator($event.target.value)"
        class="block w-full rounded-md border-gray-300 h-10 px-3 mt-1 bg-white" data-test-id="condition-operator">
        <option v-if="!operators.includes(modelValue.operator)" :value="modelValue.operator" disabled>{{ modelValue.operator }}</option>
        <option v-for="operator in operators" :key="operator" :value="operator">{{ t(`claims.form.operators.${operator}`) }}</option>
      </select>
    </label>
    <template v-if="isGroup">
      <div v-for="(child, index) in modelValue.children" :key="index" class="pl-3 border-l-2 border-gray-300 space-y-2">
        <ClaimConditionEditor :model-value="child" :properties="properties" :depth="depth + 1" :total-nodes="totalNodes"
          @update:model-value="setChild(index, $event)" />
        <button type="button" :disabled="modelValue.children.length <= 1"
          @click="update({ children: modelValue.children.filter((_, i) => i !== index) })"
          class="text-sm text-red-700 disabled:opacity-40" data-test-id="condition-remove">{{ t('claims.form.removeCondition') }}</button>
      </div>
      <div class="flex flex-wrap gap-3">
        <button type="button" :disabled="modelValue.children.length >= 8 || totalNodes >= 32 || depth >= 4" @click="addChild(false)"
          class="text-sm text-google-600 disabled:opacity-40" data-test-id="condition-add">{{ t('claims.form.addCondition') }}</button>
        <button type="button" :disabled="modelValue.children.length >= 8 || totalNodes > 30 || depth >= 3" @click="addChild(true)"
          class="text-sm text-google-600 disabled:opacity-40" data-test-id="condition-add-group">{{ t('claims.form.addGroup') }}</button>
      </div>
    </template>
    <template v-else>
      <label class="block text-sm text-gray-700">{{ t('claims.form.ruleProperty') }}
        <select :value="modelValue.property" @change="setProperty($event.target.value)" required
          class="block w-full rounded-md border-gray-300 h-10 px-3 mt-1 bg-white" data-test-id="condition-property">
          <option value="" disabled>{{ t('claims.form.selectPropertyPath') }}</option>
          <option v-if="modelValue.property && !propertyType" :value="modelValue.property" disabled>{{ modelValue.property }} ({{ t('claims.form.unavailableProperty') }})</option>
          <option v-for="(type, key) in properties" :key="key" :value="key">{{ key }} ({{ type }})</option>
        </select>
      </label>
      <label class="block text-sm text-gray-700">{{ t('claims.form.ruleValue') }}
        <select v-if="propertyType === 'Boolean'" :value="modelValue.value" @change="update({ value: $event.target.value === 'true' })"
          class="block w-full rounded-md border-gray-300 h-10 px-3 mt-1 bg-white" data-test-id="condition-value">
          <option :value="true">true</option><option :value="false">false</option>
        </select>
        <input v-else :value="modelValue.value" @input="update({ value: $event.target.value })" type="text" maxlength="1024"
          :required="['StartsWith', 'Contains'].includes(modelValue.operator) && propertyType !== 'StringArray'"
          class="block w-full rounded-md border-gray-300 h-10 px-3 mt-1 bg-white" data-test-id="condition-value" />
      </label>
    </template>
  </fieldset>
</template>
