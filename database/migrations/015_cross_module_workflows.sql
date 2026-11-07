-- Phase 17: transactional integration journal and procurement approval bridge.
-- Apply after 014. Existing integration_outbox (003) is reused.
CREATE TABLE workflow_operations (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 operation_code VARCHAR(48) NOT NULL,
 idempotency_key VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 request_sha256 BINARY(32) NOT NULL,
 employee_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 purchase_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 actor_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_workflow_operation(tenant_id,operation_code,idempotency_key),
 KEY ix_workflow_campus(tenant_id,campus_id,created_utc),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 FOREIGN KEY(tenant_id,campus_id,employee_id) REFERENCES hr_employees(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,purchase_id) REFERENCES ams_purchase_orders(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,actor_id) REFERENCES tenant_memberships(tenant_id,subject_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE workflow_purchase_finance (
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 purchase_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 status VARCHAR(16) NOT NULL DEFAULT 'PENDING',
 requested_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 reviewed_by VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL,
 reviewed_utc DATETIME(6) NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 PRIMARY KEY(tenant_id,campus_id,purchase_id),
 FOREIGN KEY(tenant_id,campus_id,purchase_id) REFERENCES ams_purchase_orders(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,requested_by) REFERENCES tenant_memberships(tenant_id,subject_id),
 FOREIGN KEY(tenant_id,reviewed_by) REFERENCES tenant_memberships(tenant_id,subject_id),
 CHECK(status IN ('PENDING','APPROVED','REJECTED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE workflow_event_receipts (
 event_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 event_type VARCHAR(90) NOT NULL,
 received_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 KEY ix_workflow_receipt_tenant(tenant_id,received_utc),
 FOREIGN KEY(event_id) REFERENCES integration_outbox(id),
 FOREIGN KEY(tenant_id) REFERENCES tenants(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO schema_migrations(version) VALUES ('015_cross_module_workflows')
ON DUPLICATE KEY UPDATE version=VALUES(version);
