-- Phase 24: immutable platform governance trail with the actual operator and target.
-- Apply after 019_student_ownership.sql.
CREATE TABLE IF NOT EXISTS platform_control_audit (
 id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
 operator_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 target_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 action_code VARCHAR(48) NOT NULL,
 reason VARCHAR(500) NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 KEY ix_platform_audit_tenant_time(tenant_id,created_utc),
 KEY ix_platform_audit_operator_time(operator_id,created_utc),
 CONSTRAINT fk_platform_control_operator FOREIGN KEY(operator_id) REFERENCES identity_users(id),
 CONSTRAINT fk_platform_control_tenant FOREIGN KEY(tenant_id) REFERENCES tenants(id),
 CONSTRAINT fk_platform_control_target FOREIGN KEY(target_user_id) REFERENCES identity_users(id),
 CHECK(action_code IN ('USER_INVITED','USER_DEACTIVATED','USER_ACTIVATED','INVITE_EMAIL_FAILED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO schema_migrations(version) VALUES ('020_platform_control_audit')
ON DUPLICATE KEY UPDATE version=VALUES(version);
