-- Apply after 001-015. Bootstrap operators OUT OF BAND only (see docs/PLATFORM_APPROVAL.md).
CREATE TABLE IF NOT EXISTS platform_operators (
  user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_platform_operator_user FOREIGN KEY (user_id) REFERENCES identity_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS platform_sessions (
  id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
  user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  expires_utc DATETIME(6) NOT NULL,
  revoked_utc DATETIME(6) NULL,
  KEY ix_platform_session_user (user_id, expires_utc),
  CONSTRAINT fk_platform_session_user FOREIGN KEY (user_id) REFERENCES identity_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS platform_approval_audit (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  request_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  operator_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  action VARCHAR(16) NOT NULL,
  reason VARCHAR(500) NULL,
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_platform_audit_request (request_id,created_utc),
  CONSTRAINT fk_platform_audit_request FOREIGN KEY (request_id) REFERENCES institution_registration_requests(id),
  CONSTRAINT fk_platform_audit_operator FOREIGN KEY (operator_id) REFERENCES platform_operators(user_id),
  CONSTRAINT chk_platform_audit_action CHECK (action IN ('APPROVED','REJECTED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
ALTER TABLE institution_registration_requests
  ADD COLUMN reviewed_by CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
  ADD COLUMN review_reason VARCHAR(500) NULL,
  ADD COLUMN provisioned_tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL;
CREATE INDEX ix_registration_slug ON institution_registration_requests(requested_slug);
INSERT INTO schema_migrations(version) VALUES ('016_platform_approvals')
ON DUPLICATE KEY UPDATE version=VALUES(version);
