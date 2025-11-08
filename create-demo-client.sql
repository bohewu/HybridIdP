-- Create Demo Client 1 for testing
INSERT INTO "OpenIddictApplications" (
    "Id",
    "ClientId",
    "ClientType",
    "DisplayName",
    "ConsentType",
    "ConcurrencyToken",
    "RedirectUris",
    "Permissions"
) VALUES (
    gen_random_uuid(),
    'demo-client-1',
    'public',
    'Demo Client 1',
    'explicit',
    gen_random_uuid(),
    '["https://localhost:7001/signin-oidc"]',
    '["ept:authorization","ept:token","gt:authorization_code","gt:refresh_token","scp:openid","scp:profile","scp:email"]'
)
ON CONFLICT DO NOTHING;

-- Verify
SELECT "ClientId", "DisplayName", "ClientType", "RedirectUris" 
FROM "OpenIddictApplications" 
WHERE "ClientId" = 'demo-client-1';
