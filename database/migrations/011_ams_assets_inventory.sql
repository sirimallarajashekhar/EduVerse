-- Phase 13. Apply after migrations 001-010. MySQL 8. All assets and stock are tenant+campus scoped.
CREATE TABLE ams_categories (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(30) NOT NULL, display_name VARCHAR(120) NOT NULL,
 UNIQUE KEY uq_ams_category_code(tenant_id,campus_id,code),
 UNIQUE KEY uq_ams_category_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE ams_vendors (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 display_name VARCHAR(160) NOT NULL, contact_email VARCHAR(254) NULL,
 UNIQUE KEY uq_ams_vendor_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE ams_assets (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 asset_code VARCHAR(42) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 category_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 display_name VARCHAR(160) NOT NULL,
 serial_number VARCHAR(100) NULL,
 status VARCHAR(20) NOT NULL DEFAULT 'AVAILABLE',
 building_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 department_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 employee_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 acquired_on DATE NULL,
 purchase_amount DECIMAL(18,2) NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_ams_asset_code(tenant_id,asset_code),
 UNIQUE KEY uq_ams_asset_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,category_id) REFERENCES ams_categories(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,building_id) REFERENCES institution_buildings(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,department_id) REFERENCES institution_departments(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,employee_id) REFERENCES hr_employees(tenant_id,campus_id,id),
 CHECK(status IN ('AVAILABLE','ALLOCATED','MAINTENANCE','RETIRED','DISPOSED')),
 CHECK(purchase_amount IS NULL OR purchase_amount>=0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

ALTER TABLE institution_buildings ADD UNIQUE KEY uq_ams_building_scope(tenant_id,campus_id,id);

CREATE TABLE ams_asset_events (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 asset_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 event_code VARCHAR(20) NOT NULL, note VARCHAR(300) NULL,
 actor_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 KEY ix_ams_asset_events(tenant_id,campus_id,asset_id,created_utc),
 FOREIGN KEY(tenant_id,campus_id,asset_id) REFERENCES ams_assets(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,actor_id) REFERENCES tenant_memberships(tenant_id,subject_id),
 CHECK(event_code IN ('REGISTERED','ALLOCATED','TRANSFERRED','RETURNED','MAINTENANCE','REPAIRED','RETIRED','DISPOSED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE ams_maintenance (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 asset_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 title VARCHAR(160) NOT NULL, due_on DATE NOT NULL,
 status VARCHAR(16) NOT NULL DEFAULT 'OPEN',
 completed_utc DATETIME(6) NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_ams_work_order_scope(tenant_id,campus_id,id),
 KEY ix_ams_maintenance_due(tenant_id,campus_id,status,due_on),
 FOREIGN KEY(tenant_id,campus_id,asset_id) REFERENCES ams_assets(tenant_id,campus_id,id),
 CHECK(status IN ('OPEN','COMPLETED','CANCELLED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE ams_stock_items (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 sku VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 display_name VARCHAR(160) NOT NULL, unit VARCHAR(16) NOT NULL,
 quantity DECIMAL(18,2) NOT NULL DEFAULT 0,
 reorder_level DECIMAL(18,2) NOT NULL DEFAULT 0,
 UNIQUE KEY uq_ams_sku(tenant_id,campus_id,sku),
 UNIQUE KEY uq_ams_stock_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 CHECK(quantity>=0 AND reorder_level>=0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE ams_stock_movements (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 stock_item_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 movement_type VARCHAR(8) NOT NULL, quantity DECIMAL(18,2) NOT NULL,
 balance_after DECIMAL(18,2) NOT NULL,
 reference_code VARCHAR(100) NULL,
 idempotency_key VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 request_sha256 BINARY(32) NOT NULL,
 actor_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_ams_stock_key(tenant_id,idempotency_key),
 KEY ix_ams_stock_ledger(tenant_id,campus_id,stock_item_id,created_utc),
 FOREIGN KEY(tenant_id,campus_id,stock_item_id) REFERENCES ams_stock_items(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,actor_id) REFERENCES tenant_memberships(tenant_id,subject_id),
 CHECK(movement_type IN ('IN','OUT','ADJUST')),
 CHECK(quantity>0 AND balance_after>=0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE ams_purchase_orders (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 vendor_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 stock_item_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 ordered_quantity DECIMAL(18,2) NOT NULL,
 unit_price DECIMAL(18,2) NOT NULL, total_price DECIMAL(18,2) NOT NULL,
 status VARCHAR(16) NOT NULL DEFAULT 'DRAFT',
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_ams_po_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,vendor_id) REFERENCES ams_vendors(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,stock_item_id) REFERENCES ams_stock_items(tenant_id,campus_id,id),
 CHECK(ordered_quantity>0 AND unit_price>=0 AND total_price>=0),
 CHECK(status IN ('DRAFT','ORDERED','CANCELLED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO schema_migrations(version) VALUES ('011_ams_assets_inventory')
ON DUPLICATE KEY UPDATE version=VALUES(version);
