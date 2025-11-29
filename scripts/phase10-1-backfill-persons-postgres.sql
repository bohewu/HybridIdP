-- Phase 10.1: Data Migration - Create Person records for existing ApplicationUsers
-- This script creates a Person row for each existing ApplicationUser and links them together.
-- This is a one-time migration script that should be run after the Phase10_1_AddPersonEntity migration.

-- PostgreSQL Version

BEGIN;

DO $$
DECLARE
    v_users_without_person INT;
    v_total_active_users INT;
    v_persons_created INT;
BEGIN
    -- Create Person records from existing ApplicationUser data
    INSERT INTO "Persons" (
        "Id",
        "FirstName",
        "MiddleName",
        "LastName",
        "Nickname",
        "EmployeeId",
        "Department",
        "JobTitle",
        "ProfileUrl",
        "PictureUrl",
        "Website",
        "Address",
        "Birthdate",
        "Gender",
        "TimeZone",
        "Locale",
        "CreatedBy",
        "CreatedAt",
        "ModifiedBy",
        "ModifiedAt"
    )
    SELECT 
        gen_random_uuid() as "Id",  -- Generate new UUID for Person
        "FirstName",
        "MiddleName",
        "LastName",
        "Nickname",
        "EmployeeId",
        "Department",
        "JobTitle",
        "ProfileUrl",
        "PictureUrl",
        "Website",
        "Address",
        "Birthdate",
        "Gender",
        "TimeZone",
        "Locale",
        "CreatedBy",
        "CreatedAt",
        NULL as "ModifiedBy",  -- Not modified yet
        NULL as "ModifiedAt"
    FROM "AspNetUsers"
    WHERE "PersonId" IS NULL  -- Only process users without a Person link
    AND "IsDeleted" = false;  -- Skip deleted users

    -- Link ApplicationUsers to their newly created Person records
    -- Match based on EmployeeId (if unique) or create individual persons
    WITH PersonMapping AS (
        SELECT 
            u."Id" as "UserId",
            p."Id" as "PersonId",
            ROW_NUMBER() OVER (PARTITION BY u."Id" ORDER BY p."CreatedAt" DESC) as "RowNum"
        FROM "AspNetUsers" u
        LEFT JOIN "Persons" p ON 
            (u."EmployeeId" IS NOT NULL AND u."EmployeeId" = p."EmployeeId")
            OR (
                u."EmployeeId" IS NULL 
                AND u."FirstName" = p."FirstName" 
                AND u."LastName" = p."LastName"
                AND u."Birthdate" = p."Birthdate"
            )
        WHERE u."PersonId" IS NULL
        AND u."IsDeleted" = false
    )
    UPDATE "AspNetUsers" u
    SET "PersonId" = pm."PersonId"
    FROM PersonMapping pm
    WHERE u."Id" = pm."UserId"
    AND pm."RowNum" = 1;

    -- Verify the migration
    SELECT COUNT(*)
    INTO v_users_without_person
    FROM "AspNetUsers"
    WHERE "PersonId" IS NULL AND "IsDeleted" = false;

    SELECT COUNT(*)
    INTO v_total_active_users
    FROM "AspNetUsers"
    WHERE "IsDeleted" = false;

    SELECT COUNT(*)
    INTO v_persons_created
    FROM "Persons";

    RAISE NOTICE 'Migration Summary:';
    RAISE NOTICE '  Total active users: %', v_total_active_users;
    RAISE NOTICE '  Users with Person link: %', (v_total_active_users - v_users_without_person);
    RAISE NOTICE '  Users without Person link: %', v_users_without_person;
    RAISE NOTICE '  Total Persons created: %', v_persons_created;

    IF v_users_without_person > 0 THEN
        RAISE WARNING 'Some users still without Person link. Manual review required.';
        -- Don't fail the transaction, just warn
    END IF;

    RAISE NOTICE 'Migration completed successfully.';

EXCEPTION
    WHEN OTHERS THEN
        RAISE EXCEPTION 'Migration failed. Error: %', SQLERRM;
END $$;

COMMIT;
