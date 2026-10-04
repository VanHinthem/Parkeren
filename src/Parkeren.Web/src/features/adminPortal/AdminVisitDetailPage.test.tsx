// @vitest-environment happy-dom
import { cleanup,fireEvent,render,screen,waitFor } from "@testing-library/react";
import { afterEach,describe,expect,it,vi } from "vitest";
import { getAdminVisit } from "../../api/client";
import type { AdminVisitDetail } from "../../api/client";
import { AdminVisitDetailPage } from "./AdminVisitsPage";

vi.mock("../../api/client",()=>({
  getAdminVisit:vi.fn(),
  getAdminVisits:vi.fn(),
  getUsers:vi.fn(),
  stopVisit:vi.fn()
}));

afterEach(()=>{
  cleanup();
  vi.resetAllMocks();
});

describe("AdminVisitDetailPage scheduler timeline",()=>{
  it("renders source links to scheduler work, provider operations, and provider actions",async()=>{
    const detail={
      visit:{
        id:"visit-1",
        userId:"user-1",
        username:"visitor",
        vehicleId:"vehicle-1",
        licensePlate:"AA-11-BB",
        startedByUserId:"user-1",
        startedByUsername:"visitor",
        startAt:"2026-10-04T08:00:00Z",
        desiredEndAt:"2026-10-04T10:00:00Z",
        actualEndAt:"2026-10-04T10:00:00Z",
        status:"Completed",
        health:"Healthy",
        paidDurationMinutes:120
      },
      providerProductName:null,
      providerProductExternalId:null,
      providerLocation:null,
      policySnapshot:{
        maxPaidParkingDurationMinutes:null,
        maxVisitElapsedDurationMinutes:null,
        allowVisitExtension:true,
        allowOpenEndedVisits:false
      },
      providerActions:[{
        id:"action-1",
        providerActionId:"remote-action-1",
        providerProductId:null,
        providerLocation:null,
        plannedStartAt:"2026-10-04T08:00:00Z",
        plannedEndAt:"2026-10-04T10:00:00Z",
        actualStartAt:"2026-10-04T08:00:00Z",
        actualEndAt:"2026-10-04T10:00:00Z",
        providerStatus:"completed",
        state:"Completed",
        health:"Healthy"
      }],
      providerOperations:[{
        id:"operation-1",
        operationId:"operation-1",
        providerParkingActionId:"action-1",
        parentOperationId:null,
        type:"Start",
        status:"Succeeded",
        attemptCount:1,
        lastErrorCode:null,
        createdAt:"2026-10-04T08:00:00Z",
        attemptStartedAt:"2026-10-04T08:00:00Z",
        requestedEndAt:null,
        completedAt:"2026-10-04T08:00:01Z"
      }],
      schedulerWork:[{
        id:"work-1",
        type:"StopVisit",
        status:"Completed",
        dueAt:"2026-10-04T10:00:00Z",
        endReason:"DesiredEndReached",
        providerParkingActionId:"action-1",
        attemptCount:1,
        createdAt:"2026-10-04T09:00:00Z",
        claimedAt:"2026-10-04T10:00:00Z",
        completedAt:"2026-10-04T10:00:01Z"
      }],
      endTimeChanges:[
        {id:"change-pending",operationId:"change-pending",actorUserId:"admin-1",actorUsername:"admin",previousDesiredEndAt:"2026-10-04T10:00:00Z",requestedDesiredEndAt:"2026-10-04T10:15:00Z",createdAt:"2026-10-04T09:00:00Z",result:"Pending"},
        {id:"change-rejected",operationId:"change-rejected",actorUserId:"admin-1",actorUsername:"admin",previousDesiredEndAt:"2026-10-04T10:00:00Z",requestedDesiredEndAt:"2026-10-04T09:45:00Z",createdAt:"2026-10-04T09:01:00Z",result:"Rejected"},
        {id:"change-applied",operationId:"change-applied",actorUserId:"admin-1",actorUsername:"admin",previousDesiredEndAt:"2026-10-04T10:00:00Z",requestedDesiredEndAt:"2026-10-04T10:30:00Z",createdAt:"2026-10-04T09:02:00Z",result:"Applied"}
      ],
      timelineEvents:[
        {
          id:"event-work",
          occurredAt:"2026-10-04T10:00:00Z",
          eventOrder:1,
          sourceType:"scheduler_work",
          sourceId:"work-1",
          eventType:"scheduler_work.completed",
          groupKey:"attempt:work-1:1",
          attemptNumber:1,
          reasonCode:"work_completed",
          detailsJson:"{\"status\":\"Completed\"}"
        },
        {
          id:"event-operation",
          occurredAt:"2026-10-04T10:00:01Z",
          eventOrder:2,
          sourceType:"provider_operation",
          sourceId:"operation-1",
          eventType:"provider_operation.succeeded",
          groupKey:"attempt:operation-1:1",
          attemptNumber:1,
          reasonCode:"provider_operation_succeeded",
          detailsJson:"{\"status\":\"Succeeded\"}"
        },
        {
          id:"event-action",
          occurredAt:"2026-10-04T10:00:02Z",
          eventOrder:3,
          sourceType:"provider_action",
          sourceId:"action-1",
          eventType:"provider_action.state_changed",
          groupKey:"action:action-1",
          attemptNumber:null,
          reasonCode:"provider_action_state_changed",
          detailsJson:"{\"state\":\"Completed\"}"
        },
        {
          id:"event-end-pending",
          occurredAt:"2026-10-04T10:00:03Z",
          eventOrder:4,
          sourceType:"visit_end_time_change",
          sourceId:"change-pending",
          eventType:"visit.desired_end_change_requested",
          groupKey:"attempt:change-pending:0",
          attemptNumber:null,
          reasonCode:"desired_end_change_requested",
          detailsJson:"{\"result\":\"Pending\"}"
        },
        {
          id:"event-end-rejected",
          occurredAt:"2026-10-04T10:00:04Z",
          eventOrder:5,
          sourceType:"visit_end_time_change",
          sourceId:"change-rejected",
          eventType:"visit.desired_end_change_rejected",
          groupKey:"attempt:change-rejected:0",
          attemptNumber:null,
          reasonCode:"desired_end_change_rejected",
          detailsJson:"{\"result\":\"Rejected\"}"
        },
        {
          id:"event-end-applied",
          occurredAt:"2026-10-04T10:00:05Z",
          eventOrder:6,
          sourceType:"visit_end_time_change",
          sourceId:"change-applied",
          eventType:"visit.desired_end_change_applied",
          groupKey:"attempt:change-applied:0",
          attemptNumber:null,
          reasonCode:"desired_end_change_applied",
          detailsJson:"{\"result\":\"Applied\"}"
        }
      ],
      relevantRuleSets:[]
    } satisfies AdminVisitDetail;

    vi.mocked(getAdminVisit).mockResolvedValue(detail);
    const { container }=render(<AdminVisitDetailPage visitId="visit-1"/>);
    await waitFor(()=>expect(screen.getByRole("heading",{name:"Schedulerverloop"})).toBeTruthy());

    for(const group of container.querySelectorAll(".admin-visit-detail__timeline-group")){
      fireEvent.click(group.querySelector("summary")!);
    }
    for(const sourceDetails of container.querySelectorAll(".admin-visit-detail__timeline-details")){
      fireEvent.click(sourceDetails.querySelector("summary")!);
    }

    expect(container.querySelector('a[href="#scheduler-work-work-1"]')).toBeTruthy();
    expect(container.querySelector('a[href="#provider-operation-operation-1"]')).toBeTruthy();
    expect(container.querySelector('a[href="#provider-action-action-1"]')).toBeTruthy();
    expect(container.querySelector("#scheduler-work-work-1")).toBeTruthy();
    expect(container.querySelector("#provider-operation-operation-1")).toBeTruthy();
    expect(container.querySelector("#provider-action-action-1")).toBeTruthy();
    expect(screen.getByText("Eindtijdwijziging aangevraagd")).toBeTruthy();
    expect(screen.getByText("Eindtijdwijziging afgewezen")).toBeTruthy();
    expect(screen.getByText("Eindtijd gewijzigd")).toBeTruthy();
  });
});