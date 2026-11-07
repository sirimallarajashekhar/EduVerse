-- EduVerse Phase 15. Apply after 001–012. API-owned learning/communication data.
CREATE TABLE learning_lessons (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 academic_year_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 class_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 subject_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 title VARCHAR(160) NOT NULL,
 body TEXT NOT NULL,
 resource_url VARCHAR(600) NULL,
 status VARCHAR(12) NOT NULL DEFAULT 'DRAFT',
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 published_utc DATETIME(6) NULL,
 UNIQUE KEY uq_learning_lesson_scope(tenant_id,campus_id,id),
 KEY ix_learning_class(tenant_id,campus_id,academic_year_id,class_id,status),
 FOREIGN KEY(tenant_id,campus_id,academic_year_id) REFERENCES academic_years(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,class_id) REFERENCES academic_catalog(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,subject_id) REFERENCES academic_catalog(tenant_id,campus_id,id),
 CHECK(status IN ('DRAFT','PUBLISHED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE learning_assignments (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 lesson_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 title VARCHAR(160) NOT NULL, instructions TEXT NOT NULL,
 due_utc DATETIME(6) NOT NULL, max_score DECIMAL(8,2) NOT NULL,
 status VARCHAR(12) NOT NULL DEFAULT 'DRAFT',
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_learning_assignment_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,lesson_id) REFERENCES learning_lessons(tenant_id,campus_id,id),
 CHECK(status IN ('DRAFT','PUBLISHED')),
 CHECK(max_score>0 AND max_score<=999999.99)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE learning_submissions (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 assignment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 enrollment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 response_text TEXT NOT NULL,
 status VARCHAR(12) NOT NULL DEFAULT 'SUBMITTED',
 score DECIMAL(8,2) NULL,
 feedback VARCHAR(1000) NULL,
 submitted_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 graded_utc DATETIME(6) NULL,
 graded_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL,
 UNIQUE KEY uq_learning_student_submission(tenant_id,campus_id,assignment_id,enrollment_id),
 UNIQUE KEY uq_learning_submission_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,assignment_id) REFERENCES learning_assignments(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,enrollment_id) REFERENCES student_enrollments(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,graded_by) REFERENCES tenant_memberships(tenant_id,subject_id),
 CHECK(status IN ('SUBMITTED','GRADED')),
 CHECK((status='SUBMITTED' AND score IS NULL AND graded_utc IS NULL)
    OR (status='GRADED' AND score IS NOT NULL AND score>=0 AND graded_utc IS NOT NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE learning_announcements (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 title VARCHAR(160) NOT NULL, body TEXT NOT NULL,
 audience VARCHAR(16) NOT NULL,
 published_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_announcement_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 CHECK(audience IN ('ALL','STAFF','GUARDIANS'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Student guardian contacts are private; parent-teacher threads are admin-mediated initially.
ALTER TABLE student_guardians ADD UNIQUE KEY uq_learning_guardian_scope(tenant_id,campus_id,id);

CREATE TABLE learning_parent_threads (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 guardian_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 subject VARCHAR(160) NOT NULL, status VARCHAR(12) NOT NULL DEFAULT 'OPEN',
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_parent_thread_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,student_id) REFERENCES students(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,guardian_id) REFERENCES student_guardians(tenant_id,campus_id,id),
 CHECK(status IN ('OPEN','CLOSED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE learning_parent_messages (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 thread_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 author_subject VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 body TEXT NOT NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 FOREIGN KEY(tenant_id,campus_id,thread_id) REFERENCES learning_parent_threads(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,author_subject) REFERENCES tenant_memberships(tenant_id,subject_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- In-app delivery only, no claims of SMS/email/push delivery.
CREATE TABLE learning_notifications (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 recipient_subject VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 source_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 title VARCHAR(160) NOT NULL, body VARCHAR(1000) NOT NULL,
 status VARCHAR(12) NOT NULL DEFAULT 'DELIVERED',
 delivered_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 read_utc DATETIME(6) NULL,
 UNIQUE KEY uq_notification_scope(tenant_id,campus_id,id),
 KEY ix_notification_inbox(tenant_id,campus_id,recipient_subject,status),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 FOREIGN KEY(tenant_id,recipient_subject) REFERENCES tenant_memberships(tenant_id,subject_id),
 CHECK(status IN ('DELIVERED','READ'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT INTO schema_migrations(version) VALUES ('013_learning_communication')
ON DUPLICATE KEY UPDATE version=VALUES(version);
