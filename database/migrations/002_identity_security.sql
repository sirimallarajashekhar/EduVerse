-- Phase 4. Apply after 001_tenant_foundation.sql, with a dedicated migration account.
-- All UUID columns are canonical lowercase CHAR(36). Do not log password hashes or reset tokens.
CREATE TABLE IF NOT EXISTS identity_users (
  id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
  email VARCHAR(254) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  email_normalized VARCHAR(254) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  password_hash VARCHAR(512) NULL,
  display_name VARCHAR(160) NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  failed_logins INT NOT NULL DEFAULT 0,
  locked_until_utc DATETIME(6) NULL,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_identity_email (email_normalized)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS identity_sessions (
  id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
  user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  expires_utc DATETIME(6) NOT NULL,
  revoked_utc DATETIME(6) NULL,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_session_user (user_id, tenant_id),
  KEY ix_session_expiry (expires_utc),
  CONSTRAINT fk_identity_session_user FOREIGN KEY (user_id) REFERENCES identity_users(id),
  CONSTRAINT fk_identity_session_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS identity_reset_tokens (
  token_hash BINARY(32) NOT NULL PRIMARY KEY,
  user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  expires_utc DATETIME(6) NOT NULL,
  used_utc DATETIME(6) NULL,
  KEY ix_reset_user (user_id),
  CONSTRAINT fk_identity_reset_user FOREIGN KEY (user_id) REFERENCES identity_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS identity_role_permissions (
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  role_code VARCHAR(32) NOT NULL,
  permission_code VARCHAR(90) NOT NULL,
  PRIMARY KEY (tenant_id, role_code, permission_code),
  CONSTRAINT fk_role_permission_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS identity_campus_access (
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  subject_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  PRIMARY KEY (tenant_id, subject_id, campus_id),
  CONSTRAINT fk_campus_scope_membership FOREIGN KEY (tenant_id, subject_id)
    REFERENCES tenant_memberships(tenant_id, subject_id),
  CONSTRAINT fk_campus_scope_campus FOREIGN KEY (campus_id) REFERENCES campuses(id),
  KEY ix_scope_campus (tenant_id, campus_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS identity_audit_events (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
  user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
  event_code VARCHAR(64) NOT NULL,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_audit_tenant_date (tenant_id, created_utc),
  KEY ix_audit_user_date (user_id, created_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO schema_migrations(version) VALUES ('002_identity_security')
ON DUPLICATE KEY UPDATE version = VALUES(version);
