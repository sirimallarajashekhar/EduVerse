-- Phase 10: apply after 001-007. MySQL 8, decimal currency only, no floating-point arithmetic.
CREATE TABLE IF NOT EXISTS finance_fee_structures (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 academic_year_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(30) NOT NULL, title VARCHAR(160) NOT NULL,
 amount DECIMAL(18,2) NOT NULL, currency CHAR(3) NOT NULL DEFAULT 'INR',
 is_active BOOLEAN NOT NULL DEFAULT TRUE,
 UNIQUE KEY uq_fee_code(tenant_id,campus_id,academic_year_id,code),
 UNIQUE KEY uq_fee_scope(tenant_id,campus_id,id),
 CONSTRAINT fk_fee_year FOREIGN KEY(tenant_id,campus_id,academic_year_id) REFERENCES academic_years(tenant_id,campus_id,id),
 CONSTRAINT chk_fee_amount CHECK(amount > 0),
 CONSTRAINT chk_fee_currency CHECK(currency='INR')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS finance_student_charges (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 enrollment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 fee_structure_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 gross_amount DECIMAL(18,2) NOT NULL,
 concession_amount DECIMAL(18,2) NOT NULL DEFAULT 0,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_charge_assignment(tenant_id,campus_id,enrollment_id,fee_structure_id),
 UNIQUE KEY uq_charge_scope(tenant_id,campus_id,id),
 CONSTRAINT fk_charge_student FOREIGN KEY(tenant_id,campus_id,student_id) REFERENCES students(tenant_id,campus_id,id),
 CONSTRAINT fk_charge_enrollment FOREIGN KEY(tenant_id,campus_id,enrollment_id) REFERENCES student_enrollments(tenant_id,campus_id,id),
 CONSTRAINT fk_charge_structure FOREIGN KEY(tenant_id,campus_id,fee_structure_id) REFERENCES finance_fee_structures(tenant_id,campus_id,id),
 CONSTRAINT chk_charge_amount CHECK(gross_amount > 0 AND concession_amount>=0 AND concession_amount<=gross_amount)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS finance_installments (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 charge_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 installment_number SMALLINT UNSIGNED NOT NULL,
 due_on DATE NOT NULL,
 amount DECIMAL(18,2) NOT NULL,
 UNIQUE KEY uq_charge_installment(tenant_id,campus_id,charge_id,installment_number),
 UNIQUE KEY uq_installment_scope(tenant_id,campus_id,id),
 CONSTRAINT fk_installment_charge FOREIGN KEY(tenant_id,campus_id,charge_id) REFERENCES finance_student_charges(tenant_id,campus_id,id),
 CONSTRAINT chk_installment_positive CHECK(amount>0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS finance_concessions (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 charge_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 category VARCHAR(20) NOT NULL, reason VARCHAR(240) NOT NULL,
 amount DECIMAL(18,2) NOT NULL,
 approved_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 CONSTRAINT fk_concession_charge FOREIGN KEY(tenant_id,campus_id,charge_id) REFERENCES finance_student_charges(tenant_id,campus_id,id),
 CONSTRAINT fk_concession_actor FOREIGN KEY(tenant_id,approved_by) REFERENCES tenant_memberships(tenant_id,subject_id),
 CONSTRAINT chk_concession_category CHECK(category IN ('SCHOLARSHIP','CONCESSION','DISCOUNT')),
 CONSTRAINT chk_concession_amount CHECK(amount>0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS finance_payments (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 charge_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 installment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 amount DECIMAL(18,2) NOT NULL,
 method VARCHAR(16) NOT NULL, external_reference VARCHAR(100) NULL,
 idempotency_key VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 request_sha256 BINARY(32) NOT NULL,
 receipt_number VARCHAR(45) NOT NULL,
 collected_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 collected_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 reconciliation_status VARCHAR(16) NOT NULL DEFAULT 'PENDING',
 reconciled_utc DATETIME(6) NULL,
 UNIQUE KEY uq_payment_key(tenant_id,idempotency_key),
 UNIQUE KEY uq_receipt_number(tenant_id,receipt_number),
 UNIQUE KEY uq_payment_scope(tenant_id,campus_id,id),
 KEY ix_payment_charge(tenant_id,campus_id,charge_id),
 CONSTRAINT fk_payment_installment FOREIGN KEY(tenant_id,campus_id,installment_id) REFERENCES finance_installments(tenant_id,campus_id,id),
 CONSTRAINT fk_payment_charge FOREIGN KEY(tenant_id,campus_id,charge_id) REFERENCES finance_student_charges(tenant_id,campus_id,id),
 CONSTRAINT fk_payment_actor FOREIGN KEY(tenant_id,collected_by) REFERENCES tenant_memberships(tenant_id,subject_id),
 CONSTRAINT chk_payment_amount CHECK(amount>0),
 CONSTRAINT chk_payment_method CHECK(method IN ('CASH','BANK_TRANSFER','CARD_MANUAL','UPI_MANUAL')),
 CONSTRAINT chk_reconcile_status CHECK(reconciliation_status IN ('PENDING','MATCHED','DISPUTED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS finance_refunds (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER ascii COLLATE ascii_bin NOT NULL,
 payment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 amount DECIMAL(18,2) NOT NULL, reason VARCHAR(240) NOT NULL,
 refund_reference VARCHAR(100) NOT NULL,
 idempotency_key VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 request_sha256 BINARY(32) NOT NULL,
 refunded_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 refunded_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_refund_key(tenant_id,idempotency_key),
 CONSTRAINT fk_refund_payment FOREIGN KEY(tenant_id,campus_id,payment_id) REFERENCES finance_payments(tenant_id,campus_id,id),
 CONSTRAINT fk_refund_actor FOREIGN KEY(tenant_id,refunded_by) REFERENCES tenant_memberships(tenant_id,subject_id),
 CONSTRAINT chk_refund_amount CHECK(amount>0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO schema_migrations(version) VALUES ('008_fees_finance')
ON DUPLICATE KEY UPDATE version=VALUES(version);
