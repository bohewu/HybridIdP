-- Delete all persons except the admin person
-- This script preserves the admin person and removes all others
-- PersonIdentities linking will be preserved for admin, others will be removed

-- First, remove PersonIdentities for non-admin persons
DELETE FROM "PersonIdentities" 
WHERE "PersonId" IN (
    SELECT "Id" FROM "Persons" 
    WHERE "Email" != 'admin@hybridauth.local'
);

-- Then, delete all persons except admin
DELETE FROM "Persons" 
WHERE "Email" != 'admin@hybridauth.local';
