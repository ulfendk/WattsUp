// IndexedDB-backed storage for the public WASM demo, replacing the add-on build's SQLite database
// (Microsoft.Data.Sqlite is a native library WASM can't load). One database, mirroring the tables
// in WattsUp/Data/Migrations/*.sql; composite keys are plain "part1::part2" strings so a single
// primary-key range query covers every access pattern the C# repositories need (e.g.
// "gsrn::2026-01-01" .. "gsrn::2026-09-11" for a year-to-date sum) — no secondary indexes needed.
// Every date/time value stored here is a pre-normalized, UTC ISO-8601 string handed in by the C#
// side (see Data/Repositories/IndexedDb*Repository.cs), never a JS Date — string comparison alone
// has to sort them correctly, exactly like the SQL repositories' TEXT columns already rely on.
(function () {
    const DB_NAME = 'wattsup';
    const DB_VERSION = 1;
    let dbPromise = null;

    const NATIONWIDE_CHARGE_SEED = [
        { chargeKey: 'system_tariff', glnNumber: '5790000432752', chargeTypeCode: '41000', note: 'Systemtarif', fallbackRateDkkPerKwh: 0.072 },
        { chargeKey: 'transmission_tariff', glnNumber: '5790000432752', chargeTypeCode: '40000', note: 'Transmissions nettarif', fallbackRateDkkPerKwh: 0.043 },
        { chargeKey: 'elafgift', glnNumber: '5790000432752', chargeTypeCode: 'EA-001', note: 'Elafgift', fallbackRateDkkPerKwh: 0.008 },
    ];

    function openDb() {
        if (!dbPromise) {
            dbPromise = new Promise((resolve, reject) => {
                const request = indexedDB.open(DB_NAME, DB_VERSION);
                request.onupgradeneeded = (event) => {
                    const db = event.target.result;
                    if (!db.objectStoreNames.contains('spot_prices')) db.createObjectStore('spot_prices', { keyPath: 'key' });
                    if (!db.objectStoreNames.contains('tariff_line_items')) db.createObjectStore('tariff_line_items', { keyPath: 'key' });
                    if (!db.objectStoreNames.contains('nationwide_charge_seed')) db.createObjectStore('nationwide_charge_seed', { keyPath: 'chargeKey' });
                    if (!db.objectStoreNames.contains('metering_points')) db.createObjectStore('metering_points', { keyPath: 'gsrn' });
                    if (!db.objectStoreNames.contains('consumption_readings')) db.createObjectStore('consumption_readings', { keyPath: 'key' });
                    if (!db.objectStoreNames.contains('elafgift_daily_allowance')) db.createObjectStore('elafgift_daily_allowance', { keyPath: 'key' });
                    if (!db.objectStoreNames.contains('settings')) db.createObjectStore('settings', { keyPath: 'id' });
                };
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
        }
        return dbPromise;
    }

    // nationwide_charge_seed has no migration mechanism of its own here (unlike SQL's
    // INSERT OR IGNORE) — reseed the same 3 known-good rows on every open. put(), not add(): the
    // db already having them is the expected, common case, not an error.
    async function seedNationwideCharges() {
        const db = await openDb();
        await new Promise((resolve, reject) => {
            const tx = db.transaction('nationwide_charge_seed', 'readwrite');
            const store = tx.objectStore('nationwide_charge_seed');
            for (const row of NATIONWIDE_CHARGE_SEED) store.put(row);
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    }

    function inTransaction(storeName, mode, work) {
        return openDb().then((db) => new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, mode);
            const store = tx.objectStore(storeName);
            let result;
            Promise.resolve(work(store)).then((r) => { result = r; }).catch(reject);
            tx.oncomplete = () => resolve(result);
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error);
        }));
    }

    function reqToPromise(request) {
        return new Promise((resolve, reject) => {
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    function putAll(storeName, rows) {
        return inTransaction(storeName, 'readwrite', (store) => {
            for (const row of rows) store.put(row);
        });
    }

    function putOne(storeName, row) {
        return inTransaction(storeName, 'readwrite', (store) => store.put(row));
    }

    function getByKey(storeName, key) {
        return inTransaction(storeName, 'readonly', (store) => reqToPromise(store.get(key)))
            .then((r) => r ?? null);
    }

    function getAllRows(storeName) {
        return inTransaction(storeName, 'readonly', (store) => reqToPromise(store.getAll()));
    }

    // Inclusive on both ends — every prefix-range caller here (getRange, getByGln, sumRange) wants
    // "everything whose composite key starts with this prefix", achieved by bounding the upper end
    // with a trailing '￿' (sorts after any real suffix).
    function getRangeByKey(storeName, lowerKey, upperKey, upperInclusive) {
        const range = IDBKeyRange.bound(lowerKey, upperKey, false, !upperInclusive);
        return inTransaction(storeName, 'readonly', (store) => reqToPromise(store.getAll(range)));
    }

    // "Most recent period at or before asOfUtc" — walk the priceArea's key range backwards and
    // take the first hit, mirroring SpotPriceRepository.GetCurrentAsync's ORDER BY ... DESC LIMIT 1.
    function getCurrentSpotPrice(priceArea, asOfUtc) {
        const range = IDBKeyRange.bound(priceArea + '::', priceArea + '::' + asOfUtc);
        return inTransaction('spot_prices', 'readonly', (store) => new Promise((resolve, reject) => {
            const cursorRequest = store.openCursor(range, 'prev');
            cursorRequest.onsuccess = () => resolve(cursorRequest.result ? cursorRequest.result.value : null);
            cursorRequest.onerror = () => reject(cursorRequest.error);
        }));
    }

    function sumConsumptionKwh(gsrn, fromDate, toDate) {
        return getRangeByKey('consumption_readings', gsrn + '::' + fromDate, gsrn + '::' + toDate, true)
            .then((rows) => rows.reduce((sum, r) => sum + r.kwh, 0));
    }

    async function setSelectedMeteringPoint(gsrn) {
        await inTransaction('metering_points', 'readwrite', async (store) => {
            const all = await reqToPromise(store.getAll());
            for (const mp of all) {
                const shouldBeSelected = mp.gsrn === gsrn;
                if (mp.isSelected !== shouldBeSelected) {
                    store.put({ ...mp, isSelected: shouldBeSelected });
                }
            }
        });
    }

    window.wattsUpDb = {
        open: async () => {
            await openDb();
            await seedNationwideCharges();
        },

        spotPrices: {
            upsertMany: (prices) => putAll('spot_prices', prices.map((p) => ({ ...p, key: p.priceArea + '::' + p.timeUtc }))),
            getCurrent: (priceArea, asOfUtc) => getCurrentSpotPrice(priceArea, asOfUtc),
            getRange: (priceArea, fromUtc, toUtc) => getRangeByKey('spot_prices', priceArea + '::' + fromUtc, priceArea + '::' + toUtc, false),
        },

        tariffs: {
            upsertMany: (items) => putAll('tariff_line_items', items.map((i) => ({ ...i, key: i.glnNumber + '::' + i.chargeTypeCode + '::' + i.validFrom }))),
            getByGln: (glnNumber) => getRangeByKey('tariff_line_items', glnNumber + '::', glnNumber + '::￿', true),
        },

        nationwideSeed: {
            getAll: () => getAllRows('nationwide_charge_seed'),
        },

        meteringPoints: {
            upsertMany: (points) => putAll('metering_points', points),
            getAll: () => getAllRows('metering_points'),
            setSelected: (gsrn) => setSelectedMeteringPoint(gsrn),
        },

        consumption: {
            upsertMany: (readings) => putAll('consumption_readings', readings.map((r) => ({ ...r, key: r.gsrn + '::' + r.date }))),
            sumRange: (gsrn, fromDate, toDate) => sumConsumptionKwh(gsrn, fromDate, toDate),
            dailyKwh: (gsrn, date) => getByKey('consumption_readings', gsrn + '::' + date).then((r) => (r ? r.kwh : 0)),
        },

        elafgift: {
            upsertMany: (allowances) => putAll('elafgift_daily_allowance', allowances.map((a) => ({ ...a, key: a.gsrn + '::' + a.date }))),
            get: (gsrn, date) => getByKey('elafgift_daily_allowance', gsrn + '::' + date),
        },

        settings: {
            get: () => getByKey('settings', 1),
            save: (settings) => putOne('settings', { ...settings, id: 1 }),
        },
    };
})();
