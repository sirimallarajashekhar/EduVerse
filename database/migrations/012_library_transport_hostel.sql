-- Phase 14: Library, Transport and Hostel operations. Apply after 001-011.
-- Every student, facility and movement uses tenant/campus foreign keys.
CREATE TABLE library_titles (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 isbn VARCHAR(20) NULL, title VARCHAR(180) NOT NULL, author VARCHAR(160) NOT NULL,
 UNIQUE KEY uq_library_title(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE library_copies (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 title_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 barcode VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 status VARCHAR(12) NOT NULL DEFAULT 'AVAILABLE',
 UNIQUE KEY uq_copy_barcode(tenant_id,campus_id,barcode),
 UNIQUE KEY uq_copy_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,title_id) REFERENCES library_titles(tenant_id,campus_id,id),
 CHECK(status IN ('AVAILABLE','LENT','LOST'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE library_loans (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 copy_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 borrowed_on DATE NOT NULL, due_on DATE NOT NULL, returned_on DATE NULL,
 fine_amount DECIMAL(12,2) NOT NULL DEFAULT 0,
 status VARCHAR(12) NOT NULL DEFAULT 'OPEN',
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_loan_scope(tenant_id,campus_id,id),
 KEY ix_library_overdue(tenant_id,campus_id,status,due_on),
 FOREIGN KEY(tenant_id,campus_id,copy_id) REFERENCES library_copies(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,student_id) REFERENCES students(tenant_id,campus_id,id),
 CHECK(status IN ('OPEN','RETURNED')),
 CHECK(due_on>=borrowed_on AND fine_amount>=0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE library_reservations (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 title_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 status VARCHAR(12) NOT NULL DEFAULT 'WAITING',
 created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_reservation_scope(tenant_id,campus_id,id),
 KEY ix_reservation_queue(tenant_id,campus_id,title_id,status,created_utc),
 FOREIGN KEY(tenant_id,campus_id,title_id) REFERENCES library_titles(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,student_id) REFERENCES students(tenant_id,campus_id,id),
 CHECK(status IN ('WAITING','FULFILLED','CANCELLED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE transport_vehicles (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 registration_number VARCHAR(32) NOT NULL, capacity SMALLINT UNSIGNED NOT NULL,
 driver_name VARCHAR(120) NOT NULL, driver_phone VARCHAR(30) NULL,
 UNIQUE KEY uq_vehicle_registration(tenant_id,registration_number),
 UNIQUE KEY uq_vehicle_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id),
 CHECK(capacity BETWEEN 1 AND 100)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE transport_routes (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(30) NOT NULL, name VARCHAR(120) NOT NULL,
 vehicle_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 UNIQUE KEY uq_route_code(tenant_id,campus_id,code),
 UNIQUE KEY uq_route_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,vehicle_id) REFERENCES transport_vehicles(tenant_id,campus_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE transport_stops (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 route_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 name VARCHAR(120) NOT NULL, stop_order SMALLINT UNSIGNED NOT NULL,
 UNIQUE KEY uq_route_stop_order(tenant_id,campus_id,route_id,stop_order),
 UNIQUE KEY uq_stop_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,route_id) REFERENCES transport_routes(tenant_id,campus_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE transport_assignments (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 route_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 stop_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 status VARCHAR(12) NOT NULL DEFAULT 'ACTIVE',
 assigned_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 released_utc DATETIME(6) NULL,
 active_student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin GENERATED ALWAYS AS
   (IF(status='ACTIVE',student_id,NULL)) STORED,
 UNIQUE KEY uq_transport_active_student(tenant_id,campus_id,active_student_id),
 UNIQUE KEY uq_transport_assignment_scope(tenant_id,campus_id,id),
 KEY ix_transport_route(tenant_id,campus_id,route_id,status),
 FOREIGN KEY(tenant_id,campus_id,student_id) REFERENCES students(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,route_id) REFERENCES transport_routes(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,stop_id) REFERENCES transport_stops(tenant_id,campus_id,id),
 CHECK(status IN ('ACTIVE','RELEASED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE hostel_blocks (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 code VARCHAR(30) NOT NULL, name VARCHAR(120) NOT NULL,
 UNIQUE KEY uq_hostel_block_code(tenant_id,campus_id,code),
 UNIQUE KEY uq_hostel_block_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id) REFERENCES campuses(tenant_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE hostel_rooms (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 block_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 room_number VARCHAR(30) NOT NULL,
 UNIQUE KEY uq_hostel_room(tenant_id,campus_id,block_id,room_number),
 UNIQUE KEY uq_hostel_room_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,block_id) REFERENCES hostel_blocks(tenant_id,campus_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE hostel_beds (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 room_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 bed_number VARCHAR(30) NOT NULL,
 UNIQUE KEY uq_hostel_bed(tenant_id,campus_id,room_id,bed_number),
 UNIQUE KEY uq_hostel_bed_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,room_id) REFERENCES hostel_rooms(tenant_id,campus_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE hostel_allocations (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 tenant_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 campus_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 bed_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 status VARCHAR(12) NOT NULL DEFAULT 'ACTIVE',
 assigned_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 vacated_utc DATETIME(6) NULL,
 active_student_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin GENERATED ALWAYS AS
  (IF(status='ACTIVE',student_id,NULL)) STORED,
 active_bed_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin GENERATED ALWAYS AS
  (IF(status='ACTIVE',bed_id,NULL)) STORED,
 UNIQUE KEY uq_hostel_active_student(tenant_id,campus_id,active_student_id),
 UNIQUE KEY uq_hostel_active_bed(tenant_id,campus_id,active_bed_id),
 UNIQUE KEY uq_hostel_allocation_scope(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,student_id) REFERENCES students(tenant_id,campus_id,id),
 FOREIGN KEY(tenant_id,campus_id,bed_id) REFERENCES hostel_beds(tenant_id,campus_id,id),
 CHECK(status IN ('ACTIVE','VACATED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT INTO schema_migrations(version) VALUES ('012_library_transport_hostel')
ON DUPLICATE KEY UPDATE version=VALUES(version);
