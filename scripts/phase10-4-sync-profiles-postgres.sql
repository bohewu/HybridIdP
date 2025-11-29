-- Phase 10.4: Sync ApplicationUser profile data to linked Person records (PostgreSQL)
-- This script copies profile fields from ApplicationUser to their linked Person entity
-- Run after Phase 10.1 backfill (ensures all users have PersonId populated)

DO $$
DECLARE
    updated_count INT := 0;
    skipped_count INT := 0;
    sample_record RECORD;
BEGIN
    RAISE NOTICE 'Starting Phase 10.4 profile synchronization...';
    RAISE NOTICE '';

    -- Update Person records with ApplicationUser profile data
    -- Only updates where ApplicationUser has non-null values
    WITH updated AS (
        UPDATE "Persons" p
        SET 
            "FirstName" = COALESCE(u."FirstName", p."FirstName"),
            "MiddleName" = COALESCE(u."MiddleName", p."MiddleName"),
            "LastName" = COALESCE(u."LastName", p."LastName"),
            "Nickname" = COALESCE(u."Nickname", p."Nickname"),
            "EmployeeId" = COALESCE(u."EmployeeId", p."EmployeeId"),
            "Department" = COALESCE(u."Department", p."Department"),
            "JobTitle" = COALESCE(u."JobTitle", p."JobTitle"),
            "ProfileUrl" = COALESCE(u."ProfileUrl", p."ProfileUrl"),
            "PictureUrl" = COALESCE(u."PictureUrl", p."PictureUrl"),
            "Website" = COALESCE(u."Website", p."Website"),
            "Address" = COALESCE(u."Address", p."Address"),
            "Birthdate" = COALESCE(u."Birthdate", p."Birthdate"),
            "Gender" = COALESCE(u."Gender", p."Gender"),
            "TimeZone" = COALESCE(u."TimeZone", p."TimeZone"),
            "Locale" = COALESCE(u."Locale", p."Locale"),
            "ModifiedAt" = NOW() AT TIME ZONE 'UTC',
            "ModifiedBy" = NULL  -- System migration, no specific user
        FROM "AspNetUsers" u
        WHERE u."PersonId" = p."Id"
            AND (
                -- Only update if ApplicationUser has different or non-null values
                (u."FirstName" IS NOT NULL AND (p."FirstName" IS NULL OR p."FirstName" != u."FirstName"))
                OR (u."MiddleName" IS NOT NULL AND (p."MiddleName" IS NULL OR p."MiddleName" != u."MiddleName"))
                OR (u."LastName" IS NOT NULL AND (p."LastName" IS NULL OR p."LastName" != u."LastName"))
                OR (u."Nickname" IS NOT NULL AND (p."Nickname" IS NULL OR p."Nickname" != u."Nickname"))
                OR (u."EmployeeId" IS NOT NULL AND (p."EmployeeId" IS NULL OR p."EmployeeId" != u."EmployeeId"))
                OR (u."Department" IS NOT NULL AND (p."Department" IS NULL OR p."Department" != u."Department"))
                OR (u."JobTitle" IS NOT NULL AND (p."JobTitle" IS NULL OR p."JobTitle" != u."JobTitle"))
                OR (u."ProfileUrl" IS NOT NULL AND (p."ProfileUrl" IS NULL OR p."ProfileUrl" != u."ProfileUrl"))
                OR (u."PictureUrl" IS NOT NULL AND (p."PictureUrl" IS NULL OR p."PictureUrl" != u."PictureUrl"))
                OR (u."Website" IS NOT NULL AND (p."Website" IS NULL OR p."Website" != u."Website"))
                OR (u."Address" IS NOT NULL AND (p."Address" IS NULL OR p."Address" != u."Address"))
                OR (u."Birthdate" IS NOT NULL AND (p."Birthdate" IS NULL OR p."Birthdate" != u."Birthdate"))
                OR (u."Gender" IS NOT NULL AND (p."Gender" IS NULL OR p."Gender" != u."Gender"))
                OR (u."TimeZone" IS NOT NULL AND (p."TimeZone" IS NULL OR p."TimeZone" != u."TimeZone"))
                OR (u."Locale" IS NOT NULL AND (p."Locale" IS NULL OR p."Locale" != u."Locale"))
            )
        RETURNING p."Id"
    )
    SELECT COUNT(*) INTO updated_count FROM updated;

    -- Count users with no PersonId (should be 0 after Phase 10.1)
    SELECT COUNT(*) INTO skipped_count
    FROM "AspNetUsers"
    WHERE "PersonId" IS NULL;

    -- Display summary
    RAISE NOTICE 'Profile synchronization completed successfully!';
    RAISE NOTICE '';
    RAISE NOTICE 'Summary:';
    RAISE NOTICE '  Person records updated: %', updated_count;
    RAISE NOTICE '  Users without PersonId: %', skipped_count;
    
    IF skipped_count > 0 THEN
        RAISE WARNING 'Some users do not have PersonId assigned!';
        RAISE NOTICE 'Run Phase 10.1 backfill script first.';
        RAISE NOTICE '';
        RAISE NOTICE 'Users without PersonId:';
        
        FOR sample_record IN 
            SELECT "Id", "Email", "UserName"
            FROM "AspNetUsers"
            WHERE "PersonId" IS NULL
        LOOP
            RAISE NOTICE '  Id: %, Email: %, UserName: %', 
                sample_record."Id", sample_record."Email", sample_record."UserName";
        END LOOP;
    END IF;

    -- Show sample of updated Person records
    RAISE NOTICE '';
    RAISE NOTICE 'Sample of updated Person records:';
    
    FOR sample_record IN 
        SELECT 
            p."Id",
            p."FirstName",
            p."LastName",
            p."EmployeeId",
            p."Department",
            u."Email" AS linked_user_email
        FROM "Persons" p
        INNER JOIN "AspNetUsers" u ON u."PersonId" = p."Id"
        ORDER BY p."ModifiedAt" DESC
        LIMIT 5
    LOOP
        RAISE NOTICE '  Id: %, Name: % %, EmployeeId: %, Dept: %, Email: %', 
            sample_record."Id", 
            sample_record."FirstName", 
            sample_record."LastName",
            sample_record."EmployeeId",
            sample_record."Department",
            sample_record.linked_user_email;
    END LOOP;

EXCEPTION
    WHEN OTHERS THEN
        RAISE NOTICE 'Error occurred during profile synchronization:';
        RAISE NOTICE '  Error: %', SQLERRM;
        RAISE;
END $$;
