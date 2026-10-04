import { describe,expect,it } from "vitest";
import type { AdminVisitTimelineEvent } from "../../api/client";
import { groupTimelineEvents,timelineSourceHref } from "./visitTimeline";

function event(
  id:string,
  occurredAt:string,
  groupKey:string,
  sourceType:string="visit",
  sourceId=id,
  eventOrder=0
):AdminVisitTimelineEvent{
  return {
    id,
    occurredAt,
    eventOrder,
    sourceType,
    sourceId,
    eventType:"test.event",
    groupKey,
    attemptNumber:null,
    reasonCode:"test_reason",
    detailsJson:null
  };
}

describe("groupTimelineEvents",()=>{
  it("keeps interleaved sources chronological while grouping adjacent events",()=>{
    const groups=groupTimelineEvents([
      event("visit-completed","2026-10-04T11:00:00Z","visit:1"),
      event("a-succeeded","2026-10-04T10:00:00Z","attempt:op:1","provider_operation","op-id",2),
      event("visit-active","2026-10-04T09:00:00Z","visit:1"),
      event("z-start","2026-10-04T10:00:00Z","attempt:op:1","provider_operation","op-id",1)
    ]);

    expect(groups.map(group=>group.events.map(item=>item.id))).toEqual([
      ["visit-active"],
      ["z-start","a-succeeded"],
      ["visit-completed"]
    ]);
  });
});

describe("timelineSourceHref",()=>{
  it("links work, operations, and actions to their detail rows",()=>{
    expect(timelineSourceHref(event("event-op","2026-10-04T10:00:00Z","op","provider_operation","op-id")))
      .toBe("#provider-operation-op-id");
    expect(timelineSourceHref(event("event-action","2026-10-04T10:00:00Z","action","provider_action","action-id")))
      .toBe("#provider-action-action-id");
    expect(timelineSourceHref(event("event-work","2026-10-04T10:00:00Z","work","scheduler_work","work-id")))
      .toBe("#scheduler-work-work-id");
  });
});