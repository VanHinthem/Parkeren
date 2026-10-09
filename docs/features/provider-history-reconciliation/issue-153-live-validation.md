# Issue #153: real-provider validation checklist

Status: pending live observation. The anonymized fixtures and mocked HTTP integration tests do not establish current production provider behavior.

The operator should use existing configured access and perform **read-only history retrieval only**. No start, stop, extension, or other provider mutation is authorized by this checklist. Do not publish personal history data.

- [ ] Verify a successful history response and document the observation date.
- [ ] Compare redacted fields: action ID, state, plate, location, start/end timestamps, cost and currency against existing parser tests.
- [ ] Observe first and final page metadata and record mismatches, while leaving the existing pagination implementation unchanged.
- [ ] Record naturally occurring stopped, chained or incomplete variants when available; do not create actions to force test cases.
- [ ] Check, if observable, history retention and whether finalized costs or times can be revised.
- [ ] Confirm that read-only verification caused no new Visits, provider mutations, or retrospective notifications.

For each item record: verified, not observed, or mismatch. Keep identifiable values and raw payloads out of GitHub. Review mismatches explicitly before considering issue #153 complete or opening the final PR. The pagination freeze and prior provider observations are recorded in `issue-145-evidence.md`.


## Live evidence — 2026-10-09 (owner-operated read-only requests)

No raw action identifiers, registration plates, or credentials were retained.

| Requested index range | Reported start | Reported stop | Reported max | Records | States |
|---|---:|---:|---:|---:|---|
| 1–10 | 1 | 10 | 24 | 10 | 10 COMPLETED |
| 21–24 (first) | 21 | 23 | 24 | 3 | 3 COMPLETED |
| 24–24 | 24 | 24 | 24 | 1 | 1 COMPLETED |
| 21–24 (repeat) | 21 | 23 | 24 | 3 | 3 COMPLETED |

All returned actions in these probes contained COST, CURRENCY_DESC, LOCATION, MBR_IDENT, TIMEEND and TIMESTART.

**Finding:** a record is individually retrievable at index 24, while both repeated range requests for 21–24 stop at 23. This is a reproducible response-boundary discrepancy; its cause is not established. The outputs alone do **not** prove identity of a missing action or that a production sync has dropped a record. No pagination implementation changes are authorized from this evidence alone.

**Release gate:** investigate range-edge semantics with a focused mocked provider test and, if required, a further privacy-preserving boundary probe. Verify whether the existing reader will skip a provider record. Keep #153 open and do not merge until safe handling is established.


## Further live boundary and identity observations — 2026-10-09

Read-only operator probe; no raw IDs or personal data retained.

- Ranges 1–10 and 11–20 each returned 10 distinct IDs and share none.
- Range 20–24 returned five IDs. It shares exactly one ID with 11–20. Thus the union of 1–10, 11–20 and 20–24 contains **24 distinct IDs**.
- Range 21–24 returned three IDs and reported stopindex 23, despite maxindex 24. All three IDs belong to the 24-ID reference set.
- Singleton 24–24 returned one ID, **already present in range 21–24**.
- Range 0–0 returned zero records; 0–9 returned nine IDs, all shared with 1–10. This supports 1-based indexing.

**Conclusion:** The reader's current 1–10, 11–20, 21–24 three-page sequence yields 23 distinct IDs in this observation, versus the independently confirmed 24-ID reference union. The singleton-tail read cannot recover the missing ID because its ID is already included in 21–24. The exact missing position has not yet been determined. Do not close issue #153 or accept the current tail-recovery workaround as sufficient.

**Next evidence needed:** a privacy-preserving comparison of IDs in 20–24 against the union of 11–20 and 21–24; report counts only. Investigate whether the provider's requested start/end semantics behave differently at a range boundary. Only then make a narrowly tested reader change.
