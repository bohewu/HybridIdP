-- Setup Test API Resources and Scopes for JWT aud claim testing
-- This script creates API resources and associates them with scopes

-- Step 1: Create API scopes in OpenIddict
DO $$
DECLARE
    company_scope_id uuid;
    inventory_scope_id uuid;
BEGIN
    -- Create company API scopes
    INSERT INTO "OpenIddictScopes" (
        "Id",
        "Name",
        "DisplayName",
        "Description",
        "ConcurrencyToken"
    ) VALUES (
        gen_random_uuid(),
        'api:company:read',
        'Read Company Data',
        'Allows reading company information',
        gen_random_uuid()
    )
    ON CONFLICT ("Name") DO UPDATE SET "DisplayName" = EXCLUDED."DisplayName"
    RETURNING "Id" INTO company_scope_id;

    INSERT INTO "OpenIddictScopes" (
        "Id",
        "Name",
        "DisplayName",
        "Description",
        "ConcurrencyToken"
    ) VALUES (
        gen_random_uuid(),
        'api:company:write',
        'Write Company Data',
        'Allows creating and updating company information',
        gen_random_uuid()
    )
    ON CONFLICT ("Name") DO UPDATE SET "DisplayName" = EXCLUDED."DisplayName";

    -- Create inventory API scope
    INSERT INTO "OpenIddictScopes" (
        "Id",
        "Name",
        "DisplayName",
        "Description",
        "ConcurrencyToken"
    ) VALUES (
        gen_random_uuid(),
        'api:inventory:read',
        'Read Inventory Data',
        'Allows reading inventory information',
        gen_random_uuid()
    )
    ON CONFLICT ("Name") DO UPDATE SET "DisplayName" = EXCLUDED."DisplayName"
    RETURNING "Id" INTO inventory_scope_id;

    RAISE NOTICE 'Created/Updated scopes';
END $$;

-- Step 2: Create API Resources
INSERT INTO "ApiResources" (
    "Name",
    "DisplayName",
    "Description",
    "BaseUrl",
    "CreatedAt"
) VALUES (
    'company_api',
    'Company API',
    'Company management and data API',
    'https://api.company.com',
    NOW()
)
ON CONFLICT ("Name") DO UPDATE 
SET "DisplayName" = EXCLUDED."DisplayName",
    "Description" = EXCLUDED."Description",
    "UpdatedAt" = NOW();

INSERT INTO "ApiResources" (
    "Name",
    "DisplayName",
    "Description",
    "BaseUrl",
    "CreatedAt"
) VALUES (
    'inventory_api',
    'Inventory API',
    'Inventory management and tracking API',
    'https://api.inventory.com',
    NOW()
)
ON CONFLICT ("Name") DO UPDATE 
SET "DisplayName" = EXCLUDED."DisplayName",
    "Description" = EXCLUDED."Description",
    "UpdatedAt" = NOW();

-- Step 3: Associate scopes with API resources
DO $$
DECLARE
    company_api_id int;
    inventory_api_id int;
    company_read_scope_id text;
    company_write_scope_id text;
    inventory_read_scope_id text;
BEGIN
    -- Get API Resource IDs
    SELECT "Id" INTO company_api_id FROM "ApiResources" WHERE "Name" = 'company_api';
    SELECT "Id" INTO inventory_api_id FROM "ApiResources" WHERE "Name" = 'inventory_api';

    -- Get Scope IDs
    SELECT "Id" INTO company_read_scope_id FROM "OpenIddictScopes" WHERE "Name" = 'api:company:read';
    SELECT "Id" INTO company_write_scope_id FROM "OpenIddictScopes" WHERE "Name" = 'api:company:write';
    SELECT "Id" INTO inventory_read_scope_id FROM "OpenIddictScopes" WHERE "Name" = 'api:inventory:read';

    -- Associate company scopes with company API
    INSERT INTO "ApiResourceScopes" ("ApiResourceId", "ScopeId")
    VALUES (company_api_id, company_read_scope_id)
    ON CONFLICT DO NOTHING;

    INSERT INTO "ApiResourceScopes" ("ApiResourceId", "ScopeId")
    VALUES (company_api_id, company_write_scope_id)
    ON CONFLICT DO NOTHING;

    -- Associate inventory scope with inventory API
    INSERT INTO "ApiResourceScopes" ("ApiResourceId", "ScopeId")
    VALUES (inventory_api_id, inventory_read_scope_id)
    ON CONFLICT DO NOTHING;

    RAISE NOTICE 'Associated scopes with API resources';
END $$;

-- Step 4: Update test client to include the new API scopes
UPDATE "OpenIddictApplications"
SET "Permissions" = jsonb_set(
    "Permissions"::jsonb,
    '{0}',
    ("Permissions"::jsonb || '["scp:api:company:read","scp:api:company:write","scp:api:inventory:read"]'::jsonb)
)
WHERE "ClientId" = 'testclient-public';

-- Verification queries
SELECT 'API Resources Created:' as info;
SELECT "Id", "Name", "DisplayName", "BaseUrl" FROM "ApiResources";

SELECT 'API Scopes Created:' as info;
SELECT "Id", "Name", "DisplayName", "Description" FROM "OpenIddictScopes" WHERE "Name" LIKE 'api:%';

SELECT 'API Resource Scope Associations:' as info;
SELECT 
    ar."Name" as "ApiResourceName",
    os."Name" as "ScopeName",
    os."DisplayName" as "ScopeDisplayName"
FROM "ApiResourceScopes" ars
JOIN "ApiResources" ar ON ars."ApiResourceId" = ar."Id"
JOIN "OpenIddictScopes" os ON ars."ScopeId" = os."Id"::text
ORDER BY ar."Name", os."Name";

SELECT 'Test Client Permissions:' as info;
SELECT "ClientId", "Permissions" FROM "OpenIddictApplications" WHERE "ClientId" = 'testclient-public';
