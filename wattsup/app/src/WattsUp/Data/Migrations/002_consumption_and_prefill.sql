-- Backlog items 4, 5, 12, 13: single-region support, HA consumption device cost tracking,
-- metering-point-driven prefill tracking, and per-day elafgift allowance.

-- Item 5: collapse the price-area selection (previously a JSON array, multi-selectable) down to
-- a single region. Carry over the first previously-selected area so existing installs don't lose
-- their tracked region.
ALTER TABLE app_settings ADD COLUMN price_area TEXT NOT NULL DEFAULT 'DK1';
UPDATE app_settings
SET price_area = COALESCE(json_extract(price_areas_json, '$[0]'), 'DK1')
WHERE price_areas_json IS NOT NULL;

-- Item 4: HA power/energy consumption devices selected for cost tracking.
CREATE TABLE IF NOT EXISTS consumption_devices (
    entity_id       TEXT NOT NULL PRIMARY KEY, -- HA entity_id, e.g. sensor.ev_charger_energy
    friendly_name   TEXT NULL,
    unit_of_measure TEXT NULL,                 -- W, kW, kWh — determines the hourly-cost integration method
    device_class    TEXT NULL,                 -- HA device_class attribute, e.g. power | energy
    is_selected     INTEGER NOT NULL DEFAULT 0
);

-- Hourly energy (kWh) attributed to each selected device — distinct from Eloverblik's
-- household-level consumption_readings, which only covers the whole metering point.
CREATE TABLE IF NOT EXISTS device_hourly_consumption (
    entity_id   TEXT NOT NULL,
    hour_utc    TEXT NOT NULL, -- ISO-8601, truncated to the hour
    kwh         REAL NOT NULL,
    PRIMARY KEY (entity_id, hour_utc)
);

CREATE INDEX IF NOT EXISTS idx_device_hourly_consumption_hour ON device_hourly_consumption (hour_utc);

-- Item 12: track whether grid company / supplier were auto-filled from the metering point or
-- entered manually, so the UI knows whether to render those fields read-only.
ALTER TABLE app_settings ADD COLUMN grid_company_source TEXT NOT NULL DEFAULT 'manual'; -- manual | metering_point
ALTER TABLE app_settings ADD COLUMN supplier_source TEXT NOT NULL DEFAULT 'manual';     -- manual | metering_point

-- Item 13: the (optional) secondary "elvarme" metering point whose daily "consumption" figure is
-- actually Eloverblik's distributed elafgift allowance for that day. Chosen from the same
-- includeAll=true metering-point list as the main household one.
ALTER TABLE app_settings ADD COLUMN selected_elafgift_allowance_gsrn TEXT NULL;

-- Item 13: per-day elafgift allowance, sourced from Eloverblik's secondary "elvarme" metering
-- point when reachable, else locally computed as a fallback (see TariffResolutionService).
CREATE TABLE IF NOT EXISTS elafgift_daily_allowance (
    gsrn            TEXT NOT NULL,
    date            TEXT NOT NULL, -- yyyy-MM-dd
    kwh_allowance   REAL NOT NULL,
    source          TEXT NOT NULL, -- eloverblik_secondary_mp | computed_average
    PRIMARY KEY (gsrn, date)
);

INSERT INTO schema_version (version, applied_at) VALUES (2, datetime('now'));
