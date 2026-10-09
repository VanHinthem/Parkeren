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


## Code review — complete paging chain (2026-10-09)

Reviewed: `TwoParkProvider.GetActionHistoryPageAsync`, `ProviderActionHistoryPage.HasMore`,
`ProviderHistoryCheckpointedImportService.ExecuteRunAsync`,
`ProviderHistoryTransactionalPageImporter`, `ProviderHistorySyncStateStore`.

### Confirmed behavior

1. The reader requests logical pages using `pageNumber * pageSize + 1` through
   `startIndex + pageSize - 1`, and clamps the stop index to a cached provider
   `maxindex`. Live 0–0 and overlapping boundary reads support 1-based,
   inclusive index positions.
2. `maxindex` is passed as `TotalCount`; `HasMore` is calculated from
   `(PageNumber + 1) * PageSize < TotalCount`. This works for the observed
   24-index example but depends on the provider's maxindex semantics.
3. `historyMaxIndexByProduct` is an instance-level cache. A fresh run does
   not explicitly invalidate it, and subsequent first-page requests can use
   an old upper bound. This is a risk if more history arrives while the same
   `TwoParkProvider` instance is reused. Verify the DI lifetime and test
   reuse of the same provider instance across successive syncs.
4. The checkpointed service validates page number, size and
   `Records.Count <= PageSize`, but does not verify the union of distinct
   action IDs against a known total. A terminal page with 3 instead of 4
   records can be committed and the run marked successful.
5. Each page and its checkpoint are committed atomically. After a successful
   run, `NextPageNumber` is reset to zero. After an interruption the next
   execution resumes at the saved page number. Moving provider index
   positions between runs can thus change the page-to-record mapping,
   even when the checkpoint remains internally consistent.
6. The terminal singleton-retry added in commit `705d8f6` is deduplicated
   by provider ID, but live evidence shows singleton 24 is **already**
   among the IDs returned for 21–24. It does not recover the missing ID.

### Identity invariant established from live data

The three request ranges `1–10`, `11–20`, `20–24` produced
24 distinct action IDs, with exactly one expected overlap at position 20.
The normal `1–10`, `11–20`, `21–24` sequence produced only 23 IDs.
The missing ID is in the `20–24` reference set but absent from the
normal sequence. Its exact provider index metadata cannot be inferred
from these aggregates alone. In particular, `stopindex=23` cannot be
used to identify which action is missing.

### Required validation before a production fix

- Reproduce the live identity sets with a mocked HTTP handler (including
  the last-page singleton duplicating an existing action).
- Test that a complete read imports all reference IDs exactly once;
  checking only `HasMore`, counts, or the final `stopindex` is insufficient.
- Test a second sync on the **same provider instance** after the provider
  maximum increases, and test interruption/resumption if page positions move.
- Decide on a consistent overlap/window strategy with deduplication by
  provider action ID and a defined termination/error rule. Do not assume
  a singleton lookup is the missing record, and do not silently report a
  partial import as complete.
- Keep all diagnostic reads read-only, avoid exposing history identifiers,
  and retain the current feature branch and issue #153 release gate.

This analysis documents risks; it does not change production pagination
or establish a universal 2Park API contract from one provider dataset.


## Resume safety decision — 2026-10-09

The integration test for shifting indices demonstrates a real model limitation:
page-number-only checkpoints cannot guarantee completeness when the provider
inserts, removes, or reorders actions between interrupted runs. Comparing
`maxindex` is insufficient because a reordering may preserve the count. The
existing `ProviderHistoryImportPlan` deduplicates identical action IDs within a
page and the persistence layer is idempotent for previously saved provider IDs,
but neither proves that every index was visited.

**Recommended implementation:** On a newly started recovery attempt whose durable
checkpoint has `NextPageNumber > 0`, restart the *read traversal* at page zero
rather than trust the old index offset. Keep all previously committed provider
actions, and use provider action IDs to reapply them idempotently. This is safer
than interpreting a changed total as proof of an index shift, and works even
if the provider count remains the same. The resume reset must be atomic and
serialized under the existing per-product advisory lock, not an ad-hoc update
of the checkpoint row. Preserve existing run/audit semantics and do not
reset progress on a mere per-page retry within the same running traversal.

