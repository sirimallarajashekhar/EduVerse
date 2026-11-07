-- EduVerse Phase 12: employee lifecycle, leave, private records and payroll foundations.
-- Apply after 001-009. All protected data is scoped to authenticated tenant and campus.
CREATE TABLE hr_designations (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(30) NOT NULL, title VARCHAR(120) NOT NULL,
 is_active BOOLEAN NOT NULL DEFAULT TRUE,
 UNIQUE KEY uq_hr_designation(tenant_id,campus_id,code),
 UNIQUE KEY uq_hr_designation_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

ALTER TABLE institution_departments
 ADD UNIQUE KEY uq_hr_department_scope(tenant_id,campus_id,id);

CREATE TABLE hr_employees (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 employee_number VARCHAR(40) NOT NULL,
 user_subject_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL,
 given_name VARCHAR(100) NOT NULL, family_name VARCHAR(100) NOT NULL,
 work_email VARCHAR(254) NULL,
 joined_on DATE NOT NULL, exited_on DATE NULL,
 employee_type VARCHAR(16) NOT NULL,
 status VARCHAR(16) NOT NULL DEFAULT 'ONBOARDING',
 department_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 designation_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_hr_employee_number(tenant_id,employee_number),
 UNIQUE KEY uq_hr_employee_account(tenant_id,user_subject_id),
 UNIQUE KEY uq_hr_employee_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 FOREIGN KEY(tenant_id,user_subject_id) REFERENCES tenant_memberships(tenant_id,subject_id),
 FOREIGN KEY(tenant_id,campus_id,department_id) REFERENCES institution_departments(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,designation_id) REFERENCES hr_designations(tenant_id,campus_id,id),
 CHECK(employee_type IN ('FACULTY','STAFF','CONTRACT')),
 CHECK(status IN ('ONBOARDING','ACTIVE','ON_LEAVE','EXITED')),
 CHECK(exited_on IS NULL OR exited_on>=joined_on)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE hr_employee_events (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 employee_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 event_type VARCHAR(20) NOT NULL,
 note VARCHAR(400) NULL,
 actor_subject_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 KEY ix_hr_events(tenant_id,campus_id,employee_id,created_utc),
 FOREIGN KEY(tenant_id,campus_id,employee_id) REFERENCES hr_employees(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,actor_subject_id) REFERENCES tenant_memberships(tenant_id,subject_id),
 CHECK(event_type IN ('CREATED','ACTIVATED','ON_LEAVE','RETURNED','EXITED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE hr_employee_documents (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 employee_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 document_type VARCHAR(24) NOT NULL, file_name VARCHAR(180) NOT NULL,
 mime_type VARCHAR(60) NOT NULL,
 content_sha256 BINARY(32) NOT NULL, payload MEDIUMBLOB NOT NULL,
 byte_length INT UNSIGNED NOT NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 FOREIGN KEY(tenant_id,campus_id,employee_id) REFERENCES hr_employees(tenant_id,campus_id,id),
 CHECK(document_type IN ('CONTRACT','QUALIFICATION','IDENTITY','OTHER')),
 CHECK(byte_length BETWEEN 4 AND 1048576)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE hr_leave_types (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(30) NOT NULL, title VARCHAR(120) NOT NULL,
 annual_days DECIMAL(6,2) NOT NULL,
 UNIQUE KEY uq_hr_leave_code(tenant_id,campus_id,code),
 UNIQUE KEY uq_hr_leave_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 CHECK(annual_days>=0 AND annual_days<=366)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE hr_leave_balances (
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 employee_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 leave_type_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 year SMALLINT NOT NULL,
 allowance DECIMAL(6,2) NOT NULL,
 used DECIMAL(6,2) NOT NULL DEFAULT 0,
 PRIMARY KEY(tenant_id,campus_id,employee_id,leave_type_id,year),
 FOREIGN KEY(tenant_id,campus_id,employee_id) REFERENCES hr_employees(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,leave_type_id) REFERENCES hr_leave_types(tenant_id,campus_id,id),
 CHECK(allowance>=0 AND used>=0 AND used<=allowance)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE hr_leave_requests (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 employee_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 leave_type_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 from_date DATE NOT NULL, to_date DATE NOT NULL,
 days DECIMAL(6,2) NOT NULL,
 reason VARCHAR(300) NOT NULL,
 status VARCHAR(16) NOT NULL DEFAULT 'PENDING',
 reviewed_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL,
 reviewed_utc DATETIME(6) NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_hr_leave_scope(tenant_id,campus_id,id),
 KEY ix_hr_leave_queue(tenant_id,campus_id,status,created_utc),
 FOREIGN KEY(tenant_id,campus_id,employee_id) REFERENCES hr_employees(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,leave_type_id) REFERENCES hr_leave_types(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,reviewed_by) REFERENCES tenant_memberships(tenant_id,subject_id),
 CHECK(status IN ('PENDING','APPROVED','REJECTED')),
 CHECK(days>0 AND from_date<=to_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE hr_salary_structures (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 employee_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 effective_on DATE NOT NULL,
 base_amount DECIMAL(18,2) NOT NULL,
 allowance_amount DECIMAL(18,2) NOT NULL DEFAULT 0,
 deduction_amount DECIMAL(18,2) NOT NULL DEFAULT 0,
 currency CHAR(3) NOT NULL DEFAULT 'INR',
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_hr_salary_effective(tenant_id,campus_id,employee_id,effective_on),
 FOREIGN KEY(tenant_id,campus_id,employee_id) REFERENCES hr_employees(tenant_id,campus_id,id),
 CHECK(base_amount>=0 AND allowance_amount>=0 AND deduction_amount>=0),
 CHECK(base_amount+allowance_amount>=deduction_amount),
 CHECK(currency='INR')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE hr_payroll_previews (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 employee_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 payroll_month DATE NOT NULL,
 gross_amount DECIMAL(18,2) NOT NULL, deduction_amount DECIMAL(18,2) NOT NULL,
 net_amount DECIMAL(18,2) NOT NULL,
 calculated_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_hr_payroll_period(tenant_id,campus_id,employee_id,payroll_month),
 FOREIGN KEY(tenant_id,campus_id,employee_id) REFERENCES hr_employees(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,calculated_by) REFERENCES tenant_memberships(tenant_id,subject_id),
 CHECK(gross_amount>=0 AND deduction_amount>=0 AND net_amount>=0),
 CHECK(net_amount=gross_amount-deduction_amount)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO subscription_plan_features(plan_code,feature_code)
VALUES ('PILOT','hrms')
ON DUPLICATE KEY UPDATE feature_code=VALUES(feature_code);

INSERT INTO schema_migrations(version) VALUES ('010_hrms')
ON DUPLICATE KEY UPDATE version=VALUES(version);
