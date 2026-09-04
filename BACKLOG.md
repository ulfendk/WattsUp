This backlog does not take effect until the plan for implementation has successfully concluded.

## 1. Prediction engine

Build a prediction engine, which by looking at the weather forecast can predict electricity prices beyond what
is known. Whether this engine is based on an online or local method, depends on your findings - local is
preferred. The model should use historical data for electricity prices and weather forecasts (prices are
determined by forecasts, not actual weather, as the trades are done a day in advance).

Having a plan for how to keep the model up to date would be great as well.

The primary intent is to provide e.g. a 3 days forecast of prices - and when different types of activities, such
as EV charging, big laundry / dryer, etc. should be scheduled.

## 2. Add screenshots

~~Add screenshots to the Github README and in HA presentation.~~ Done — `wattsup/screenshots/`,
embedded in `README.md` and `wattsup/DOCS.md`.

~~The logo for this should be something with Watts going up.~~ Done — `wattsup/icon.png`/`logo.png`
(source: `wattsup/logo.svg`), a lightning bolt crossed with an upward price-trend arrow.

## 3. On the price card, make VAT clearer

~~The Price card needs to show all prices with a suffix of VAT excluded. The VAT line must then show the VAT and the total on top, should make it clear as well, that it is VAT inclusive. When entering markups, etc. state whether the price should be incl. or excl. VAT. Double check that VAT is handled correctly when fetching prices.~~
Done — `PriceBreakdown.VatAmountDkkPerKwh`, relabeled card (every line "(excl. VAT)", a real VAT
amount row, "Total (incl. VAT)"), Settings fields labeled excl. VAT. VAT audit recorded as a comment
in `PriceCalculationService.Calculate` — spot prices and tariff rows are ex-VAT by convention, VAT
is applied exactly once.

## 4. Integrate consumption from HA

~~Let WattsUp be a great interface for seeing the energy costs associated with devices, by integrating with HA for power consumption devices. Use the existing pattern of fetching a list, and let that be multi-selectable. Calculate costs on an hourly basis and in "real time" for the current hour.~~
Done — `Services/Homeassistant/HomeAssistantApiClient` (Home Assistant's own REST API via the
Supervisor's `/core/*` proxy, not MQTT), a "Consumption devices" picker in Settings reusing the
existing load-list-then-select pattern, `DeviceConsumptionPollingService` (15-minute cadence, same
boundary as the MQTT publisher), and `DeviceCostService` for hourly + current-hour cost.

## 5. Support only 1 region

~~Regions should be a single select - and the other region must be cleared as not set in HA (the sensor).~~
Done — `AppSettings.PriceArea` (was `PriceAreas`), single-select in Settings, and
`IMqttPublisherService.UnpublishPriceAreaAsync` removes the old region's HA sensor when the tracked
area changes.

## 6. Proper support for client device locale

~~Numbers should be formatted according to the client device (browser) locale. Also, the Supplier markup is entered in øre/kWh, which is to do in decimals, when the iPhone keyboard shows decimal comma and the app expects decimal point. The markup is rounded to three decimals in kr - but is in reality 4 decimals. Perhaps there should be four decimals on all the table numbers.~~
Done — browser locale auto-detected once via `/culture/set` (`Program.cs`, `App.razor`) and applied
through `CultureInfo.CurrentCulture` everywhere numbers are formatted; table numbers and the markup
field now use 4 decimals.

## 7. Cheapest time periods

~~Calculate the cheapest period of power consumption for 1, 2, 3, 4, 5 and 6 hours of continuous use. The start of each of those periods should be a sensor in HA, so it can be used for triggering e.g. the laundry or EV charging. When the prediction engine is up, use that along with the confidence (also a sensor for each time period). But for now, use only the available data.~~
Done for the 1-6h start-time sensors (`CheapestPeriodCalculator`, `wattsup_cheapest_start_<n>h`
sensors) and a Dashboard card. **No confidence sensor** — there's no prediction engine (item 1 is
out of scope) to base one on, and fabricating a number would be worse than not having one.

## 8. Theming

~~Match the theming of timetracker (which is missing the dark/light mode here, which I love, so just match the basic colours - e.g. do not use purple here, but rather use a HA colour scheme.~~
Done — `MainLayout.razor` ported from timetracker: the same `#03A9F4` palette, persisted dark/light
mode (`ProtectedLocalStorage` + `prefers-color-scheme` fallback via `wwwroot/theme.js`), and the
responsive drawer.

## 9. Trends

~~Show a trend for the current price compared to the hour before. Show a bar with today's min/max as the high/how and place the current price within it. Show a comparison for today against the last 7 days at this hour. Show a comparison for today's average against the last sevens days.~~
Done — `PriceTrendService` + a Dashboard "Trends" card: hour-over-hour trend, today's min/max range
bar, same-hour-last-7-days, and today's-average-vs-last-7-days.

## 10. Bug - garbled something below the price line chart

~~There is something garbled showing below the hourly line chart.~~ Done — two compounding bugs in
`PriceChart.razor`: MudBlazor 9.9's Y-axis auto-scale defaults to a step of 20 regardless of data
range, squashing DKK/kWh values into an unreadable sliver (fixed via `LineChartOptions.YAxisTicks`);
and all 48 hourly x-axis labels rendered with no thinning, overlapping into illegible text (fixed by
labeling only every 6th hour, rotated).

## 11. Add suppliers list

**Skipped** — investigated per your instruction to only build this if the data is available from
APIs already in use. Neither EnergiDataService (`DayAheadPrices`/`DatahubPricelist`, both regulated
grid-company data) nor Eloverblik (consumption data only) expose a retail-supplier directory, and
hand-curating one wasn't wanted. No code added.

## 12. Be smart and read from the metering point

**Not implemented** — investigated; Eloverblik's Customer API is scoped to consumption data
(metering point ID, type, address) and doesn't expose grid company or supplier identity, so there's
nothing to prefill from today. Documented in `wattsup/DOCS.md`. `AppSettings.GridCompanySource`/
`SupplierSource` ("manual" | "metering_point") are in place if a future API surface makes this
possible.

## 13. Smarter reduced elafgift

~~As we read from the metering point, there is no need to keep track of the 4000 kWh/year. It is readily available from Eloverblik as a secondary metering point, in a distributed fashion, where the 4000 kWhs are split per day. So, when showing costs from metering devices from HA, follow this. When a day is over, distribute the elafgift as an average instead, based on the total consumption. This is not actually the day after - Eloverblik has at least one more day's delay.~~
Done — confirmed Eloverblik does expose a secondary "elvarme" metering point for this (selectable
in Settings once configured); `EloverblikConsumptionPollingService` pulls its daily figures (2-day
settlement lag) into `elafgift_daily_allowance`, and `TariffResolutionService` blends a single
elafgift rate per completed day via `ElafgiftDistributionCalculator`, falling back to an estimate
from the household's own year-to-date consumption when no real allowance data exists yet for that
date. Today (still in progress) keeps the previous live year-to-date-vs-threshold check.
