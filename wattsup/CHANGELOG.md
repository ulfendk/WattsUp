# Changelog

## 0.1.0 — 2026-09-03

First iteration.

- Fetch DK1/DK2 day-ahead spot prices from EnergiDataService (`DayAheadPrices`).
- Resolve grid tariffs for a user-selected grid company and the nationwide system tariff,
  transmission tariff, and elafgift from EnergiDataService (`DatahubPricelist`).
- Electric-heating reduced elafgift, gated on annual consumption pulled from Eloverblik.
- Publish the resulting actual price per tracked price area to Home Assistant over MQTT, with HA
  MQTT Discovery, availability/LWT, and a diagnostics sensor.
- MudBlazor web UI (via ingress): live Dashboard, Settings (grid company, price areas, electric
  heating, VAT, supplier markup/subscription, Eloverblik metering point), Diagnostics.
- SQLite-backed settings and caches; secrets configured via the add-on's own options.
- Price predictions (Carnot.dk) deliberately deferred — see `IPricePredictionProvider`.
