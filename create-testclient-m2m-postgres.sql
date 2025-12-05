-- Create M2M TestClient for Client Credentials Flow (PostgreSQL)
-- This client is used for machine-to-machine authentication testing

-- Step 1: Ensure API scopes exist (not public scopes)
INSERT INTO "OpenIddictScopes" ("Id", "Name", "DisplayName", "Description", "ConcurrencyToken")
SELECT gen_random_uuid(), 'api:company:read', 'Read Company Data', 'Allows reading company information', gen_random_uuid()
WHERE NOT EXISTS (SELECT 1 FROM "OpenIddictScopes" WHERE "Name" = 'api:company:read');

INSERT INTO "OpenIddictScopes" ("Id", "Name", "DisplayName", "Description", "ConcurrencyToken")
SELECT gen_random_uuid(), 'api:company:write', 'Write Company Data', 'Allows creating and updating company information', gen_random_uuid()
WHERE NOT EXISTS (SELECT 1 FROM "OpenIddictScopes" WHERE "Name" = 'api:company:write');

INSERT INTO "OpenIddictScopes" ("Id", "Name", "DisplayName", "Description", "ConcurrencyToken")
SELECT gen_random_uuid(), 'api:inventory:read', 'Read Inventory Data', 'Allows reading inventory information', gen_random_uuid()
WHERE NOT EXISTS (SELECT 1 FROM "OpenIddictScopes" WHERE "Name" = 'api:inventory:read');

-- Step 2: Ensure scope extensions exist with IsPublic=false for API scopes
INSERT INTO "ScopeExtensions" ("ScopeId", "IsPublic", "IsRequired", "DisplayOrder")
SELECT s."Id", false, false, 0
FROM "OpenIddictScopes" s
WHERE s."Name" IN ('api:company:read', 'api:company:write', 'api:inventory:read')
AND NOT EXISTS (
    SELECT 1 FROM "ScopeExtensions" se WHERE se."ScopeId" = s."Id"
);

-- Step 3: Create M2M TestClient application (confidential client with secret)
-- Client Secret: "m2m-test-secret-2024" (will be hashed by OpenIddict automatically)
INSERT INTO "OpenIddictApplications" (
    "Id",
    "ClientId",
    "ClientSecret",
    "ClientType",
    "DisplayName",
    "ConsentType",
    "ConcurrencyToken",
    "Permissions"
)
SELECT 
    gen_random_uuid(),
    'testclient-m2m',
    'm2m-test-secret-2024',
    'confidential',
    'Test M2M Client (Confidential)',
    'implicit',
    gen_random_uuid(),
    '["ept:token","ept:introspection","ept:revocation","gt:client_credentials","scp:api:company:read","scp:api:company:write","scp:api:inventory:read"]'
WHERE NOT EXISTS (SELECT 1 FROM "OpenIddictApplications" WHERE "ClientId" = 'testclient-m2m');

-- Step 4: Verify scopes
SELECT "Name", "DisplayName" FROM "OpenIddictScopes" 
WHERE "Name" IN ('api:company:read', 'api:company:write', 'api:inventory:read')
ORDER BY "Name";

-- Step 5: Verify client
SELECT "ClientId", "DisplayName", "ClientType", "Permissions" 
FROM "OpenIddictApplications" 
WHERE "ClientId" = 'testclient-m2m';

-- Step 6: Verify scope extensions (API scopes should have IsPublic=false)
SELECT s."Name", se."IsPublic"
FROM "OpenIddictScopes" s
INNER JOIN "ScopeExtensions" se ON se."ScopeId" = s."Id"
WHERE s."Name" IN ('api:company:read', 'api:company:write', 'api:inventory:read');

