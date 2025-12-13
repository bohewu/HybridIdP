import { APIRequestContext, APIResponse } from '@playwright/test';

const BASE_URL = 'https://localhost:7035';

/**
 * Generic API client wrapper for Playwright's APIRequestContext.
 * Provides type-safe HTTP methods for Admin API operations.
 */
export class ApiClient {
    constructor(protected request: APIRequestContext) { }

    protected async handleResponse<T>(response: APIResponse): Promise<T> {
        if (!response.ok()) {
            const body = await response.text().catch(() => 'No response body');
            throw new Error(`API error ${response.status()}: ${body}`);
        }
        // Handle empty responses (e.g., 204 No Content)
        const text = await response.text();
        if (!text) return undefined as T;
        return JSON.parse(text) as T;
    }

    async get<T>(path: string): Promise<T> {
        const response = await this.request.get(`${BASE_URL}${path}`);
        return this.handleResponse<T>(response);
    }

    async post<T>(path: string, data?: any): Promise<T> {
        const response = await this.request.post(`${BASE_URL}${path}`, {
            data,
            headers: { 'Content-Type': 'application/json' },
        });
        return this.handleResponse<T>(response);
    }

    async put<T>(path: string, data?: any): Promise<T> {
        const response = await this.request.put(`${BASE_URL}${path}`, {
            data,
            headers: { 'Content-Type': 'application/json' },
        });
        return this.handleResponse<T>(response);
    }

    async delete(path: string): Promise<void> {
        const response = await this.request.delete(`${BASE_URL}${path}`);
        if (!response.ok() && response.status() !== 404) {
            const body = await response.text().catch(() => 'No response body');
            throw new Error(`Delete failed ${response.status()}: ${body}`);
        }
    }
}

// ============================================================================
// Domain-specific API clients (to be expanded in Phase 19.2+)
// ============================================================================

export interface UserCreateRequest {
    email: string;
    userName: string;
    password: string;
    firstName: string;
    lastName: string;
}

export interface UserResponse {
    id: string;
    email: string;
    userName: string;
    firstName: string;
    lastName: string;
    isActive: boolean;
    roles?: string[];
}

export interface PaginatedResponse<T> {
    items: T[];
    total: number;
}

/**
 * Users API client for Admin operations.
 */
export class UsersApi extends ApiClient {
    async list(search?: string, take = 20): Promise<PaginatedResponse<UserResponse>> {
        const params = new URLSearchParams();
        if (search) params.set('search', search);
        params.set('take', String(take));
        return this.get(`/api/admin/users?${params}`);
    }

    async create(data: UserCreateRequest): Promise<UserResponse> {
        return this.post('/api/admin/users', data);
    }

    async getById(id: string): Promise<UserResponse> {
        return this.get(`/api/admin/users/${id}`);
    }

    async update(id: string, data: Partial<UserCreateRequest>): Promise<UserResponse> {
        return this.put(`/api/admin/users/${id}`, data);
    }

    async deleteUser(id: string): Promise<void> {
        return this.delete(`/api/admin/users/${id}`);
    }

    async assignRoles(userId: string, roleIds: string[]): Promise<void> {
        await this.put(`/api/admin/users/${userId}/roles/ids`, { RoleIds: roleIds });
    }

    async findByEmail(email: string): Promise<UserResponse | null> {
        const result = await this.list(email);
        return result.items.find(u => u.email === email) || null;
    }
}

export interface RoleResponse {
    id: string;
    name: string;
    description: string;
}

/**
 * Roles API client.
 */
export class RolesApi extends ApiClient {
    async list(search?: string): Promise<PaginatedResponse<RoleResponse>> {
        const params = search ? `?search=${encodeURIComponent(search)}` : '';
        return this.get(`/api/admin/roles${params}`);
    }

    async create(name: string, description: string, permissions: string[] = []): Promise<RoleResponse> {
        return this.post('/api/admin/roles', { name, description, permissions });
    }

    async deleteRole(id: string): Promise<void> {
        return this.delete(`/api/admin/roles/${id}`);
    }
}

export interface PersonResponse {
    id: string;
    firstName: string;
    lastName: string;
    employeeId?: string;
    department?: string;
    jobTitle?: string;
}

/**
 * People API client.
 */
export class PeopleApi extends ApiClient {
    async list(search?: string): Promise<PaginatedResponse<PersonResponse>> {
        const params = search ? `?search=${encodeURIComponent(search)}` : '';
        return this.get(`/api/admin/people${params}`);
    }

    async create(data: Partial<PersonResponse>): Promise<PersonResponse> {
        return this.post('/api/admin/people', data);
    }

    async deletePerson(id: string): Promise<void> {
        return this.delete(`/api/admin/people/${id}`);
    }

    async linkAccount(personId: string, userId: string): Promise<void> {
        await this.post(`/api/admin/people/${personId}/accounts`, { userId });
    }

    async unlinkAccount(userId: string): Promise<void> {
        await this.delete(`/api/admin/people/accounts/${userId}`);
    }
}

export interface ClientResponse {
    id: string;
    clientId: string;
    displayName: string;
    type: string;
}

/**
 * Clients API client.
 */
export class ClientsApi extends ApiClient {
    async list(search?: string): Promise<PaginatedResponse<ClientResponse>> {
        const params = search ? `?search=${encodeURIComponent(search)}` : '';
        return this.get(`/api/admin/clients${params}`);
    }

    async create(data: any): Promise<ClientResponse> {
        return this.post('/api/admin/clients', data);
    }

    async deleteClient(id: string): Promise<void> {
        return this.delete(`/api/admin/clients/${id}`);
    }

    async findByClientId(clientId: string): Promise<ClientResponse | null> {
        const result = await this.list(clientId);
        return result.items.find(c => c.clientId === clientId) || null;
    }
}

/**
 * Composite API facade providing access to all domain APIs.
 */
export class AdminApi {
    public readonly users: UsersApi;
    public readonly roles: RolesApi;
    public readonly people: PeopleApi;
    public readonly clients: ClientsApi;

    constructor(request: APIRequestContext) {
        this.users = new UsersApi(request);
        this.roles = new RolesApi(request);
        this.people = new PeopleApi(request);
        this.clients = new ClientsApi(request);
    }
}
