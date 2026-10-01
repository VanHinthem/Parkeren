import { describe,expect,it } from "vitest";
import type { ActiveVisit } from "../api/client";
import {
  activeVisitHealthMessage,
  activeVisitPresentation,
  canExtendActiveVisit,
  canStopActiveVisit
} from "./activeVisitPresentation";

function visit(overrides:Partial<ActiveVisit>={}):ActiveVisit{
  return {
    id:"visit",
    startOperationId:"operation",
    userId:"user",
    vehicleId:"vehicle",
    startAt:"2026-10-01T16:00:00Z",
    desiredEndAt:"2026-10-01T18:00:00Z",
    actualEndAt:null,
    status:"Active",
    health:"Healthy",
    ...overrides
  };
}

describe("active visit presentation",()=>{
  it("shows provider attention instead of healthy parking",()=>{
    expect(activeVisitPresentation("AttentionRequired")).toEqual({
      label:"Parkeren vraagt aandacht",
      detail:"Providerstatus wijkt af",
      attention:true
    });
    expect(activeVisitHealthMessage("AttentionRequired")).toContain("parkeerprovider meldt een afwijking");
  });

  it("blocks extension but still allows finishing an attention visit",()=>{
    const attentionVisit=visit({health:"AttentionRequired"});
    expect(canExtendActiveVisit(attentionVisit)).toBe(false);
    expect(canStopActiveVisit(attentionVisit)).toBe(true);
  });

  it("blocks new actions while active reconciliation is in progress",()=>{
    const reconciling=visit({health:"Reconciling"});
    expect(canExtendActiveVisit(reconciling)).toBe(false);
    expect(canStopActiveVisit(reconciling)).toBe(false);
  });

  it("allows retrying a stop that is already reconciling",()=>{
    const reconcilingStop=visit({status:"Stopping",health:"Reconciling"});
    expect(canExtendActiveVisit(reconcilingStop)).toBe(false);
    expect(canStopActiveVisit(reconcilingStop)).toBe(true);
  });
});
