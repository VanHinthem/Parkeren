import { beforeEach, afterEach, expect, it, vi } from "vitest";
import { clearResolvedVisitOperations, getOrCreatePendingOperation } from "./pendingOperations";

let nextId = 0;

beforeEach(() => {
  const values = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value); },
    removeItem: (key: string) => { values.delete(key); }
  });
  nextId = 0;
  vi.stubGlobal("crypto", { randomUUID: () => `operation-${++nextId}` });
});

afterEach(() => vi.unstubAllGlobals());

it("clears confirmed start and end-time IDs on refresh but retains an unresolved stop", () => {
  const startKey = "user:vehicle:2026-09-29T12:00:00.000Z";
  const endKey = "visit:2026-09-29T13:00:00.000Z";
  const startId = getOrCreatePendingOperation("start", startKey);
  const endId = getOrCreatePendingOperation("end-time", endKey);
  const stopId = getOrCreatePendingOperation("stop", "visit");

  clearResolvedVisitOperations({
    id: "visit",
    startOperationId: startId,
    desiredEndAt: "2026-09-29T13:00:00+00:00"
  });

  expect(getOrCreatePendingOperation("start", startKey)).not.toBe(startId);
  expect(getOrCreatePendingOperation("end-time", endKey)).not.toBe(endId);
  expect(getOrCreatePendingOperation("stop", "visit")).toBe(stopId);
});

it("retains an end-time ID until the server reports its requested end", () => {
  const key = "visit:2026-09-29T13:00:00.000Z";
  const id = getOrCreatePendingOperation("end-time", key);
  clearResolvedVisitOperations({
    id: "visit",
    startOperationId: "other-operation",
    desiredEndAt: "2026-09-29T12:00:00.000Z"
  });
  expect(getOrCreatePendingOperation("end-time", key)).toBe(id);
});
