-- Phase 9: apply after migrations 001-006. MySQL 8 / InnoDB.
-- Every dependent entity is tenant+campus scoped; scheduling conflicts are checked
-- transactionally with campus-row locking in the repository.
CREATE TABLE IF NOT EXISTS academic_periods (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(30) NOT NULL,
 display_name VARCHAR(100) NOT NULL,
 starts_at TIME NOT NULL,
 ends_at TIME NOT NULL,
 is_active BOOLEAN NOT NULL DEFAULT TRUE,
 UNIQUE KEY uq_period_code(tenant_id,campus_id,code),
 UNIQUE KEY uq_period_scope(tenant_id,campus_id,id),
 CONSTRAINT fk_period_campus FOREIGN KEY (tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 CONSTRAINT chk_period_times CHECK (starts_at < ends_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS academic_timetable_slots (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 academic_year_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 class_item_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 subject_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 period_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 teacher_subject_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 day_of_week TINYINT UNSIGNED NOT NULL,
 is_active BOOLEAN NOT NULL DEFAULT TRUE,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_slot_class_period(tenant_id,campus_id,academic_year_id,class_item_id,day_of_week,period_id),
 KEY ix_slot_teacher(tenant_id,campus_id,academic_year_id,teacher_subject_id,day_of_week),
 KEY ix_slot_subject(tenant_id,campus_id,subject_id),
 CONSTRAINT fk_slot_year FOREIGN KEY (tenant_id,campus_id,academic_year_id) REFERENCES academic_years(tenant_id,campus_id,id),
 CONSTRAINT fk_slot_class FOREIGN KEY (tenant_id,campus_id,class_item_id) REFERENCES academic_catalog(tenant_id,campus_id,id),
 CONSTRAINT fk_slot_subject FOREIGN KEY (tenant_id,campus_id,subject_id) REFERENCES academic_catalog(tenant_id,campus_id,id),
 CONSTRAINT fk_slot_period FOREIGN KEY (tenant_id,campus_id,period_id) REFERENCES academic_periods(tenant_id,campus_id,id),
 CONSTRAINT fk_slot_teacher FOREIGN KEY (tenant_id,teacher_subject_id) REFERENCES tenant_memberships(tenant_id,subject_id),
 CONSTRAINT chk_slot_day CHECK(day_of_week BETWEEN 1 AND 7)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Tie attendance enrollment to the same tenant and campus at the DB layer.
ALTER TABLE student_enrollments
 ADD UNIQUE KEY uq_attendance_enrollment_scope(tenant_id,campus_id,id);

CREATE TABLE IF NOT EXISTS student_attendance (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 enrollment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 attendance_date DATE NOT NULL,
 period_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 attendance_scope VARCHAR(8) NOT NULL,
 status VARCHAR(10) NOT NULL,
 remarks VARCHAR(250) NULL,
 marked_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 marked_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 -- null-safe uniqueness for DAILY vs PERIOD, unlike MySQL nullable unique keys
 attendance_key VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 UNIQUE KEY uq_student_attendance(tenant_id,campus_id,student_id,attendance_date,attendance_key),
 KEY ix_attendance_summary(tenant_id,campus_id,enrollment_id,attendance_date),
 CONSTRAINT fk_attendance_student FOREIGN KEY (tenant_id,campus_id,student_id) REFERENCES students(tenant_id,campus_id,id),
 CONSTRAINT fk_attendance_enrollment FOREIGN KEY (tenant_id,campus_id,enrollment_id) REFERENCES student_enrollments(tenant_id,campus_id,id),
 CONSTRAINT fk_attendance_period FOREIGN KEY (tenant_id,campus_id,period_id) REFERENCES academic_periods(tenant_id,campus_id,id),
 CONSTRAINT fk_attendance_actor FOREIGN KEY (tenant_id,marked_by) REFERENCES tenant_memberships(tenant_id,subject_id),
 CONSTRAINT chk_attendance_scope CHECK(attendance_scope IN ('DAILY','PERIOD')),
 CONSTRAINT chk_attendance_status CHECK(status IN ('PRESENT','ABSENT','LATE','EXCUSED')),
 CONSTRAINT chk_attendance_period CHECK(
  (attendance_scope='DAILY' AND period_id IS NULL AND attendance_key='DAILY') OR
  (attendance_scope='PERIOD' AND period_id IS NOT NULL AND attendance_key=period_id))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS staff_attendance (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 staff_subject_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 attendance_date DATE NOT NULL,
 status VARCHAR(10) NOT NULL,
 remarks VARCHAR(250) NULL,
 marked_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 marked_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_staff_attendance(tenant_id,campus_id,staff_subject_id,attendance_date),
 KEY ix_staff_attendance_date(tenant_id,campus_id,attendance_date),
 CONSTRAINT fk_staff_campus FOREIGN KEY (tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 CONSTRAINT fk_staff_member FOREIGN KEY (tenant_id,staff_subject_id) REFERENCES tenant_memberships(tenant_id,subject_id),
 CONSTRAINT fk_staff_actor FOREIGN KEY (tenant_id,marked_by) REFERENCES tenant_memberships(tenant_id,subject_id),
 CONSTRAINT chk_staff_attendance_status CHECK(status IN ('PRESENT','ABSENT','LATE','EXCUSED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO schema_migrations(version) VALUES ('007_attendance_timetables')
ON DUPLICATE KEY UPDATE version=VALUES(version);
