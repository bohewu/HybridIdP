-- Create test user with consent to Demo Client 1
-- This script should be run after admin@hybridauth.local exists

-- 1. Create test user (if not exists)
DO $$
DECLARE
    test_user_id UUID;
    demo_client_id UUID;
    authorization_id UUID;
BEGIN
    -- Get or create test user
    SELECT "Id" INTO test_user_id 
    FROM "AspNetUsers" 
    WHERE "NormalizedEmail" = 'TESTUSER@HYBRIDAUTH.LOCAL';
    
    IF test_user_id IS NULL THEN
        test_user_id := gen_random_uuid();
        
        INSERT INTO "AspNetUsers" (
            "Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
            "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
            "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled",
            "AccessFailedCount", "FirstName", "LastName", "IsActive",
            "CreatedAt", "UpdatedAt"
        ) VALUES (
            test_user_id,
            'testuser@hybridauth.local',
            'TESTUSER@HYBRIDAUTH.LOCAL',
            'testuser@hybridauth.local',
            'TESTUSER@HYBRIDAUTH.LOCAL',
            TRUE,
            'AQAAAAIAAYagAAAAEHvM8B8qF/xX5vYZKGXQYxN5xQ7M3HwxK3qZ9Y6mVvC8N2pD4kR0jL7wE5tF6gH8sA==', -- Test@123
            'TESTSECU' || substr(md5(random()::text), 1, 28),
            gen_random_uuid(),
            FALSE,
            FALSE,
            TRUE,
            0,
            'Test',
            'User',
            TRUE,
            NOW(),
            NOW()
        );
        
        RAISE NOTICE 'Created test user: testuser@hybridauth.local (Password: Test@123)';
    ELSE
        RAISE NOTICE 'Test user already exists: testuser@hybridauth.local';
    END IF;

    -- Get Demo Client 1 ID
    SELECT "Id" INTO demo_client_id 
    FROM "OpenIddictApplications" 
    WHERE "ClientId" = 'demo-client-1';
    
    IF demo_client_id IS NULL THEN
        RAISE EXCEPTION 'Demo Client 1 not found. Please create it first.';
    END IF;

    -- Check if authorization already exists
    SELECT "Id" INTO authorization_id
    FROM "OpenIddictAuthorizations"
    WHERE "Subject" = test_user_id
    AND "ApplicationId" = demo_client_id;

    IF authorization_id IS NULL THEN
        -- Create permanent authorization (consent)
        INSERT INTO "OpenIddictAuthorizations" (
            "Id", "ApplicationId", "ConcurrencyToken", "CreationDate",
            "Scopes", "Status", "Subject", "Type"
        ) VALUES (
            gen_random_uuid(),
            demo_client_id,
            gen_random_uuid(),
            NOW(),
            '["openid","profile","email"]',
            'valid',
            test_user_id,
            'permanent'
        );
        
        RAISE NOTICE 'Created authorization for testuser with Demo Client 1';
    ELSE
        RAISE NOTICE 'Authorization already exists';
    END IF;

END $$;

-- Verify the setup
SELECT 
    'Test User' as description,
    u."Email",
    u."FirstName",
    u."LastName",
    u."IsActive"
FROM "AspNetUsers" u
WHERE u."NormalizedEmail" = 'TESTUSER@HYBRIDAUTH.LOCAL';

SELECT 
    'Authorized Apps' as description,
    a."ClientId",
    a."DisplayName",
    auth."Status",
    auth."Type",
    auth."Scopes"
FROM "OpenIddictAuthorizations" auth
JOIN "OpenIddictApplications" a ON a."Id" = auth."ApplicationId"::uuid
JOIN "AspNetUsers" u ON u."Id" = auth."Subject"::uuid
WHERE u."NormalizedEmail" = 'TESTUSER@HYBRIDAUTH.LOCAL';
