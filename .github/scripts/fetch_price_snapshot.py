#!/usr/bin/env python3
"""
Fetches a static snapshot of api.energidataservice.dk's public data for the WASM/GitHub Pages
demo, which — unlike the Home Assistant add-on — has no server to poll the live API from: a
browser can't call it directly (it doesn't send CORS headers; confirmed against the real API, not
assumed). This script runs server-side in the GitHub Actions runner (a normal HTTP client, not a
browser, so CORS is irrelevant here) on a schedule, and its output is published as static JSON
files alongside the WASM app. See WattsUp.Wasm/Services/StaticSnapshotEnergiDataServiceClient.cs
for the client that reads them — same-origin fetches, no CORS problem.

It has two modes, run on different cadences by .github/workflows/build.yml:
  - prices:  one small request; run on every scheduled build, since spot prices change daily.
  - tariffs: pages through the whole ~440k-row DatahubPricelist dataset (~9 requests), which the
             API rate-limits (429) hard, especially from shared GitHub runner IPs. Tariffs change
             rarely, so the workflow caches this output and refetches it only about once a week.

Output files (written to --out-dir), each matching the shape IEnergiDataServiceClient's DTOs
already expect, so the client needs no bespoke parsing:
  prices:
  - day-ahead-prices.json   {"total": N, "records": [DayAheadPriceRecord, ...]}  (raw API passthrough)
  tariffs:
  - tariff-line-items.json  {"total": N, "records": [DatahubPricelistRecord, ...]} (not-yet-expired rows, all companies)
  - grid-companies.json     [{"GlnNumber": "...", "ChargeOwner": "..."}, ...]      (distinct pairs, custom shape)

No personal data is involved anywhere in this script — every field fetched is the same public,
keyless dataset the add-on itself already reads.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timedelta, timezone

API_BASE = "https://api.energidataservice.dk"
PRICE_AREAS = ["DK1", "DK2"]

# Full historical dataset is ~440k rows across ~69 companies (see EnergiDataServiceClient.cs) —
# page through it all once so filtering to current and upcoming rows is done ourselves, not relying on any
# unverified server-side date-range filter capability.
PAGE_SIZE = 50_000
MAX_PAGES = 20
# EnergiDataServiceClient.cs uses 2s between pages (GridCompanyPageDelay), but from GitHub's shared
# runner IPs that still got 429 on page 2 every run for weeks. This job runs about once a week
# now, so it can afford to be slow and polite.
PAGE_DELAY_SECONDS = 10
RETRIES = 6
BACKOFF_BASE_SECONDS = 15
MAX_BACKOFF_SECONDS = 180
# How far ahead upcoming tariff rows are kept. The workflow refetches weekly, so a week is the
# minimum; the extra covers a couple of failed refetches. Grid companies publish many rows months
# ahead (a year's worth made the file ~11MB instead of ~2MB), and nothing needs them this early.
UPCOMING_HORIZON_DAYS = 21


def retry_delay(ex: urllib.error.HTTPError, attempt: int) -> float:
    """Honours the server's Retry-After (in seconds) when present, else exponential backoff."""
    retry_after = ex.headers.get("Retry-After") if ex.headers else None
    if retry_after and retry_after.strip().isdigit():
        return min(int(retry_after.strip()), MAX_BACKOFF_SECONDS)
    return min(BACKOFF_BASE_SECONDS * (2 ** attempt), MAX_BACKOFF_SECONDS)


def fetch_json(url: str) -> dict:
    for attempt in range(RETRIES):
        try:
            with urllib.request.urlopen(url, timeout=60) as response:
                return json.load(response)
        except urllib.error.HTTPError as ex:
            if ex.code in (429, 503) and attempt < RETRIES - 1:
                delay = retry_delay(ex, attempt)
                print(f"  HTTP {ex.code}, retrying in {delay}s (attempt {attempt + 2}/{RETRIES})...", file=sys.stderr)
                time.sleep(delay)
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


def is_current_or_upcoming(row: dict, cutoff: datetime, horizon: datetime) -> bool:
    """Keeps rows still valid at `cutoff` or starting before `horizon`, not just today's. The app
    picks the right row per date itself (TariffLineItem.CoversDate), so a snapshot built this way
    stays correct while the workflow's cache keeps it, and announced changes take effect on time."""
    valid_from = parse_iso(row.get("ValidFrom"))
    if valid_from is None or valid_from > horizon:
        return False
    valid_to = parse_iso(row.get("ValidTo"))
    return valid_to is None or valid_to >= cutoff


def write_prices(out_dir: str) -> None:
    print("Fetching day-ahead spot prices (DK1 + DK2, last 7 days .. next 2 days)...", file=sys.stderr)
    day_ahead = fetch_day_ahead_prices()
    with open(os.path.join(out_dir, "day-ahead-prices.json"), "w", encoding="utf-8") as f:
        json.dump(day_ahead, f)
    print(f"  wrote {len(day_ahead.get('records', []))} spot-price records", file=sys.stderr)


def write_tariffs(out_dir: str) -> None:
    print("Fetching the full DatahubPricelist dataset (paginated)...", file=sys.stderr)
    all_rows = fetch_all_datahub_pricelist_rows()

    # A day of slack: ValidTo is Danish local midnight, i.e. 22:00/23:00 UTC the day before.
    now = datetime.now(timezone.utc)
    cutoff = now - timedelta(days=1)
    horizon = now + timedelta(days=UPCOMING_HORIZON_DAYS)
    kept_rows = [r for r in all_rows if is_current_or_upcoming(r, cutoff, horizon)]
    with open(os.path.join(out_dir, "tariff-line-items.json"), "w", encoding="utf-8") as f:
        json.dump({"total": len(kept_rows), "records": kept_rows}, f)
    print(f"  {len(kept_rows)} of {len(all_rows)} rows are current or upcoming -> tariff-line-items.json", file=sys.stderr)

    distinct_companies = sorted(
        {(r.get("GLN_Number", ""), r.get("ChargeOwner", "")) for r in all_rows if r.get("GLN_Number") and r.get("ChargeOwner")},
        key=lambda pair: pair[1].lower(),
    )
    companies_payload = [{"GlnNumber": gln, "ChargeOwner": owner} for gln, owner in distinct_companies]
    with open(os.path.join(out_dir, "grid-companies.json"), "w", encoding="utf-8") as f:
        json.dump(companies_payload, f)
    print(f"  wrote {len(companies_payload)} distinct grid companies -> grid-companies.json", file=sys.stderr)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("mode", choices=["prices", "tariffs"], help="Which snapshot to fetch")
    parser.add_argument("--out-dir", required=True, help="Directory to write the snapshot JSON files into")
    args = parser.parse_args()

    os.makedirs(args.out_dir, exist_ok=True)
    if args.mode == "prices":
        write_prices(args.out_dir)
    else:
        write_tariffs(args.out_dir)


if __name__ == "__main__":
    try:
        main()
    except urllib.error.URLError as ex:
        print(f"Failed to fetch from {API_BASE}: {ex}", file=sys.stderr)
        sys.exit(1)
