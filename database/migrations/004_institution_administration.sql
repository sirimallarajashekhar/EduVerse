-- Phase 6 institution administration. Apply only after 001–003 with a migration-only account.
CREATE TABLE IF NOT EXISTS institution_registration_requests (
  id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
  requested_slug VARCHAR(80) NOT NULL,
  institution_name VARCHAR(160) NOT NULL,
  institution_kind VARCHAR(16) NOT NULL,
  contact_email VARCHAR(254) NOT NULL,
  status VARCHAR(16) NOT NULL DEFAULT 'PENDING',
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  reviewed_utc DATETIME(6) NULL,
  KEY ix_registration_queue (status,created_utc),
  CONSTRAINT chk_registration_kind CHECK (institution_kind IN ('School','College','Mixed')),
  CONSTRAINT chk_registration_status CHECK (status IN ('PENDING','APPROVED','REJECTED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS institution_profiles (
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
  legal_name VARCHAR(160) NOT NULL,
  contact_email VARCHAR(254) NOT NULL,
  contact_phone VARCHAR(24) NULL,
  address_line VARCHAR(240) NULL,
  city VARCHAR(100) NULL,
  state_region VARCHAR(100) NULL,
  postal_code VARCHAR(20) NULL,
  onboarding_status VARCHAR(16) NOT NULL DEFAULT 'DRAFT',
  updated_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_institution_profile_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT chk_profile_status CHECK (onboarding_status IN ('DRAFT','READY'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS institution_departments (
  id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  code VARCHAR(30) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  display_name VARCHAR(160) NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_department_code (tenant_id,campus_id,code),
  KEY ix_department_campus (tenant_id,campus_id,is_active),
  CONSTRAINT fk_department_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_department_campus FOREIGN KEY (tenant_id,campus_id) REFERENCES campuses(tenant_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS institution_buildings (
  id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  code VARCHAR(30) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  display_name VARCHAR(160) NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_building_code (tenant_id,campus_id,code),
  KEY ix_building_campus (tenant_id,campus_id,is_active),
  CONSTRAINT fk_building_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_building_campus FOREIGN KEY (tenant_id,campus_id) REFERENCES campuses(tenant_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS institution_settings (
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  setting_key VARCHAR(50) NOT NULL,
  setting_value VARCHAR(240) NOT NULL,
  updated_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (tenant_id,setting_key),
  CONSTRAINT fk_setting_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS subscription_plans (
  plan_code VARCHAR(32) NOT NULL PRIMARY KEY,
  display_name VARCHAR(90) NOT NULL,
  description VARCHAR(240) NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS subscription_plan_features (
  plan_code VARCHAR(32) NOT NULL,
  feature_code VARCHAR(60) NOT NULL,
  PRIMARY KEY (plan_code,feature_code),
  CONSTRAINT fk_plan_feature FOREIGN KEY (plan_code) REFERENCES subscription_plans(plan_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS institution_subscriptions (
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
  plan_code VARCHAR(32) NOT NULL,
  starts_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  expires_utc DATETIME(6) NULL,
  status VARCHAR(16) NOT NULL DEFAULT 'ACTIVE',
  CONSTRAINT fk_subscription_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
  CONSTRAINT fk_subscription_plan FOREIGN KEY (plan_code) REFERENCES subscription_plans(plan_code),
  CONSTRAINT chk_subscription_status CHECK (status IN ('ACTIVE','SUSPENDED','CANCELLED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO subscription_plans(plan_code,display_name,description)
VALUES ('PILOT','Pilot','Free pilot feature set'),('STANDARD','Standard','Expanded institution feature set')
ON DUPLICATE KEY UPDATE plan_code=VALUES(plan_code);
INSERT INTO subscription_plan_features(plan_code,feature_code)
VALUES ('PILOT','core'),('PILOT','academics'),('STANDARD','core'),
       ('STANDARD','academics'),('STANDARD','ams'),('STANDARD','hrms'),
       ('STANDARD','reports')
ON DUPLICATE KEY UPDATE feature_code=VALUES(feature_code);

INSERT INTO schema_migrations(version) VALUES ('004_institution_administration')
ON DUPLICATE KEY UPDATE version=VALUES(version);
