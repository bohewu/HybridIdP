-- Create TestClient Device OAuth Application (Postgres)

INSERT INTO "OpenIddictApplications" (
    "Id",
    "ClientId",
    "ClientType",
    "DisplayName",
    "ConsentType",
    "ConcurrencyToken",
    "Permissions"
)
SELECT 
    gen_random_uuid(),
    'testclient-device',
    'public',
    'Test Client (Device)',
    'explicit',
    gen_random_uuid(),
    '["ept:device","ept:token","gt:device_code","gt:refresh_token","scp:openid","scp:profile","scp:email"]'
WHERE NOT EXISTS (SELECT 1 FROM "OpenIddictApplications" WHERE "ClientId" = 'testclient-device');

SELECT "ClientId", "Permissions" FROM "OpenIddictApplications" WHERE "ClientId" = 'testclient-device';
