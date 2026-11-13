-- Full-feature pilot for action-by-action acceptance testing.
-- Extend only active PILOT subscriptions through plan features; do not bypass
-- membership, campus, role, record ownership or institution-kind validation.
INSERT INTO subscription_plan_features(plan_code, feature_code)
SELECT p.plan_code, f.feature_code
FROM subscription_plans p
CROSS JOIN (
  SELECT 'core' AS feature_code UNION ALL SELECT 'academics'
  UNION ALL SELECT 'finance' UNION ALL SELECT 'operations'
  UNION ALL SELECT 'learning' UNION ALL SELECT 'college'
  UNION ALL SELECT 'reports' UNION ALL SELECT 'workflows'
  UNION ALL SELECT 'ams' UNION ALL SELECT 'hrms'
) f
WHERE p.plan_code='PILOT'
ON DUPLICATE KEY UPDATE feature_code=VALUES(feature_code);

-- These advertised STANDARD modules previously had no matching plan features.
INSERT INTO subscription_plan_features(plan_code, feature_code)
SELECT p.plan_code, f.feature_code
FROM subscription_plans p
CROSS JOIN (
  SELECT 'operations' AS feature_code UNION ALL SELECT 'learning'
  UNION ALL SELECT 'workflows'
) f
WHERE p.plan_code='STANDARD'
ON DUPLICATE KEY UPDATE feature_code=VALUES(feature_code);

UPDATE subscription_plans
SET description='Full module access during the free pilot and acceptance testing'
WHERE plan_code='PILOT';

INSERT INTO schema_migrations(version) VALUES ('021_pilot_all_module_access')
ON DUPLICATE KEY UPDATE version=VALUES(version);
