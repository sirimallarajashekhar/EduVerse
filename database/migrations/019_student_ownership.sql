-- Phase 21: explicit, tenant/campus-secured identity-to-student bindings.
-- No automatic guardian access based on unverified contact details.
CREATE TABLE IF NOT EXISTS student_portal_links (
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 subject_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 relationship_code VARCHAR(8) NOT NULL,
 linked_by CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 linked_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 PRIMARY KEY(tenant_id,campus_id,student_id,subject_id),
 KEY ix_portal_subject(tenant_id,subject_id),
 CONSTRAINT fk_portal_student FOREIGN KEY (tenant_id,campus_id,student_id)
   REFERENCES students(tenant_id,campus_id,id),
 CONSTRAINT fk_portal_membership FOREIGN KEY (tenant_id,subject_id)
   REFERENCES tenant_memberships(tenant_id,subject_id),
 CONSTRAINT chk_portal_relationship CHECK(relationship_code IN ('SELF','PARENT'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
INSERT INTO schema_migrations(version) VALUES('019_student_ownership')
ON DUPLICATE KEY UPDATE version=VALUES(version);
