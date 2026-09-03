# Changelog

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
