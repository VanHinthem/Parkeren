# Issue #145 — 2Park history evidence and open verification

Date: 2026-10-08
Status: repository evidence collected; live provider validation pending.
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

## Proposed next evidence step

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
