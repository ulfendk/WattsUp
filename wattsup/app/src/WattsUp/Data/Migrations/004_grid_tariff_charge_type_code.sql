-- Grid companies publish one per-kWh tariff row per connection class (A høj, A lav, B høj, B lav,
-- C, ...) in parallel, and the price calculation used to add ALL of them up — several times too
-- high for a household. It now uses only the row with this charge type code. "DT_C_01" (Nettarif C)
-- is the household tariff; existing installs default to it and can change it on the Settings page.
ALTER TABLE app_settings ADD COLUMN grid_tariff_charge_type_code TEXT NOT NULL DEFAULT 'DT_C_01';

INSERT INTO schema_version (version, applied_at) VALUES (4, datetime('now'));
