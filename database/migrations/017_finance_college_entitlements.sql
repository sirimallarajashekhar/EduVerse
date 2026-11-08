-- EduVerse: add explicit finance and college entitlements to STANDARD plan.
-- Existing PILOT tenants remain unchanged. A College/Mixed tenant still needs the
-- college feature AND the correct institution kind (API checks both).
INSERT INTO subscription_plan_features(plan_code, feature_code)
SELECT 'STANDARD', 'finance'
WHERE EXISTS (SELECT 1 FROM subscription_plans WHERE plan_code='STANDARD')
ON DUPLICATE KEY UPDATE feature_code=VALUES(feature_code);
INSERT INTO subscription_plan_features(plan_code, feature_code)
SELECT 'STANDARD', 'college'
WHERE EXISTS (SELECT 1 FROM subscription_plans WHERE plan_code='STANDARD')
ON DUPLICATE KEY UPDATE feature_code=VALUES(feature_code);
INSERT INTO schema_migrations(version) VALUES ('017_finance_college_entitlements')
ON DUPLICATE KEY UPDATE version=VALUES(version);
