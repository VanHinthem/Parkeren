import { Fragment,useState } from "react";
import type {
  AdminProviderOperationSummary,
  AdminVisitSchedulerWorkSummary,
  AdminVisitTimelineEvent
} from "../../api/client";
import {
  adminProcessStatusTone,
  adminStatusClass,
  formatAdminDateTime,
  formatAdminJson,
  formatAdminNumber,
  formatAdminProcessStatus
} from "./adminFieldFormatters";
import { timelineSourceHref } from "./visitTimeline";

function timelineTitle(event:AdminVisitTimelineEvent){
  const titles:Record<string,string>={
    "visit.created":"Visit aangemaakt",
    "visit.status_changed":"Visitstatus gewijzigd",
    "visit.health_changed":"Visitgezondheid gewijzigd",
    "visit.desired_end_change_requested":"Eindtijdwijziging aangevraagd",
    "visit.desired_end_change_rejected":"Eindtijdwijziging afgewezen",
    "visit.desired_end_change_applied":"Eindtijd gewijzigd",
    "visit.desired_end_changed":"Eindtijd gewijzigd",
    "scheduler_work.created":"Schedulerwerk aangemaakt",
    "scheduler_work.claimed":"Schedulerwerk opgepakt",
    "scheduler_work.deferred":"Schedulerwerk uitgesteld",
    "scheduler_work.released":"Schedulerwerk opnieuw ingepland",
    "scheduler_work.completed":"Schedulerwerk afgerond",
    "scheduler_work.cancelled":"Schedulerwerk geannuleerd",
    "provider_operation.created":"Provideroperatie aangemaakt",
    "provider_operation.attempt_started":"Providerpoging gestart",
    "provider_operation.outcome_unknown":"Provideruitkomst onbekend",
    "provider_operation.reconciliation_started":"Reconciliatie gestart",
    "provider_operation.succeeded":"Provideroperatie geslaagd",
    "provider_operation.failed":"Provideroperatie mislukt",
    "provider_operation.retry_ready":"Provideroperatie klaar voor retry",
    "provider_action.created":"Provideractie aangemaakt",
    "provider_action.state_changed":"Provideractiestatus gewijzigd",
    "provider_action.health_changed":"Provideractiegezondheid gewijzigd",
    "provider_action.history_changed":"Providerhistorie bijgewerkt",
    "provider_action.timing_changed":"Provideractietijden gewijzigd"
  };
  return titles[event.eventType]??event.eventType;
}

type TimelineActivityKind=
  |"start"
  |"stop-planned"
  |"stop"
  |"continuation"
  |"reconciliation"
  |"end-time"
  |"warning"
  |"visit"
  |"other";

type TimelineActivity={
  key:string;
  kind:TimelineActivityKind;
  events:AdminVisitTimelineEvent[];
};

function detailsText(event:AdminVisitTimelineEvent){
  return `${event.reasonCode} ${event.detailsJson??""}`.toLowerCase();
}

function activityKind(
  event:AdminVisitTimelineEvent,
  workById:Map<string,AdminVisitSchedulerWorkSummary>,
  operationById:Map<string,AdminProviderOperationSummary>
):TimelineActivityKind{
  if(event.eventType.includes("desired_end"))return "end-time";
  if(event.eventType==="provider_operation.reconciliation_started")return "reconciliation";

  if(event.sourceType==="scheduler_work"){
    const work=workById.get(event.sourceId);
    switch(work?.type){
      case "StopVisit":
        return event.eventType==="scheduler_work.created"?"stop-planned":"stop";
      case "ContinueProviderCoverage": return "continuation";
      case "ReconcileProviderAction": return "reconciliation";
      case "LongVisitWarning": return "warning";
    }
  }

  if(event.sourceType==="provider_operation"){
    const operation=operationById.get(event.sourceId);
    switch(operation?.type){
      case "Start": return "start";
      case "ContinueStart":
      case "Extend": return "continuation";
      case "Stop": return "stop";
    }
  }

  if(event.sourceType==="provider_action"){
    const details=detailsText(event);
    if(details.includes("stopping")||details.includes("stopped")||details.includes("completed"))return "stop";
    if(details.includes("starting")||details.includes("active"))return "start";
    if(details.includes("scheduled")||details.includes("planned"))return "continuation";
  }

  if(event.sourceType==="visit"){
    if(event.eventType==="visit.created")return "start";
    const details=detailsText(event);
    if(details.includes("completed")||details.includes("stopping")||details.includes("cancelled"))return "stop";
    if(details.includes("starting")||details.includes("active"))return "start";
    return "visit";
  }

  return "other";
}