Before changing production code, cover: (1) interrupted run -> restart page 0;
(2) stable provider snapshot -> no duplicate stored actions; (3) insertion at
head with unchanged or increased maxindex -> no lost action; (4) failure during
restart -> checkpoint persists correctly; (5) reserved/cancelled runs and
concurrency locks retain their lifecycle behavior.

A final full traversal alone does not guarantee completeness if the provider
changes order *during* that traversal. This separate consistency limitation
remains for #153 and should not be described as solved by the restart rule.


## Validation status — 2026-10-09 (after CI run 37952776045)

The earlier **pending** checklist and code-review observations above are a historical
record of the original risks, not the current implementation status. The
following distinguishes automated verification from read-only live evidence.

| Acceptance area | Current evidence | Status |
|---|---|---|
| Read-only 2Park history access | Owner-operated requests to `get_action_history.json`, anonymized aggregate findings above | Observed |
| Terminal range missing an action | Mocked live-boundary shape: overlap `20–24` recovers all 24 distinct action IDs | Regression covered; repeat live validation needed |
| Growth between separate syncs | First page refreshes `maxindex` on the same provider instance | Regression covered |
| Interrupted import with shifted indices | The checkpoint traversal restarts from page zero under the product lock; saved actions remain idempotent | Implementation + test coverage; service-level shifting-index E2E still desirable |
| Provider count changes between pages | Reject a changed `maxindex`, avoiding successful completion of that traversal | Regression covered |
| Missing action ID | Reject malformed action instead of silently skipping it | Parser regression covered |
| Completed action missing valid start/end | Reject the page rather than silently dropping the action | Reader regression covered |
| Provider changes order without changing `maxindex` during a single traversal | Count check cannot detect this; action-ID union may still be incomplete | **Open** |
| Reimport, corrections, assignments, budgets, capacity, notification deduplication | Existing targeted suites cover individual flows; review full E2E acceptance against #153 | **Not signed off** |
| Real provider corrected-history / chained / incomplete variants and retention | Not established by current read-only observations | **Not observed / open** |
| Final feature PR to `develop` | Must follow documented sign-off and green CI | **Not started** |

**Release gate:** Do not infer complete real-provider correctness from mocked
success. In particular, an unchanged `maxindex` does not establish a stable
snapshot or action-ID ordering across pages. Keep #153 open until the remaining
E2E scenarios and privacy-safe provider observations have been reviewed.


## Live overlap recovery confirmation — 2026-10-09

The owner reran `scripts/probe-twopark-history-readonly.py` against the
configured real 2Park product after commit `3efc101`. Only aggregate counts
and set comparisons were reported; no provider IDs or personal data retained.

| Observation | Result |
|---|---:|
| Provider `maxindex` (all five queried ranges) | 24 |
| Standard reader's unique IDs: 1–10 + 11–20 + 21–24 | 23 |
| Reference unique IDs: 1–10 + 11–20 + 20–24 | 24 |
| Unique IDs with terminal overlap | **24** |
| Additional ID recovered by overlap | **1** |
| Overlap result identical to reference ID set | **Yes** |
| Action ID missing from reference after recovery | **0** |
| Actions without IDs or duplicate IDs within queried pages | **0** |
| Overlap between disjoint first two pages | **0** |
| Singleton 24–24 action also present in 21–24 | **Yes** |

**Confirmed:** The implemented bounded terminal overlap strategy recovers the
missing ID for the observed 24-index live provider dataset. This upgrades the
earlier boundary analysis from mock-only verification to a privacy-preserving,
read-only live identity-set comparison. The preceding sections' outstanding
questions reflect their historical status before this observation.

**Not established:** correct results for every possible history size, a stable
provider snapshot when entries reorder mid-traversal with an unchanged
`maxindex`, or unobserved history variants and retention behavior. The probe
does not itself execute the app's persistence pipeline. Retain these limitations
in the #153 release assessment; do not claim universal provider completeness.
