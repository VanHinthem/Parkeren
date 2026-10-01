import { describe,expect,it } from "vitest";
import type { AdminProviderDiscrepancy } from "../../api/client";
import {
  providerDiscrepancyContextLabel,
  providerDiscrepancyTypeLabel
} from "./providerDiscrepancyPresentation";

function discrepancy(
  overrides:Partial<AdminProviderDiscrepancy>={}
):AdminProviderDiscrepancy{
  return {
    id:"1",
    key:"key",
    type:"ExternalProviderAction",
    status:"Open",
    providerProductId:"product",
    providerProductName:"Bezoekersparkeren",
    providerProductExternalId:"visitor",
    visitId:null,
    providerParkingActionId:null,
    localProviderActionState:null,
    localPlannedEndAt:null,
    providerActionId:"action",
    providerStatus:"active",
    providerStartAt:null,
    providerEndAt:null,
    detectedAt:"2026-10-01T12:00:00Z",
    lastObservedAt:"2026-10-01T12:00:00Z",
    resolvedAt:null,
    ...overrides
  };
}

describe("provider discrepancy presentation",()=>{
  it("labels all discrepancy types",()=>{
    expect(providerDiscrepancyTypeLabel("MissingProviderAction")).toBe("Actie ontbreekt bij 2Park");
    expect(providerDiscrepancyTypeLabel("ProviderActionStatusMismatch")).toBe("Providerstatus wijkt af");
    expect(providerDiscrepancyTypeLabel("ProviderActionEndMismatch")).toBe("Eindtijd wijkt af");
    expect(providerDiscrepancyTypeLabel("ExternalProviderAction")).toBe("Externe 2Park-actie");
    expect(providerDiscrepancyTypeLabel("BalanceMismatch")).toBe("Saldo-afwijking");
  });

  it("shows local and provider status together",()=>{
    expect(providerDiscrepancyContextLabel(discrepancy({
      localProviderActionState:"Active",
      providerStatus:"stopped"
    }))).toBe("Lokaal: Active · 2Park: stopped");
  });
});
