# Issue #145 — 2Park history evidence and open verification

Date: 2026-10-08
Status: repository evidence collected; live provider field sample received; pagination is previously validated and deliberately out of scope.
Issue: https://github.com/VanHinthem/Parkeren/issues/145

## Evidence located

- `tests/Parkeren.IntegrationTests/TestData/two-park-action-history.json` is a checked-in **anonymized** fixture. It contains `data.headers` entries for `MBR_IDENT` (LPN), `TIMESTART`, `TIMEEND`, `LOCATION`, `COST` and `CURRENCY_DESC`. The action also carries `atn_id` and `atn_state`.
- The fixture uses `startindex=10`, `stopindex=19`, `maxindex=20` and one example action. It is **not** by itself proof of live pagination semantics.
- `TwoParkActionHistoryParser` currently extracts ID, state, start/end, cost and currency. It does **not** extract `MBR_IDENT` or `LOCATION` even though the fixture provides these fields.
- `ProviderActionHistoryRecord` likewise has no plate/location fields; its current fields are ID, status, actual start/end, cost, currency.
- `TwoParkProvider.GetActionHistoryPageAsync` calls `get_action_history.json` with `product_id`, `locale=nl_NL`, and one-based inclusive `startindex`/`stopindex`. The reader limits pages to 10, caches a returned maxindex per product, and drops records whose start or end cannot be parsed.
- `TwoParkProviderHistoryReaderTests` validate 1..10, 11..20, 21..21 against a **test HTTP handler**; they do not establish live 2Park page behavior.
- `TwoParkActionHistoryParserTests` establish interpretation of the anonymized fixture and the incomplete fixture. The parser itself keeps a record missing a valid start, but `GetActionHistoryPageAsync` filters it out, which makes incomplete imports invisible without changes.
- `docs/work/issue-82-evidence-map.md` documents an existing unique non-null external action-id index and explicitly calls the global provider ID scope an accepted risk. Multi-product uniqueness needs deliberate review before changing this index.

## What is supported, and what is not

| Claim | Evidence level |
|---|---|
| Plate (`MBR_IDENT`) and zone (`LOCATION`) can occur in a history payload | Supported by checked-in anonymized fixture |
| Current history DTO/reader exposes these fields | No: code explicitly omits them |
| 2Park always supplies plate/location for historic actions | Unverified |
| History pages are one-based and max 10 for live 2Park | Current code/handler assumption; live behavior not yet reverified |
| History records carry provider action ID, status, times, cost/currency | Supported by anonymized fixture and parser tests |
| Live history retains all required prior-year actions | Unverified |
| Provider ID uniqueness across different products | Unverified |
| Natural completion, external stop, and in-flight status formats | Unverified |

## Previously planned evidence step (superseded for pagination)

Using the existing **real provider test environment** and credentials kept outside GitHub:
1. Make read-only `get_action_history.json` requests for the configured product. Never send start/stop mutations.
2. Collect a minimal **redacted** sample from a recent completed action and, when available, externally stopped and incomplete actions; remove plate, IDs and any secrets before attaching evidence.
3. Verify `data.headers`, actual `atn_parameters`, local datetime format, state variants, cost/currency presence, and whether records can lack start/end.
4. Check the first/last pages and `maxindex` behavior, including empty pages and history ordering. Avoid dumping all history or logging personally identifiable data.
5. Verify how far back history reaches and whether provider IDs repeat across products, if a safe observation is possible.

## Implementation consequences pending live verification

- Extend parser/record for normalized license plate and location; preserve absence as explicit nullable data.
- Preserve incomplete rows for import diagnostics instead of silently filtering them.
- Avoid relying exclusively on a permanent `maxindex` cache for incremental sync.
- Keep history-reader and active-action-reader responsibilities distinct.
- Do not mark #145 complete until read-only live verification or explicit documented acceptance of remaining assumptions.


## 2026-10-08 — Live 2Park response supplied by owner

Evidence provenance: direct JSON response supplied in project conversation. Personal identifiers are **not reproduced** in this repository document.

### Confirmed from this sample
- Provider status is OK/SUCCESS.
- `data.startindex = "0"`, `data.stopindex = "9"`, `data.maxindex = "24"`.
- Ten actions were supplied; all have `atn_id`, `atn_state = "COMPLETED"`, and `atn_chained = "NO"`.
- Each of the ten actions has `MBR_IDENT`, `TIMESTART`, `TIMEEND`, `LOCATION`, `COST`, and `CURRENCY_DESC` in `atn_parameters`.
- Timestamps are local strings in `dd-MM-yyyy HH:mm:ss` format; `LOCATION` is a human-readable zone description; `CURRENCY_DESC` has a euro symbol.
- All ten supplied actions have dates from late September through early October 2026 and complete time intervals.

### Important divergence from current code
- Reader `GetActionHistoryPageAsync` calculates `startIndex = pageNumber * pageSize + 1`; live sample indicates zero is a valid starting index. This is evidence of a **possible first-record skip**, not proof of omission until a controlled `startindex=0` vs `startindex=1` query comparison.
- The current `HasMore` implementation assumes `TotalCount = maxindex`. A value of 24 may be the inclusive last index (25 records) rather than the count (24); confirm with last-page requests.
- `TwoParkActionHistoryParser` drops `MBR_IDENT` and `LOCATION` despite their availability in the raw data.

### Still unverified
- Whether `maxindex` represents last zero-based index, count, or a provider-specific index convention.
- Whether `startindex=0,stopindex=9` returns the same/next records as `startindex=1,stopindex=10`.
- Which status/chained variants appear outside completed stand-alone records.
- Whether all older history, multiple products, missing fields or externally stopped actions follow the same structure.
- Whether historical paid-hour accounting matches rule-based reconstruction.

### Next safe validation
Request the next history page (`startindex=10,stopindex=19`) and final page (`startindex=20,stopindex=24`) if these ranges are accepted, plus controlled boundary comparison for the first page; record only redacted action identifiers and counts. Do not commit raw history containing license plates.

## Decision — 2026-10-08: pagination is frozen

The repository owner confirmed that history pagination has already been verified and documented in earlier work. **Do not change page numbering, maxindex handling, or pagination tests under issue #145.** The newly provided JSON is evidence for the action fields and not authorization to revise existing pagination behavior. Earlier concerns in this document about index 0 and maxindex are superseded by this explicit decision. The next code slice is strictly limited to parsing `MBR_IDENT` and `LOCATION`, extending the history DTO, and adding focused tests without altering pagination.
