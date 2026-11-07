-- EduVerse phase 7: academic foundations; MySQL 8.0, after migration 004.
-- Every dependent relation is tenant+campus scoped at the database boundary.
CREATE TABLE IF NOT EXISTS academic_years (
  id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  code VARCHAR(30) NOT NULL,
  display_name VARCHAR(160) NOT NULL,
  starts_on DATE NOT NULL,
  ends_on DATE NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  PRIMARY KEY(id),
  UNIQUE KEY uq_academic_year_tenant_campus_code(tenant_id,campus_id,code),
  UNIQUE KEY uq_academic_year_scope(tenant_id,campus_id,id),
  CONSTRAINT fk_academic_year_campus FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
  CONSTRAINT chk_academic_year_dates CHECK (starts_on <= ends_on)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- One hierarchy supports school grades/classes/sections and college programs/courses/batches,
-- with strict kind checks in Application services plus scoped parent FK enforcement in MySQL.
ALTER TABLE institution_departments
  ADD UNIQUE KEY uq_academic_department_scope (tenant_id,campus_id,id);

CREATE TABLE IF NOT EXISTS academic_catalog (
  id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  kind VARCHAR(20) NOT NULL,
  code VARCHAR(30) NOT NULL,
  display_name VARCHAR(160) NOT NULL,
  parent_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
  academic_year_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
  department_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
  starts_on DATE NULL,
  ends_on DATE NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  PRIMARY KEY(id),
  UNIQUE KEY uq_academic_catalog_code(tenant_id,campus_id,kind,code),
  UNIQUE KEY uq_academic_catalog_scope(tenant_id,campus_id,id),
  KEY ix_academic_catalog_kind(tenant_id,campus_id,kind,is_active),
  CONSTRAINT fk_catalog_campus FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
  CONSTRAINT fk_catalog_parent FOREIGN KEY(tenant_id,campus_id,parent_id)
    REFERENCES academic_catalog(tenant_id,campus_id,id),
  CONSTRAINT fk_catalog_year FOREIGN KEY(tenant_id,campus_id,academic_year_id)
    REFERENCES academic_years(tenant_id,campus_id,id),
  CONSTRAINT fk_catalog_department FOREIGN KEY(tenant_id,campus_id,department_id)
    REFERENCES institution_departments(tenant_id,campus_id,id),
  CONSTRAINT chk_catalog_kind CHECK(kind IN ('TERM','SEMESTER','GRADE','CLASS','SECTION','PROGRAM','COURSE','BATCH','SUBJECT')),
  CONSTRAINT chk_catalog_dates CHECK(starts_on IS NULL OR ends_on IS NULL OR starts_on<=ends_on)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS academic_faculty_assignments (
  id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  subject_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  academic_year_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  faculty_subject_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  UNIQUE KEY uq_faculty_assignment(tenant_id,campus_id,subject_id,academic_year_id,faculty_subject_id),
  KEY ix_faculty_member(tenant_id,faculty_subject_id),
  CONSTRAINT fk_assignment_subject FOREIGN KEY(tenant_id,campus_id,subject_id)
    REFERENCES academic_catalog(tenant_id,campus_id,id),
  CONSTRAINT fk_assignment_year FOREIGN KEY(tenant_id,campus_id,academic_year_id)
    REFERENCES academic_years(tenant_id,campus_id,id),
  CONSTRAINT fk_assignment_member FOREIGN KEY(tenant_id,faculty_subject_id)
    REFERENCES tenant_memberships(tenant_id,subject_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS academic_calendar_events (
  id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  academic_year_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  kind VARCHAR(16) NOT NULL,
  title VARCHAR(160) NOT NULL,
  starts_on DATE NOT NULL,
  ends_on DATE NOT NULL,
  notes VARCHAR(500) NULL,
  KEY ix_calendar_dates(tenant_id,campus_id,starts_on,ends_on),
  CONSTRAINT fk_calendar_year FOREIGN KEY(tenant_id,campus_id,academic_year_id)
    REFERENCES academic_years(tenant_id,campus_id,id),
  CONSTRAINT chk_calendar_kind CHECK(kind IN ('HOLIDAY','EVENT','EXAM','ACADEMIC')),
  CONSTRAINT chk_calendar_dates CHECK(starts_on<=ends_on)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Existing Phase 6 department schema needs this scoped unique index for composite FKs.
-- Applied BEFORE the catalog table on a new environment; see separate ALTER below.
INSERT INTO schema_migrations(version) VALUES ('005_academic_management')
ON DUPLICATE KEY UPDATE version=VALUES(version);
