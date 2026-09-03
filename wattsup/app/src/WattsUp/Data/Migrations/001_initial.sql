-- WattsUp initial schema.

CREATE TABLE IF NOT EXISTS schema_version (
    version     INTEGER NOT NULL PRIMARY KEY,
    applied_at  TEXT NOT NULL
);

-- Singleton row (id = 1). Non-secret operational settings edited live from the Settings page.
CREATE TABLE IF NOT EXISTS app_settings (
    id                                      INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
    price_areas_json                        TEXT NOT NULL DEFAULT '["DK1"]',
    grid_company_gln                        TEXT NULL,
    grid_company_name                       TEXT NULL,
    electric_heating_registered             INTEGER NOT NULL DEFAULT 0,
    vat_enabled                             INTEGER NOT NULL DEFAULT 1,
    supplier_markup_ore_per_kwh             REAL NOT NULL DEFAULT 0,
    supplier_subscription_fee_dkk_month     REAL NOT NULL DEFAULT 0,
    reduced_elafgift_rate_dkk_per_kwh       REAL NOT NULL DEFAULT 0.008,
    selected_metering_point_gsrn            TEXT NULL
);

INSERT OR IGNORE INTO app_settings (id) VALUES (1);

-- Day-ahead spot prices (EnergiDataService DayAheadPrices dataset).
CREATE TABLE IF NOT EXISTS spot_prices (
    price_area      TEXT NOT NULL,
    time_utc        TEXT NOT NULL,
    time_dk         TEXT NOT NULL,
    price_dkk_per_kwh REAL NOT NULL,
    PRIMARY KEY (price_area, time_utc)
);

CREATE INDEX IF NOT EXISTS idx_spot_prices_time ON spot_prices (time_utc);

-- Grid tariff + nationwide charge line items (EnergiDataService DatahubPricelist dataset).
-- Hourly prices (when resolution_duration = 'PT1H') are stored as a JSON array of 24 values in
-- prices_json; flat daily prices ('P1D') are stored as a single-element array.
CREATE TABLE IF NOT EXISTS tariff_line_items (
    gln_number              TEXT NOT NULL,
    charge_type_code        TEXT NOT NULL,
    valid_from               TEXT NOT NULL,
    valid_to                 TEXT NULL,
    charge_owner             TEXT NOT NULL,
    note                     TEXT NULL,
    description              TEXT NULL,
    vat_class                TEXT NULL,
    resolution_duration      TEXT NOT NULL,
    prices_json               TEXT NOT NULL,
    charge_classification    TEXT NOT NULL DEFAULT 'unknown', -- per_kwh | subscription | unknown
    transparent_invoicing    INTEGER NOT NULL DEFAULT 0,
    tax_indicator             INTEGER NOT NULL DEFAULT 0,
    fetched_at                TEXT NOT NULL,
    PRIMARY KEY (gln_number, charge_type_code, valid_from)
);

CREATE INDEX IF NOT EXISTS idx_tariff_line_items_gln ON tariff_line_items (gln_number);

-- Seed rows for the confirmed nationwide charges, keyed by a stable logical name. Re-resolved
-- from DatahubPricelist by TariffPollingService on every poll; this table is only the seed/fallback.
CREATE TABLE IF NOT EXISTS nationwide_charge_seed (
    charge_key                   TEXT NOT NULL PRIMARY KEY, -- system_tariff | transmission_tariff | elafgift
    gln_number                   TEXT NOT NULL,
    charge_type_code             TEXT NOT NULL,
    note                          TEXT NOT NULL,
    fallback_rate_dkk_per_kwh    REAL NOT NULL
);

INSERT OR IGNORE INTO nationwide_charge_seed (charge_key, gln_number, charge_type_code, note, fallback_rate_dkk_per_kwh)
VALUES
    ('system_tariff',       '5790000432752', '41000',  'Systemtarif',              0.072),
    ('transmission_tariff', '5790000432752', '40000',  'Transmissions nettarif',   0.043),
    ('elafgift',            '5790000432752', 'EA-001', 'Elafgift',                 0.008);

-- Eloverblik metering points for the authenticated customer.
CREATE TABLE IF NOT EXISTS metering_points (
    gsrn            TEXT NOT NULL PRIMARY KEY,
    type_of_mp      TEXT NULL,
    address         TEXT NULL,
    is_selected     INTEGER NOT NULL DEFAULT 0
);

-- Daily consumption, used for the electric-heating annual-threshold calculation.
CREATE TABLE IF NOT EXISTS consumption_readings (
    gsrn    TEXT NOT NULL,
    date    TEXT NOT NULL, -- yyyy-MM-dd
    kwh     REAL NOT NULL,
    PRIMARY KEY (gsrn, date)
);

CREATE INDEX IF NOT EXISTS idx_consumption_readings_gsrn_date ON consumption_readings (gsrn, date);

INSERT INTO schema_version (version, applied_at) VALUES (1, datetime('now'));
