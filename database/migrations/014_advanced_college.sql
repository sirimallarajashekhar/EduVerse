-- Phase 16: credit-based college programs and immutable semester results.
-- Apply after migrations 001-013. MySQL 8; all relationships tenant/campus scoped.
CREATE TABLE college_regulations (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(30) NOT NULL, name VARCHAR(160) NOT NULL,
 passing_points DECIMAL(5,2) NOT NULL DEFAULT 4.00,
 min_credits_for_progression DECIMAL(7,2) NOT NULL DEFAULT 0,
 allow_backlogs BOOLEAN NOT NULL DEFAULT FALSE,
 max_semesters SMALLINT UNSIGNED NOT NULL DEFAULT 8,
 UNIQUE KEY uq_college_regulation_code(tenant_id,campus_id,code),
 UNIQUE KEY uq_college_regulation_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 CHECK(passing_points BETWEEN 0 AND 10),
 CHECK(min_credits_for_progression BETWEEN 0 AND 200),
 CHECK(max_semesters BETWEEN 2 AND 16)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE college_programs (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 catalog_program_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 regulation_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(30) NOT NULL, name VARCHAR(160) NOT NULL,
 UNIQUE KEY uq_college_program_code(tenant_id,campus_id,code),
 UNIQUE KEY uq_college_program_catalog(tenant_id,campus_id,catalog_program_id),
 UNIQUE KEY uq_college_program_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,catalog_program_id) REFERENCES academic_catalog(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,regulation_id) REFERENCES college_regulations(tenant_id,campus_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE college_semesters (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 program_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 academic_year_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 catalog_semester_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 semester_number SMALLINT UNSIGNED NOT NULL,
 status VARCHAR(12) NOT NULL DEFAULT 'OPEN',
 UNIQUE KEY uq_college_semester_program_year(tenant_id,campus_id,program_id,academic_year_id,semester_number),
 UNIQUE KEY uq_college_semester_catalog(tenant_id,campus_id,catalog_semester_id),
 UNIQUE KEY uq_college_semester_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,program_id) REFERENCES college_programs(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,academic_year_id) REFERENCES academic_years(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,catalog_semester_id) REFERENCES academic_catalog(tenant_id,campus_id,id),
 CHECK(semester_number BETWEEN 1 AND 16),
 CHECK(status IN ('OPEN','CLOSED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE college_courses (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 program_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 semester_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 subject_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(30) NOT NULL, title VARCHAR(160) NOT NULL,
 course_type VARCHAR(12) NOT NULL,
 credits DECIMAL(5,2) NOT NULL,
 capacity SMALLINT UNSIGNED NOT NULL,
 prerequisite_course_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 UNIQUE KEY uq_college_course_code(tenant_id,campus_id,program_id,code),
 UNIQUE KEY uq_college_course_subject(tenant_id,campus_id,semester_id,subject_id),
 UNIQUE KEY uq_college_course_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,program_id) REFERENCES college_programs(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,semester_id) REFERENCES college_semesters(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,subject_id) REFERENCES academic_catalog(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,prerequisite_course_id) REFERENCES college_courses(tenant_id,campus_id,id),
 CHECK(course_type IN ('CORE','ELECTIVE')),
 CHECK(credits>0 AND credits<=30),
 CHECK(capacity BETWEEN 1 AND 5000)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Snapshot the chosen published Phase 11 subject result so transcript changes do not follow mutable marks.
ALTER TABLE exam_result_subject_snapshots ADD UNIQUE KEY uq_college_published_subject_scope(tenant_id,campus_id,id);

CREATE TABLE college_registrations (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 enrollment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 course_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 semester_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 status VARCHAR(12) NOT NULL DEFAULT 'ENROLLED',
 grade_points DECIMAL(5,2) NULL,
 grade_label VARCHAR(10) NULL,
 passed BOOLEAN NULL,
 source_snapshot_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 graded_utc DATETIME(6) NULL,
 UNIQUE KEY uq_college_registration(tenant_id,campus_id,enrollment_id,course_id),
 UNIQUE KEY uq_college_registration_scope(tenant_id,campus_id,id),
 KEY ix_college_seats(tenant_id,campus_id,course_id,status),
 FOREIGN KEY(tenant_id,campus_id,enrollment_id) REFERENCES student_enrollments(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,course_id) REFERENCES college_courses(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,semester_id) REFERENCES college_semesters(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,source_snapshot_id) REFERENCES exam_result_subject_snapshots(tenant_id,campus_id,id),
 CHECK(status IN ('ENROLLED','PASSED','BACKLOG')),
 CHECK(grade_points IS NULL OR grade_points BETWEEN 0 AND 10),
 CHECK((status='ENROLLED' AND grade_points IS NULL AND source_snapshot_id IS NULL)
    OR (status IN ('PASSED','BACKLOG') AND grade_points IS NOT NULL AND source_snapshot_id IS NOT NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE college_semester_results (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 enrollment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 semester_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 attempted_credits DECIMAL(7,2) NOT NULL,
 earned_credits DECIMAL(7,2) NOT NULL,
 weighted_points DECIMAL(12,4) NOT NULL,
 sgpa DECIMAL(5,2) NOT NULL,
 backlogs INT UNSIGNED NOT NULL,
 progression_eligible BOOLEAN NOT NULL,
 finalized_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 finalized_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_college_result(tenant_id,campus_id,enrollment_id,semester_id),
 FOREIGN KEY(tenant_id,campus_id,enrollment_id) REFERENCES student_enrollments(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,semester_id) REFERENCES college_semesters(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,finalized_by) REFERENCES tenant_memberships(tenant_id,subject_id),
 CHECK(attempted_credits>0 AND earned_credits>=0 AND earned_credits<=attempted_credits),
 CHECK(sgpa BETWEEN 0 AND 10)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO schema_migrations(version) VALUES ('014_advanced_college')
ON DUPLICATE KEY UPDATE version=VALUES(version);
