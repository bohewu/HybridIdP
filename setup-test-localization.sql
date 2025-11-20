-- Insert sample localization data for consent text
-- This script inserts test data for the Resource table used for multi-language consent text

INSERT INTO "Resources" ("Id", "Key", "Culture", "Value", "Category", "CreatedUtc", "UpdatedUtc") VALUES
-- English (en-US) entries
(gen_random_uuid(), 'scope.profile.display', 'en-US', 'Access to your profile information', 'Consent', NOW(), NOW()),
(gen_random_uuid(), 'scope.profile.description', 'en-US', 'This scope allows the application to access your basic profile information including name and email.', 'Consent', NOW(), NOW()),
(gen_random_uuid(), 'scope.email.display', 'en-US', 'Access to your email address', 'Consent', NOW(), NOW()),
(gen_random_uuid(), 'scope.email.description', 'en-US', 'This scope allows the application to access your email address for communication purposes.', 'Consent', NOW(), NOW()),
(gen_random_uuid(), 'scope.phone.display', 'en-US', 'Access to your phone number', 'Consent', NOW(), NOW()),
(gen_random_uuid(), 'scope.phone.description', 'en-US', 'This scope allows the application to access your phone number for verification and contact.', 'Consent', NOW(), NOW()),

-- Traditional Chinese (zh-TW) entries
(gen_random_uuid(), 'scope.profile.display', 'zh-TW', '存取您的個人資料', 'Consent', NOW(), NOW()),
(gen_random_uuid(), 'scope.profile.description', 'zh-TW', '此範圍允許應用程式存取您的基本個人資料，包括姓名和電子郵件。', 'Consent', NOW(), NOW()),
(gen_random_uuid(), 'scope.email.display', 'zh-TW', '存取您的電子郵件地址', 'Consent', NOW(), NOW()),
(gen_random_uuid(), 'scope.email.description', 'zh-TW', '此範圍允許應用程式存取您的電子郵件地址以進行通訊。', 'Consent', NOW(), NOW()),
(gen_random_uuid(), 'scope.phone.display', 'zh-TW', '存取您的電話號碼', 'Consent', NOW(), NOW()),
(gen_random_uuid(), 'scope.phone.description', 'zh-TW', '此範圍允許應用程式存取您的電話號碼以進行驗證和聯絡。', 'Consent', NOW(), NOW())

ON CONFLICT ("Key", "Culture") DO NOTHING;