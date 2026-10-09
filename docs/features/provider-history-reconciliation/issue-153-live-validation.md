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