function activityTitle(activity:TimelineActivity){
  const eventTypes=new Set(activity.events.map(event=>event.eventType));
  switch(activity.kind){
    case "start": return "Parkeerbezoek gestart";
    case "stop-planned": return "Stop ingepland";
    case "stop": return "Parkeerbezoek gestopt";
    case "continuation":
      return eventTypes.has("scheduler_work.created")?"Vervolgactie ingepland":"Providerdekking voortgezet";
    case "reconciliation":
      return activity.events.some(event=>event.eventType.endsWith(".completed")||event.eventType.endsWith(".succeeded"))
        ?"Provider gereconcilieerd"
        :"Providerreconciliatie";
    case "end-time":
      if(eventTypes.has("visit.desired_end_change_rejected"))return "Eindtijdwijziging afgewezen";
      if(eventTypes.has("visit.desired_end_change_applied")||eventTypes.has("visit.desired_end_changed"))return "Eindtijd gewijzigd";
      return "Eindtijdwijziging aangevraagd";
    case "warning": return "Long-Visit waarschuwing";
    case "visit": return "Visitstatus bijgewerkt";
    case "other": return "Systeemgebeurtenissen";
  }
}

function buildTimelineActivities(
  events:AdminVisitTimelineEvent[],
  schedulerWork:AdminVisitSchedulerWorkSummary[],
  providerOperations:AdminProviderOperationSummary[]
){
  const workById=new Map(schedulerWork.map(work=>[work.id,work]));
  const operationById=new Map(providerOperations.map(operation=>[operation.id,operation]));
  const chronological=[...events].sort((left,right)=>
    new Date(left.occurredAt).getTime()-new Date(right.occurredAt).getTime()||
    left.eventOrder-right.eventOrder||
    left.id.localeCompare(right.id)
  );
  const activities:TimelineActivity[]=[];
  const maxGapMs=2*60*1000;

  for(const event of chronological){
    const kind=activityKind(event,workById,operationById);
    const current=activities.at(-1);
    const previous=current?.events.at(-1);
    const gap=previous?new Date(event.occurredAt).getTime()-new Date(previous.occurredAt).getTime():Number.POSITIVE_INFINITY;

    if(current&&current.kind===kind&&gap<=maxGapMs){
      current.events.push(event);
      continue;
    }

    activities.push({key:`${kind}:${event.id}`,kind,events:[event]});
  }

  return activities;
}

function toggleSet(current:Set<string>,key:string){
  const next=new Set(current);
  if(next.has(key))next.delete(key);else next.add(key);
  return next;
}

