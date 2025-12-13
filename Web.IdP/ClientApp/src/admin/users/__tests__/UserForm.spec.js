import { mount } from '@vue/test-utils'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import UserForm from '../components/UserForm.vue'

// Mock components
const BaseModal = {
    template: '<div><slot name="body"></slot><slot name="footer"></slot></div>',
    props: ['show', 'title', 'loading']
}

const PasswordPolicyInput = {
    template: '<div></div>',
    props: ['policy', 'isEditMode'],
    expose: ['validate'],
    setup() {
        return {
            validate: vi.fn(() => true)
        }
    }
}

// Mock vue-i18n
vi.mock('vue-i18n', () => ({
    useI18n: () => ({
        t: (key) => key
    })
}))

describe('UserForm.vue', () => {
    let wrapper
    const policy = {
        minPasswordLength: 8,
        requireUppercase: true
    }

    const globalMocks = {
        $t: (key) => key
    }

    beforeEach(() => {
        global.fetch = vi.fn(() => Promise.resolve({
            ok: true,
            json: () => Promise.resolve({})
        }))
    })

    it('renders correctly in create mode', () => {
        wrapper = mount(UserForm, {
            global: {
                stubs: { BaseModal, PasswordPolicyInput },
                mocks: globalMocks
            },
            props: {
                policy: policy,
                user: null
            }
        })

        expect(wrapper.find('input#email').exists()).toBe(true)
        expect(wrapper.find('input#userName').exists()).toBe(true)
        expect(wrapper.find('button[type="submit"]').text()).toBe('users.createUser')
    })

    it('renders correctly in edit mode', async () => {
        const user = {
            id: '123',
            email: 'test@example.com',
            userName: 'testuser',
            firstName: 'Test',
            lastName: 'User'
        }

        wrapper = mount(UserForm, {
            global: {
                stubs: { BaseModal, PasswordPolicyInput },
                mocks: globalMocks
            },
            props: {
                policy: policy,
                user: user
            }
        })

        await wrapper.vm.$nextTick()

        // Debugging assistance if needed
        // console.log(wrapper.find('input#email').element.value)

        expect(wrapper.find('input#email').element.value).toBe(user.email)
        expect(wrapper.find('input#email').attributes('disabled')).toBe('')
        expect(wrapper.find('button[type="submit"]').text()).toBe('users.updateUser')
    })

    it('validates required fields', async () => {
        wrapper = mount(UserForm, {
            global: {
                stubs: { BaseModal, PasswordPolicyInput },
                mocks: globalMocks
            },
            props: { policy, user: null }
        })

        await wrapper.find('form').trigger('submit.prevent')

        expect(wrapper.find('input#email').classes()).toContain('border-red-500')
        expect(wrapper.find('input#userName').classes()).toContain('border-red-500')
        expect(global.fetch).not.toHaveBeenCalled()
    })

    it('submits valid form data', async () => {
        wrapper = mount(UserForm, {
            global: {
                stubs: { BaseModal, PasswordPolicyInput },
                mocks: globalMocks
            },
            props: { policy, user: null }
        })

        await wrapper.find('input#email').setValue('new@example.com')
        await wrapper.find('input#userName').setValue('newuser')

        // Simulate child component interaction
        const passwordComp = wrapper.findComponent(PasswordPolicyInput)
        passwordComp.vm.$emit('update:password', 'Valid123!')

        await wrapper.find('form').trigger('submit.prevent')

        expect(global.fetch).toHaveBeenCalledWith('/api/admin/users', expect.objectContaining({
            method: 'POST',
            body: expect.stringContaining('"email":"new@example.com"')
        }))
        expect(wrapper.emitted('save')).toBeTruthy()
    })
})
