# Changelog

## 0.2.0 — 2026-09-04

- Single tracked price area (was multi-select) — switching regions now removes the old one's HA
  sensor instead of leaving it stale.
- VAT clarity on the price card: every line item labeled excl./incl. VAT, an explicit VAT amount
  row, and a "Total (incl. VAT)" row, instead of a bare "included" flag.
- Numbers are now formatted using the client browser's own locale (decimal comma vs. point, etc.),
  auto-detected once via a `/culture/set` redirect; table numbers and the markup field use 4
  decimals.
- HA consumption device integration: pick power/energy sensor entities in Settings (polled via Home
  Assistant's own REST API, not MQTT) and see their hourly + current-hour DKK cost.
- Cheapest continuous-use period sensors (`wattsup_cheapest_start_1h`..`_6h`) and a Dashboard card,
  computed over already-cached price data.
- Dashboard "Trends" card: hour-over-hour trend, today's min/max range, and 7-day comparisons.
- Smarter reduced elafgift: distributes the electric-heating threshold as one blended rate per
  completed day (using Eloverblik's secondary "elvarme" metering point when configured, else an
  estimate from year-to-date consumption) instead of a hard cutover mid-day.
- Theming now matches the sibling `timetracker` app: HA's default Material blue, persisted
  dark/light mode (falls back to the browser's `prefers-color-scheme`), responsive drawer.
- Fix: the hourly price chart's Y-axis defaulted to a fixed step of 20 regardless of the data's
  actual (much smaller) range, squashing the line into an unreadable sliver; the x-axis also
  crammed all 48 hourly labels in with no thinning, overlapping into illegible text.
- Investigated and intentionally skipped: a suppliers list (no such data available from the APIs
  already in use) and metering-point-driven supplier/grid-company prefill (Eloverblik's Customer
  API doesn't expose that identity data) — see `BACKLOG.md` items 11–12.

## 0.1.2 — 2026-09-03

- Fix: the grid-company picker only ever fetched a single unsorted 50,000-row page from
  DatahubPricelist, which — since each company has hundreds of historical rows — surfaced only 7
  of the ~69 real distinct grid companies (Radius Elnet among the missing). Now pages through the
  full dataset (sorted, ~440k rows) accumulating every distinct company.

## 0.1.1 — 2026-09-03

- Fix: real ingress requests (the full `/api/hassio_ingress/<token>/...` path Supervisor actually
  sends) 404'd entirely — `Program.cs` never called `UseRouting()` explicitly, so endpoint
  matching ran before the ingress PathBase rewrite and always matched against the raw path.
- Fix: the MudBlazor stylesheet was linked at the wrong asset path, so component styling never
  loaded.
- Fix: the ingress remote-IP allowlist compared against a plain IPv4 address, but Kestrel's
  dual-stack listener presents real peers as IPv4-mapped IPv6 — every ingress request was
  silently rejected. Normalized before comparing, and rejections are now logged.
- MQTT moved off the legacy `MQTTnet.Extensions.ManagedClient` (no 5.x-compatible release exists)
  onto plain MQTTnet 5.x with a self-managed reconnect loop, and now honors the Supervisor-
  reported broker's TLS flag.
- Repository restructured to match a standard HA add-on repository layout (`repository.yaml`, the
  add-on under `wattsup/`, a devcontainer, and CI that publishes multi-arch images on release
  tags) instead of living at the repo root.
- Added `icon.png`/`logo.png`.

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
