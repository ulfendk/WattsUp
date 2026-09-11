#!/usr/bin/env python3
"""
Fetches a static snapshot of api.energidataservice.dk's public data for the WASM/GitHub Pages
demo, which — unlike the Home Assistant add-on — has no server to poll the live API from: a
browser can't call it directly (it doesn't send CORS headers; confirmed against the real API, not
assumed). This script runs server-side in the GitHub Actions runner (a normal HTTP client, not a
browser, so CORS is irrelevant here) on a schedule, and its output is published as static JSON
files alongside the WASM app. See WattsUp.Wasm/Services/StaticSnapshotEnergiDataServiceClient.cs
for the client that reads them — same-origin fetches, no CORS problem.

Output files (written to --out-dir), each matching the shape IEnergiDataServiceClient's DTOs
already expect, so the client needs no bespoke parsing:
  - day-ahead-prices.json   {"total": N, "records": [DayAheadPriceRecord, ...]}  (raw API passthrough)
  - tariff-line-items.json  {"total": N, "records": [DatahubPricelistRecord, ...]} (today-valid rows only, all companies)
  - grid-companies.json     [{"GlnNumber": "...", "ChargeOwner": "..."}, ...]      (distinct pairs, custom shape)

No personal data is involved anywhere in this script — every field fetched is the same public,
keyless dataset the add-on itself already reads.
"""
from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timedelta, timezone

API_BASE = "https://api.energidataservice.dk"
PRICE_AREAS = ["DK1", "DK2"]

# Full historical dataset is ~440k rows across ~69 companies (see EnergiDataServiceClient.cs) —
# page through it all once so filtering to "valid today" is done ourselves, not relying on any
# unverified server-side date-range filter capability.
PAGE_SIZE = 50_000
MAX_PAGES = 20
# Same politeness delay EnergiDataServiceClient.cs already uses for this exact pagination pattern
# (GridCompanyPageDelay) — without it the API starts returning 429 Too Many Requests after a
# handful of consecutive 50k-row pages (confirmed empirically).
PAGE_DELAY_SECONDS = 2


def fetch_json(url: str, retries: int = 3) -> dict:
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(url, timeout=60) as response:
                return json.load(response)
        except urllib.error.HTTPError as ex:
            if ex.code == 429 and attempt < retries - 1:
                backoff = PAGE_DELAY_SECONDS * (2 ** (attempt + 1))
                print(f"  429 Too Many Requests, retrying in {backoff}s...", file=sys.stderr)
                time.sleep(backoff)
                continue
            raise
    raise RuntimeError("unreachable")


def fetch_day_ahead_prices() -> dict:
    now = datetime.now(timezone.utc)
    start = (now - timedelta(days=7)).strftime("%Y-%m-%dT00:00")
    end = (now + timedelta(days=2)).strftime("%Y-%m-%dT00:00")
    filter_param = json.dumps({"PriceArea": PRICE_AREAS})
    url = (
        f"{API_BASE}/dataset/DayAheadPrices"
        f"?start={urllib.parse.quote(start)}&end={urllib.parse.quote(end)}"
        f"&filter={urllib.parse.quote(filter_param)}&sort=TimeUTC%20ASC&limit=20000"
    )
    return fetch_json(url)


def fetch_all_datahub_pricelist_rows() -> list[dict]:
    rows: list[dict] = []
    for page in range(MAX_PAGES):
        offset = page * PAGE_SIZE
        url = f"{API_BASE}/dataset/DatahubPricelist?sort=GLN_Number&limit={PAGE_SIZE}&offset={offset}"
        envelope = fetch_json(url)
        page_rows = envelope.get("records", [])
        rows.extend(page_rows)
        print(f"  fetched page {page + 1}: {len(page_rows)} rows (total so far: {len(rows)})", file=sys.stderr)
        if len(page_rows) < PAGE_SIZE:
            break
        time.sleep(PAGE_DELAY_SECONDS)
    return rows


def parse_iso(value: str | None) -> datetime | None:
    """Parses one of the API's timestamps, assuming UTC when no offset is present — the API
    returns naive-looking timestamps that ARE UTC (see UtcDateTimeOffsetConverter.cs, which the
    C# side already has to do this same assume-UTC normalization for the identical reason)."""
    if not value:
        return None
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    return parsed if parsed.tzinfo is not None else parsed.replace(tzinfo=timezone.utc)


def is_valid_today(row: dict, today: datetime) -> bool:
    valid_from = parse_iso(row.get("ValidFrom"))
    valid_to = parse_iso(row.get("ValidTo"))
    if valid_from is None or valid_from > today:
        return False
    return valid_to is None or valid_to >= today


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out-dir", required=True, help="Directory to write the snapshot JSON files into")
    args = parser.parse_args()

    import os

    os.makedirs(args.out_dir, exist_ok=True)

    print("Fetching day-ahead spot prices (DK1 + DK2, last 7 days .. next 2 days)...", file=sys.stderr)
    day_ahead = fetch_day_ahead_prices()
    with open(os.path.join(args.out_dir, "day-ahead-prices.json"), "w", encoding="utf-8") as f:
        json.dump(day_ahead, f)
    print(f"  wrote {len(day_ahead.get('records', []))} spot-price records", file=sys.stderr)

    print("Fetching the full DatahubPricelist dataset (paginated)...", file=sys.stderr)
    all_rows = fetch_all_datahub_pricelist_rows()

    today = datetime.now(timezone.utc)
    valid_rows = [r for r in all_rows if is_valid_today(r, today)]
    with open(os.path.join(args.out_dir, "tariff-line-items.json"), "w", encoding="utf-8") as f:
        json.dump({"total": len(valid_rows), "records": valid_rows}, f)
    print(f"  {len(valid_rows)} of {len(all_rows)} rows are valid today -> tariff-line-items.json", file=sys.stderr)

    distinct_companies = sorted(
        {(r.get("GLN_Number", ""), r.get("ChargeOwner", "")) for r in all_rows if r.get("GLN_Number") and r.get("ChargeOwner")},
        key=lambda pair: pair[1].lower(),
    )
    companies_payload = [{"GlnNumber": gln, "ChargeOwner": owner} for gln, owner in distinct_companies]
    with open(os.path.join(args.out_dir, "grid-companies.json"), "w", encoding="utf-8") as f:
        json.dump(companies_payload, f)
    print(f"  wrote {len(companies_payload)} distinct grid companies -> grid-companies.json", file=sys.stderr)


if __name__ == "__main__":
    try:
        main()
    except urllib.error.URLError as ex:
        print(f"Failed to fetch from {API_BASE}: {ex}", file=sys.stderr)
        sys.exit(1)
