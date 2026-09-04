-- v0.2.5 fixed a bug where EnergiDataService timestamps were silently mis-parsed by up to 2 hours
-- in containers whose system time zone isn't UTC. Rows written before that fix have the OLD
-- (wrong-offset) string baked directly into their primary key (spot_prices.time_utc,
-- tariff_line_items.valid_from), so a post-upgrade poll can't recognize them as stale — it just
-- inserts new, correctly-parsed rows alongside the old wrong ones instead of replacing them.
-- That leftover duplication corrupted every time-range query: wrong "current price" selection,
-- a garbled/crisscrossing chart, and cheapest-period windows failing to find a contiguous run.
-- Both tables are pure caches, repopulated automatically by their pollers within moments of
-- startup — clearing them outright is simpler and safer than trying to reconcile old rows in
-- place (which would require knowing exactly which DST offset was wrongly applied to each one).
DELETE FROM spot_prices;
DELETE FROM tariff_line_items;

INSERT INTO schema_version (version, applied_at) VALUES (3, datetime('now'));