export function AdminVisitSchedulerSections({
  timelineEvents,
  schedulerWork,
  providerOperations
}:{
  timelineEvents:AdminVisitTimelineEvent[];
  schedulerWork:AdminVisitSchedulerWorkSummary[];
  providerOperations:AdminProviderOperationSummary[];
}){
  const[openTimelineGroups,setOpenTimelineGroups]=useState<Set<string>>(()=>new Set());
  const[openWorkItems,setOpenWorkItems]=useState<Set<string>>(()=>new Set());
  const activities=buildTimelineActivities(timelineEvents,schedulerWork,providerOperations);

  return <>
    <section className="admin-visits__panel admin-visit-detail__section">
      <h2>Schedulerverloop</h2>
      {activities.length===0
        ? <p className="admin-visit-detail__muted">Geen schedulergebeurtenissen vastgelegd.</p>
        : <div className="admin-table-wrap">
            <table className="admin-table admin-table--fixed admin-visit-detail__timeline-table">
              <thead><tr><th>Gebeurtenis</th><th>Tijdstip</th><th>Technische events</th><th aria-label="Details"/></tr></thead>
              <tbody>
                {activities.map(activity=>{
                  const open=openTimelineGroups.has(activity.key);
                  return <Fragment key={activity.key}>
                    <tr>
                      <td><strong>{activityTitle(activity)}</strong></td>
                      <td>{formatAdminDateTime(activity.events[0].occurredAt)}</td>
                      <td className="admin-number">{formatAdminNumber(activity.events.length)}</td>
                      <td className="admin-table__actions"><button type="button" className="admin-action-link admin-action-link--muted" onClick={()=>setOpenTimelineGroups(current=>toggleSet(current,activity.key))}>{open?"Verbergen ▴":"Tonen ▾"}</button></td>
                    </tr>
                    {open&&<tr className="admin-table__detail-row"><td colSpan={4}><div className="admin-table__detail-panel admin-visit-detail__timeline-events">
                      {activity.events.map(event=><article className="admin-visit-detail__timeline-event" key={event.id}>
                        <div className="admin-visit-detail__timeline-event-head"><strong>{timelineTitle(event)}</strong><time>{formatAdminDateTime(event.occurredAt)}</time></div>
                        <p>{event.reasonCode.replaceAll("_"," ")}</p>
                        <dl className="admin-facts admin-facts--grid admin-visit-detail__technical-facts">
                          <div className="admin-fact"><dt>Bron</dt><dd className="admin-code">{event.sourceType}</dd></div>
                          <div className="admin-fact"><dt>Record-ID</dt><dd className="admin-code">{timelineSourceHref(event)?<a className="admin-action-link" href={timelineSourceHref(event)!}>{event.sourceId}</a>:event.sourceId}</dd></div>
                          <div className="admin-fact"><dt>Event type</dt><dd className="admin-code">{event.eventType}</dd></div>
                        </dl>
                        {event.detailsJson&&<pre className="admin-code-block">{formatAdminJson(event.detailsJson)}</pre>}
                      </article>)}
                    </div></td></tr>}
                  </Fragment>;
                })}
              </tbody>
            </table>
          </div>}
    </section>

    <section className="admin-visits__panel admin-visit-detail__section">
      <h2>Schedulerwerk</h2>
      {schedulerWork.length===0
        ? <p className="admin-visit-detail__muted">Geen schedulerwerk vastgelegd.</p>
        : <div className="admin-table-wrap">
            <table className="admin-table admin-table--fixed admin-visit-detail__scheduler-work-table">
              <thead><tr><th>Type</th><th>Uitvoeren</th><th>Pogingen</th><th>Status</th><th>Eindreden</th><th aria-label="Details"/></tr></thead>
              <tbody>
                {schedulerWork.map(work=>{
                  const open=openWorkItems.has(work.id);
                  return <Fragment key={work.id}>
                    <tr id={`scheduler-work-${work.id}`}>
                      <td><strong>{work.type}</strong></td>
                      <td>{formatAdminDateTime(work.dueAt)}</td>
                      <td className="admin-number admin-number--table">{formatAdminNumber(work.attemptCount)}</td>
                      <td><span className={adminStatusClass(adminProcessStatusTone(work.status))}>{formatAdminProcessStatus(work.status)}</span></td>
                      <td className="admin-code">{work.endReason??"—"}</td>
                      <td className="admin-table__actions"><button type="button" className="admin-action-link admin-action-link--muted" onClick={()=>setOpenWorkItems(current=>toggleSet(current,work.id))}>{open?"Verbergen ▴":"Tonen ▾"}</button></td>
                    </tr>
                    {open&&<tr className="admin-table__detail-row"><td colSpan={6}><div className="admin-table__detail-panel">
                      <dl className="admin-facts admin-facts--grid admin-visit-detail__technical-facts">
                        <div className="admin-fact"><dt>Schedulerwerk-ID</dt><dd className="admin-code">{work.id}</dd></div>
                        <div className="admin-fact"><dt>Aangemaakt</dt><dd>{formatAdminDateTime(work.createdAt)}</dd></div>
                        <div className="admin-fact"><dt>Opgepakt</dt><dd>{formatAdminDateTime(work.claimedAt)}</dd></div>
                        <div className="admin-fact"><dt>Voltooid</dt><dd>{formatAdminDateTime(work.completedAt)}</dd></div>
                        <div className="admin-fact"><dt>Provideractie</dt><dd>{work.providerParkingActionId?<a className="admin-action-link admin-code" href={`#provider-action-${work.providerParkingActionId}`}>Gekoppelde provideractie</a>:"—"}</dd></div>
                        <div className="admin-fact"><dt>Technische status</dt><dd className="admin-code">{work.status}</dd></div>
                      </dl>
                    </div></td></tr>}
                  </Fragment>;
                })}
              </tbody>
            </table>
          </div>}
    </section>
  </>;
}