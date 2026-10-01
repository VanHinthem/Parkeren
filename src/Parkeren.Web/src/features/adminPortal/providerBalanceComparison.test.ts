import { describe,expect,it } from "vitest";
import { providerBalanceDiscrepancyMinutes } from "./providerBalanceComparison";

const currentFrom="2026-01-01T00:00:00.000Z";
const currentUntil="2027-01-01T00:00:00.000Z";
const now=Date.parse("2026-10-01T19:00:00.000Z");

function compare(overrides:Partial<Parameters<typeof providerBalanceDiscrepancyMinutes>[0]>={}){
  return providerBalanceDiscrepancyMinutes({
    providerUnit:"Minute",
    providerRemainingBalance:600,
    providerBalanceIsStale:false,
    localRemainingPaidDurationMinutes:540,
    localUsageIsComplete:true,
    periodValidFrom:currentFrom,
    periodValidUntil:currentUntil,
    nowMs:now,
    ...overrides
  });
}

describe("providerBalanceDiscrepancyMinutes",()=>{
  it("compares an active complete minute balance",()=>{
    expect(compare()).toBe(60);
  });

  it("does not compare balances with another unit",()=>{
    expect(compare({providerUnit:"Euro"})).toBeNull();
  });

  it("does not compare a stale provider balance",()=>{
    expect(compare({providerBalanceIsStale:true})).toBeNull();
  });

  it("does not compare an incomplete local calculation",()=>{
    expect(compare({localUsageIsComplete:false})).toBeNull();
  });

  it("does not compare a historical budget period with the current provider balance",()=>{
    expect(compare({
      periodValidFrom:"2025-01-01T00:00:00.000Z",
      periodValidUntil:"2026-01-01T00:00:00.000Z"
    })).toBeNull();
  });
});
