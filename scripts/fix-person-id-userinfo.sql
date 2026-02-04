-- Fix: Add person_id to UserInfo endpoint
-- This script ensures person_id ClaimDefinition exists and is mapped to the profile scope

-- Step 1: Check if person_id ClaimDefinition exists
DECLARE @PersonIdClaimId INT;
SELECT @PersonIdClaimId = Id FROM ClaimDefinitions WHERE Name = 'person_id';

-- Step 2: Insert ClaimDefinition if not exists
IF @PersonIdClaimId IS NULL
BEGIN
    INSERT INTO ClaimDefinitions (Name, DisplayName, Description, ClaimType, UserPropertyPath, DataType, IsStandard, IsRequired)
    VALUES ('person_id', 'Person ID', 'Unique identifier linking user to Person entity', 'person_id', 'PersonId', 'String', 0, 0);
    
    SET @PersonIdClaimId = SCOPE_IDENTITY();
    PRINT 'Created person_id ClaimDefinition with Id: ' + CAST(@PersonIdClaimId AS NVARCHAR);
END
ELSE
BEGIN
    PRINT 'person_id ClaimDefinition already exists with Id: ' + CAST(@PersonIdClaimId AS NVARCHAR);
END

-- Step 3: Get profile scope Id
DECLARE @ProfileScopeId NVARCHAR(450);
SELECT @ProfileScopeId = Id FROM OpenIddictScopes WHERE Name = 'profile';

IF @ProfileScopeId IS NULL
BEGIN
    PRINT 'ERROR: profile scope not found!';
END
ELSE
BEGIN
    PRINT 'profile scope Id: ' + @ProfileScopeId;
    
    -- Step 4: Check if ScopeClaim mapping exists
    IF NOT EXISTS (SELECT 1 FROM ScopeClaims WHERE ScopeId = @ProfileScopeId AND ClaimDefinitionId = @PersonIdClaimId)
    BEGIN
        INSERT INTO ScopeClaims (ScopeId, ClaimDefinitionId, ScopeName, AlwaysInclude)
        VALUES (@ProfileScopeId, @PersonIdClaimId, 'profile', 0);
        
        PRINT 'Created ScopeClaim mapping: profile -> person_id';
    END
    ELSE
    BEGIN
        PRINT 'ScopeClaim mapping already exists';
    END
END

-- Verification query
SELECT 
    cd.Name AS ClaimName,
    cd.ClaimType,
    cd.UserPropertyPath,
    sc.ScopeName,
    sc.ScopeId
FROM ClaimDefinitions cd
LEFT JOIN ScopeClaims sc ON cd.Id = sc.ClaimDefinitionId
WHERE cd.Name = 'person_id';

PRINT '';
PRINT '=== Fix Complete ===';
PRINT 'person_id should now appear in UserInfo endpoint when profile scope is granted';
