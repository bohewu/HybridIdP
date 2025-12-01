-- Phase 10.6.2: Data Migration - Set Admin Person Identity
-- Purpose: Update existing admin Person records with default NationalId
-- Database: PostgreSQL
-- Date: 2025-12-01

-- Update admin Person records with default National ID
-- Assumes admin user email is 'admin@hybridauth.local'
UPDATE "Persons" p
SET 
    "NationalId" = 'A123456789',
    "IdentityDocumentType" = 'NationalId',
    "IdentityVerifiedAt" = NOW() AT TIME ZONE 'UTC',
    "IdentityVerifiedBy" = u."Id",
    "ModifiedAt" = NOW() AT TIME ZONE 'UTC',
    "ModifiedBy" = u."Id"
FROM "AspNetUsers" u
WHERE p."Id" = u."PersonId"
  AND u."Email" = 'admin@hybridauth.local'
  AND p."NationalId" IS NULL;

-- Verify the update
SELECT 
    p."Id" AS "PersonId",
    p."FirstName",
    p."LastName",
    p."NationalId",
    p."IdentityDocumentType",
    p."IdentityVerifiedAt",
    u."Email" AS "AdminEmail"
FROM "Persons" p
INNER JOIN "AspNetUsers" u ON p."Id" = u."PersonId"
WHERE u."Email" = 'admin@hybridauth.local';
