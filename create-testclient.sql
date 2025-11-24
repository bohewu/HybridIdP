-- Create TestClient OAuth Application for E2E Testing
-- This client is used by the TestClient application (https://localhost:7001)

INSERT INTO "OpenIddictApplications" (
    "Id",
    "ClientId",
    "ClientType",
    "DisplayName",
    "ConsentType",
    "ConcurrencyToken",
    "RedirectUris",
    "PostLogoutRedirectUris",
    "Permissions"
) VALUES (
    gen_random_uuid(),
    'testclient-public',
    'public',
    'Test Client (Public)',
    'explicit',
    gen_random_uuid(),
    '["https://localhost:7001/signin-oidc"]',
    '["https://localhost:7001/signout-callback-oidc"]',
    '["ept:authorization","ept:token","ept:logout","gt:authorization_code","gt:refresh_token","scp:openid","scp:profile","scp:email","scp:roles","scp:api:company:read","scp:api:inventory:read"]'
)
ON CONFLICT DO NOTHING;

-- Verify
SELECT "ClientId", "DisplayName", "ClientType", "RedirectUris", "Permissions" 
FROM "OpenIddictApplications" 
WHERE "ClientId" = 'testclient-public';
