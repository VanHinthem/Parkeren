# Issue #146 — Model implementation slices

Status: model preflight; implementation to follow in small, testable commits.
Date: 2026-10-08
Branch: `feature/provider-history-reconciliation`

## Existing confirmed schema constraints
- `ProviderParkingAction.VisitId` is already nullable.
- `ProviderParkingAction` stores provider ID/product, planned and actual timestamps, provider cost and state.
- Existing EF configuration has a unique index for non-null provider action IDs; its global uniqueness was documented as an accepted risk in issue #82. Avoid changing scope without concrete provider evidence.
- `Vehicle.NormalizedLicensePlate` is unique. A `UserVehicle` join associates multiple users with one vehicle, with a composite key; the join does not store historical assignment periods.
- Historical `Visit.UserId` is immutable domain history, and must not be treated as an editable assignment for imported records.

## Target invariants
1. Imported actions exist independently of Visit (`VisitId = null`); no synthetic Visits.
2. A stable plate reference and optional user attribution belong to the provider-action record. This attribution is separate from Visit ownership.
3. Automatic `Inferred` assignment occurs only at the moment a previously unknown historical action is first linked, and only if exactly one current UserVehicle mapping exists.
4. Future vehicle assignments never reattribute historical actions.
5. `ManuallyAssigned` cannot be overwritten by provider resync.
6. Every user assignment change is auditable.
7. External provider ID remains the matching key; do not change the existing global uniqueness convention in this feature without evidence that 2Park reuses IDs across products.
8. Imported vehicle must not create `UserVehicle` access.
9. Provider's actual start/end/cost data must be correctable independently from local managed Visit lifecycle.
10. New persistent fields and corresponding EF migrations/tests must land as one coherent slice to keep CI green.

## Proposed persistence fields on ProviderParkingAction
- `Origin`: `Managed | Imported | External`. Default for existing rows: Managed.
- `VehicleId?`: FK to Vehicle, `Restrict` on delete; plate snapshot may be needed to preserve raw provider record even if vehicle later changes.
- `AssignedUserId?`: nullable FK to User, `Restrict` on delete.
- `AssignmentSource`: `Confirmed | Inferred | ManuallyAssigned | Unassigned`; existing actions should derive initial value from the associated Visit, not infer incorrectly.
- `FirstObservedAt?`, `LastSyncedAt?`: metadata for history import; null for pre-feature rows when appropriate.

## Proposed separate entities
- `ProviderHistorySyncState`: one record per product; incremental cursor, last successful sync, status and diagnostics. Actual cursor algorithm comes with #147/#152.
- `ProviderActionAssignmentAudit`: before/after user and source, actor and timestamp; append-only admin audit.
- Avoid putting provider credentials/raw unredacted payload into persistent run logs.

## Execution order
1. Add and test safe domain model semantics, EF mappings, defaults and migration together. Confirm upgrade of existing rows.
2. Introduce assignment audit model and persistence.
3. Introduce sync state and related persistence with its first consuming service in #147.
4. Add import/correction APIs after persisted invariants are verified.
5. No pagination changes; no financial ledger or balance validation.

## Special consideration
Current `ProviderParkingAction` constructor requires planned start/end, and `ApplyProviderHistory` accepts only pending reconciliations. New import construction must supply valid actual historical intervals without simulating a Start/Stop operation. Incomplete records require explicit handling in the importer (#147); never fabricate dates.

## Test expectations
- Existing managed rows retain behavior and match attribution after migration.
- Imported action without Visit persists correctly and does not produce scheduler work.
- Multiple UserVehicle owners result in Unassigned; one owner results in Inferred.
- Repeated import leaves manual attribution untouched and does not duplicate actions.
- Vehicle and user FK restrict deletes and user changes are auditable.
