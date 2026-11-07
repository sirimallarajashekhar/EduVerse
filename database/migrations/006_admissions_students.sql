-- EduVerse Phase 8. Apply after migrations 001–005. Forward-only MySQL 8 schema.
-- Every campus-owned relationship includes tenant_id and campus_id.
CREATE TABLE IF NOT EXISTS admission_applications (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 application_number BIGINT UNSIGNED NOT NULL AUTO_INCREMENT UNIQUE,
 given_name VARCHAR(100) NOT NULL,
 family_name VARCHAR(100) NOT NULL,
 date_of_birth DATE NOT NULL,
 contact_email VARCHAR(254) NULL,
 contact_phone VARCHAR(24) NULL,
 status VARCHAR(16) NOT NULL DEFAULT 'SUBMITTED',
 reviewed_by CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 reviewed_utc DATETIME(6) NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_application_scope (tenant_id,campus_id,id),
 KEY ix_application_queue (tenant_id,campus_id,status,created_utc),
 CONSTRAINT fk_admission_campus FOREIGN KEY (tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 CONSTRAINT chk_application_status CHECK (status IN ('SUBMITTED','APPROVED','REJECTED','ENROLLED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS students (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 application_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 admission_number VARCHAR(40) NOT NULL,
 given_name VARCHAR(100) NOT NULL,
 family_name VARCHAR(100) NOT NULL,
 date_of_birth DATE NOT NULL,
 status VARCHAR(16) NOT NULL DEFAULT 'ACTIVE',
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_student_tenant_admission (tenant_id,admission_number),
 UNIQUE KEY uq_student_application (application_id),
 UNIQUE KEY uq_student_campus (tenant_id,campus_id,id),
 KEY ix_student_active (tenant_id,campus_id,status),
 CONSTRAINT fk_student_campus FOREIGN KEY (tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 CONSTRAINT fk_student_application FOREIGN KEY (tenant_id,campus_id,application_id)
    REFERENCES admission_applications(tenant_id,campus_id,id),
 CONSTRAINT chk_student_status CHECK (status IN ('ACTIVE','TRANSFERRED','WITHDRAWN'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS student_guardians (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 full_name VARCHAR(160) NOT NULL,
 relationship_code VARCHAR(20) NOT NULL,
 contact_phone VARCHAR(24) NULL,
 contact_email VARCHAR(254) NULL,
 is_primary BOOLEAN NOT NULL DEFAULT FALSE,
 KEY ix_guardian_student (tenant_id,campus_id,student_id),
 CONSTRAINT fk_guardian_student FOREIGN KEY (tenant_id,campus_id,student_id)
    REFERENCES students(tenant_id,campus_id,id),
 CONSTRAINT chk_guardian_relation CHECK (relationship_code IN ('MOTHER','FATHER','GUARDIAN','OTHER'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS student_enrollments (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 academic_year_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 academic_item_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 status VARCHAR(16) NOT NULL DEFAULT 'ACTIVE',
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_enrollment_student_year (tenant_id,student_id,academic_year_id),
 KEY ix_enrollment_class (tenant_id,campus_id,academic_item_id,status),
 CONSTRAINT fk_enrollment_student FOREIGN KEY (tenant_id,campus_id,student_id)
    REFERENCES students(tenant_id,campus_id,id),
 CONSTRAINT fk_enrollment_year FOREIGN KEY (tenant_id,campus_id,academic_year_id)
    REFERENCES academic_years(tenant_id,campus_id,id),
 CONSTRAINT fk_enrollment_item FOREIGN KEY (tenant_id,campus_id,academic_item_id)
    REFERENCES academic_catalog(tenant_id,campus_id,id),
 CONSTRAINT chk_enrollment_status CHECK (status IN ('ACTIVE','PROMOTED','TRANSFERRED','WITHDRAWN'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS student_lifecycle_events (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 event_code VARCHAR(20) NOT NULL,
 reason VARCHAR(400) NULL,
 old_item_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 new_item_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 occurred_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 actor_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 KEY ix_student_events (tenant_id,campus_id,student_id,occurred_utc),
 CONSTRAINT fk_event_student FOREIGN KEY (tenant_id,campus_id,student_id)
    REFERENCES students(tenant_id,campus_id,id),
 CONSTRAINT chk_lifecycle_code CHECK (event_code IN ('PROMOTED','TRANSFERRED','WITHDRAWN'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Documents remain private database objects: no public path; hash for integrity.
CREATE TABLE IF NOT EXISTS student_documents (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 category VARCHAR(30) NOT NULL,
 original_filename VARCHAR(180) NOT NULL,
 content_type VARCHAR(60) NOT NULL,
 content_sha256 BINARY(32) NOT NULL,
 content_bytes MEDIUMBLOB NOT NULL,
 byte_length INT NOT NULL,
 verification_status VARCHAR(16) NOT NULL DEFAULT 'PENDING',
 verified_by CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 verified_utc DATETIME(6) NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 KEY ix_documents_student (tenant_id,campus_id,student_id,created_utc),
 CONSTRAINT fk_document_student FOREIGN KEY (tenant_id,campus_id,student_id)
    REFERENCES students(tenant_id,campus_id,id),
 CONSTRAINT chk_document_verification CHECK (verification_status IN ('PENDING','VERIFIED','REJECTED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO schema_migrations(version) VALUES ('006_admissions_students')
ON DUPLICATE KEY UPDATE version=VALUES(version);
