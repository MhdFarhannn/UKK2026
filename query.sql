CREATE TABLE roles (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    nama VARCHAR(30) NOT NULL UNIQUE
) ENGINE=InnoDB;

CREATE TABLE status_barang (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    nama VARCHAR(30) NOT NULL UNIQUE
) ENGINE=InnoDB;

CREATE TABLE status_peminjaman (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    nama VARCHAR(30) NOT NULL UNIQUE
) ENGINE=InnoDB;

CREATE TABLE status_kondisi_kembali (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    nama VARCHAR(30) NOT NULL UNIQUE
) ENGINE=InnoDB;

CREATE TABLE status_ketersediaan (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    nama VARCHAR(30) NOT NULL UNIQUE
) ENGINE=InnoDB;

CREATE TABLE users (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    id_role INT UNSIGNED NOT NULL,
    username VARCHAR(50) NOT NULL,
    nama VARCHAR(100) NOT NULL,
    password VARCHAR(255) NOT NULL,
    no_telepon VARCHAR(20) NOT NULL,
    alamat TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    deleted_at TIMESTAMP NULL DEFAULT NULL,

    CONSTRAINT uq_users_username
        UNIQUE (username),

    CONSTRAINT uq_users_no_telepon
        UNIQUE (no_telepon),

    CONSTRAINT fk_users_roles
        FOREIGN KEY (id_role)
        REFERENCES roles(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE kategori (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    nama VARCHAR(100) NOT NULL UNIQUE,
    deleted_at TIMESTAMP NULL DEFAULT NULL
) ENGINE=InnoDB;

CREATE TABLE alat (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    id_kategori INT UNSIGNED NOT NULL,
    nama VARCHAR(150) NOT NULL,
    deskripsi TEXT,
    harga_sewa DECIMAL(15,2) UNSIGNED NOT NULL DEFAULT 0,
    image_path VARCHAR(255),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    deleted_at TIMESTAMP NULL DEFAULT NULL,

    CONSTRAINT fk_alat_kategori
        FOREIGN KEY (id_kategori)
        REFERENCES kategori(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE unit_alat (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    id_alat INT UNSIGNED NOT NULL,
    id_status_ketersediaan INT UNSIGNED NOT NULL,
    kode_inventaris VARCHAR(50) NOT NULL UNIQUE,
    id_status_barang INT UNSIGNED NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    deleted_at TIMESTAMP NULL DEFAULT NULL,

    INDEX idx_unit_alat_ketersediaan (id_alat, id_status_ketersediaan),

    CONSTRAINT fk_unit_alat_alat
        FOREIGN KEY (id_alat)
        REFERENCES alat(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT fk_unit_alat_status_barang
        FOREIGN KEY (id_status_barang)
        REFERENCES status_barang(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT fk_unit_alat_status_ketersediaan
        FOREIGN KEY (id_status_ketersediaan)
        REFERENCES status_ketersediaan(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE peminjaman (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    id_peminjam INT UNSIGNED NOT NULL,
    id_petugas INT UNSIGNED NULL,
    id_status_peminjaman INT UNSIGNED NOT NULL,
    tanggal_peminjaman DATE NOT NULL,
    tanggal_rencana_kembali DATE NOT NULL,
    total_biaya DECIMAL(15,2) UNSIGNED NOT NULL DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    deleted_at TIMESTAMP NULL DEFAULT NULL,

    CONSTRAINT fk_peminjaman_peminjam
        FOREIGN KEY (id_peminjam)
        REFERENCES users(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT fk_peminjaman_petugas
        FOREIGN KEY (id_petugas)
        REFERENCES users(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT fk_peminjaman_status
        FOREIGN KEY (id_status_peminjaman)
        REFERENCES status_peminjaman(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT chk_tanggal_peminjaman
        CHECK (tanggal_rencana_kembali >= tanggal_peminjaman)
) ENGINE=InnoDB;

CREATE TABLE detail_peminjaman (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    id_peminjaman INT UNSIGNED NOT NULL,
    id_alat INT UNSIGNED NOT NULL,
    jumlah INT UNSIGNED NOT NULL,
    harga_sewa_saat_transaksi DECIMAL(15,2) UNSIGNED NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT uq_detail_peminjaman_alat
        UNIQUE (id_peminjaman, id_alat),

    CONSTRAINT fk_detail_peminjaman_peminjaman
        FOREIGN KEY (id_peminjaman)
        REFERENCES peminjaman(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT fk_detail_peminjaman_alat
        FOREIGN KEY (id_alat)
        REFERENCES alat(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT chk_jumlah_peminjaman
        CHECK (jumlah > 0)
) ENGINE=InnoDB;

CREATE TABLE detail_unit_peminjaman (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    id_detail_peminjaman INT UNSIGNED NOT NULL,
    id_unit_alat INT UNSIGNED NOT NULL,

    CONSTRAINT fk_detail_unit_peminjaman_detail
        FOREIGN KEY (id_detail_peminjaman)
        REFERENCES detail_peminjaman(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT fk_detail_unit_peminjaman_unit
        FOREIGN KEY (id_unit_alat)
        REFERENCES unit_alat(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT uq_detail_unit_peminjaman
        UNIQUE (id_detail_peminjaman, id_unit_alat)
) ENGINE=InnoDB;

CREATE TABLE pengembalian (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    id_peminjaman INT UNSIGNED NOT NULL UNIQUE,
    id_petugas INT UNSIGNED NULL,
    tanggal_kembali DATE NOT NULL,
    keterlambatan_pengembalian INT UNSIGNED NOT NULL DEFAULT 0,
    denda DECIMAL(15,2) UNSIGNED NOT NULL DEFAULT 0,
    catatan TEXT,
    deleted_at TIMESTAMP NULL DEFAULT NULL,

    CONSTRAINT fk_pengembalian_peminjaman
        FOREIGN KEY (id_peminjaman)
        REFERENCES peminjaman(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT fk_pengembalian_petugas
        FOREIGN KEY (id_petugas)
        REFERENCES users(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE detail_pengembalian (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    id_pengembalian INT UNSIGNED NOT NULL,
    id_unit_alat INT UNSIGNED NOT NULL,
    id_status_kondisi_kembali INT UNSIGNED NOT NULL,
    denda_kondisi DECIMAL(15,2) UNSIGNED NOT NULL DEFAULT 0,
    catatan TEXT,

    CONSTRAINT fk_detail_pengembalian_pengembalian
        FOREIGN KEY (id_pengembalian)
        REFERENCES pengembalian(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT fk_detail_pengembalian_unit
        FOREIGN KEY (id_unit_alat)
        REFERENCES unit_alat(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT fk_detail_pengembalian_kondisi
        FOREIGN KEY (id_status_kondisi_kembali)
        REFERENCES status_kondisi_kembali(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT uq_detail_pengembalian_unit
        UNIQUE (id_pengembalian, id_unit_alat)
) ENGINE=InnoDB;

CREATE TABLE log_aktivitas (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    id_user INT UNSIGNED NULL,
    aktivitas TEXT NOT NULL,
    waktu TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_log_aktivitas_user
        FOREIGN KEY (id_user)
        REFERENCES users(id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT
) ENGINE=InnoDB;

INSERT INTO roles (nama) VALUES
('Admin'),
('Petugas'),
('Peminjam');

INSERT INTO status_barang (nama) VALUES
('baik'),
('rusak_ringan'),
('rusak_berat');

INSERT INTO status_peminjaman (nama) VALUES
('diajukan'),
('disetujui'),
('ditolak'),
('dipinjam'),
('selesai');

INSERT INTO status_kondisi_kembali (nama) VALUES
('baik'),
('rusak_ringan'),
('rusak_berat'),
('hilang');

INSERT INTO status_ketersediaan (nama) VALUES
('tersedia'),
('dipinjam'),
('tidak_tersedia');