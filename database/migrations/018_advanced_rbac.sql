-- Phase 20. Preserve ADMIN/MEMBER memberships and platform operators.
-- Custom roles never replace the tenant membership or confer PLATFORM_SUPER_ADMIN.
CREATE TABLE IF NOT EXISTS rbac_roles (
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 display_name VARCHAR(100) NOT NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 PRIMARY KEY (tenant_id,code),
 CONSTRAINT fk_rbac_role_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id),
 CONSTRAINT chk_rbac_role_code CHECK (code REGEXP '^[A-Z][A-Z0-9_]{2,31}$'
   AND code NOT IN ('ADMIN','MEMBER','PLATFORM_SUPER_ADMIN'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE IF NOT EXISTS rbac_role_permissions (
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 role_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 permission_code VARCHAR(90) NOT NULL,
 PRIMARY KEY (tenant_id,role_code,permission_code),
 CONSTRAINT fk_rbac_perm_role FOREIGN KEY (tenant_id,role_code) REFERENCES rbac_roles(tenant_id,code)
 ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE IF NOT EXISTS rbac_user_roles (
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 subject_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 role_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 PRIMARY KEY (tenant_id,subject_id,role_code),
 KEY ix_rbac_assigned_role (tenant_id,role_code),
 CONSTRAINT fk_rbac_assignment_role FOREIGN KEY (tenant_id,role_code)
   REFERENCES rbac_roles(tenant_id,code) ON DELETE CASCADE,
 CONSTRAINT fk_rbac_assignment_member FOREIGN KEY (tenant_id,subject_id)
   REFERENCES tenant_memberships(tenant_id,subject_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
INSERT INTO schema_migrations(version) VALUES('018_advanced_rbac')
ON DUPLICATE KEY UPDATE version=VALUES(version);
