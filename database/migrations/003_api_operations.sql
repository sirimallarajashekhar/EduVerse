-- Phase 5: schema version 003; run after migrations 001 and 002.
-- Idempotency keys are private to tenant + identity + operation; payload digests avoid storing PII.
CREATE TABLE IF NOT EXISTS api_idempotency (
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  subject_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  operation_code VARCHAR(80) NOT NULL,
  idempotency_key VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  payload_sha256 BINARY(32) NOT NULL,
  result_entity_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (tenant_id, subject_id, operation_code, idempotency_key),
  KEY ix_idempotency_created (created_utc),
  CONSTRAINT fk_idempotency_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS integration_outbox (
  id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
  tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  type VARCHAR(90) NOT NULL,
  payload_json JSON NOT NULL,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  processed_utc DATETIME(6) NULL,
  next_attempt_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  attempts SMALLINT UNSIGNED NOT NULL DEFAULT 0,
  last_error VARCHAR(400) NULL,
  KEY ix_outbox_ready (processed_utc, next_attempt_utc, created_utc),
  KEY ix_outbox_tenant (tenant_id, created_utc),
  CONSTRAINT fk_outbox_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO schema_migrations(version) VALUES ('003_api_operations')
ON DUPLICATE KEY UPDATE version=VALUES(version);
