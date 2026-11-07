-- Phase 11: apply after 001–008. MySQL 8: decimal grading, tenant/campus foreign keys.
CREATE TABLE exam_grading_schemes (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(30) NOT NULL, name VARCHAR(120) NOT NULL,
 scale_max DECIMAL(5,2) NOT NULL DEFAULT 10.00,
 is_active BOOLEAN NOT NULL DEFAULT TRUE,
 UNIQUE KEY uq_scheme_code(tenant_id,campus_id,code),
 UNIQUE KEY uq_scheme_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 CHECK(scale_max>0 AND scale_max<=10)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE exam_grade_bands (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 scheme_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 grade VARCHAR(10) NOT NULL, min_percent DECIMAL(5,2) NOT NULL,
 max_percent DECIMAL(5,2) NOT NULL, grade_points DECIMAL(5,2) NOT NULL,
 UNIQUE KEY uq_grade_label(tenant_id,campus_id,scheme_id,grade),
 FOREIGN KEY(tenant_id,campus_id,scheme_id) REFERENCES exam_grading_schemes(tenant_id,campus_id,id),
 CHECK(min_percent>=0 AND min_percent<=max_percent AND max_percent<=100),
 CHECK(grade_points>=0 AND grade_points<=10)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE examinations (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 academic_year_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 scheme_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(30) NOT NULL, title VARCHAR(160) NOT NULL,
 exam_type VARCHAR(20) NOT NULL, status VARCHAR(16) NOT NULL DEFAULT 'DRAFT',
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 approved_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL,
 approved_utc DATETIME(6) NULL, published_utc DATETIME(6) NULL,
 UNIQUE KEY uq_exam_code(tenant_id,campus_id,academic_year_id,code),
 UNIQUE KEY uq_exam_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,academic_year_id) REFERENCES academic_years(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,scheme_id) REFERENCES exam_grading_schemes(tenant_id,campus_id,id),
 CHECK(exam_type IN ('UNIT','MIDTERM','FINAL','SEMESTER','PRACTICAL','OTHER')),
 CHECK(status IN ('DRAFT','APPROVED','PUBLISHED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE exam_assessments (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 exam_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 academic_item_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 subject_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 scheduled_on DATE NOT NULL, starts_at TIME NULL, duration_minutes SMALLINT UNSIGNED NULL,
 max_marks DECIMAL(8,2) NOT NULL, pass_marks DECIMAL(8,2) NOT NULL,
 credits DECIMAL(5,2) NOT NULL DEFAULT 1,
 UNIQUE KEY uq_assessment(tenant_id,campus_id,exam_id,academic_item_id,subject_id),
 UNIQUE KEY uq_assessment_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,exam_id) REFERENCES examinations(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,academic_item_id) REFERENCES academic_catalog(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,subject_id) REFERENCES academic_catalog(tenant_id,campus_id,id),
 CHECK(max_marks>0 AND pass_marks>=0 AND pass_marks<=max_marks),
 CHECK(credits>0 AND credits<=50),
 CHECK(duration_minutes IS NULL OR duration_minutes BETWEEN 1 AND 600)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE exam_student_marks (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 assessment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 enrollment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 marks DECIMAL(8,2) NULL, is_absent BOOLEAN NOT NULL DEFAULT FALSE,
 entered_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 entered_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_marks(tenant_id,campus_id,assessment_id,enrollment_id),
 FOREIGN KEY(tenant_id,campus_id,assessment_id) REFERENCES exam_assessments(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,enrollment_id) REFERENCES student_enrollments(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,entered_by) REFERENCES tenant_memberships(tenant_id,subject_id),
 CHECK((is_absent=TRUE AND marks IS NULL) OR (is_absent=FALSE AND marks IS NOT NULL AND marks>=0))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE exam_result_publications (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 exam_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 enrollment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 total_marks DECIMAL(12,2) NOT NULL,
 maximum_marks DECIMAL(12,2) NOT NULL,
 percentage DECIMAL(6,2) NOT NULL,
 gpa DECIMAL(5,2) NOT NULL, passed BOOLEAN NOT NULL,
 published_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_result(tenant_id,campus_id,exam_id,enrollment_id),
 UNIQUE KEY uq_published_result_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,exam_id) REFERENCES examinations(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,enrollment_id) REFERENCES student_enrollments(tenant_id,campus_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Freeze complete subject-level report cards at publication, independently of later enrollments.
CREATE TABLE exam_result_subject_snapshots (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 result_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 subject_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 subject_name VARCHAR(160) NOT NULL,
 grade VARCHAR(10) NOT NULL,
 marks DECIMAL(8,2) NULL,
 max_marks DECIMAL(8,2) NOT NULL,
 percentage DECIMAL(6,2) NOT NULL,
 grade_points DECIMAL(5,2) NOT NULL,
 passed BOOLEAN NOT NULL,
 absent BOOLEAN NOT NULL,
 UNIQUE KEY uq_result_subject_snapshot(result_id,subject_id),
 FOREIGN KEY(tenant_id,campus_id,result_id) REFERENCES exam_result_publications(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,subject_id) REFERENCES academic_catalog(tenant_id,campus_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO schema_migrations(version) VALUES ('009_exams_results')
ON DUPLICATE KEY UPDATE version=VALUES(version);
