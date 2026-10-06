import { Fragment,useState } from "react";
import type { AdminVisitSchedulerWorkSummary,AdminVisitTimelineEvent } from "../../api/client";
import {
  adminProcessStatusTone,
  adminStatusClass,
  formatAdminDateTime,
  formatAdminJson,
  formatAdminNumber,
  formatAdminProcessStatus
} from "./adminFieldFormatters";
import { groupTimelineEvents,timelineSourceHref } from "./visitTimeline";

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

function timelineGroupTitle(event:AdminVisitTimelineEvent){
  if(event.attemptNumber!==null)return event.attemptNumber>0?`Poging ${event.attemptNumber}`:"Voorbereiding";
  switch(event.sourceType){
    case "visit": return "Visitstatus";
    case "provider_action": return "Provideractie";
    case "visit_end_time_change": return "Eindtijdwijziging";
    default: return "Schedulerverloop";
  }
}

function toggleSet(current:Set<string>,key:string){
  const next=new Set(current);
  if(next.has(key))next.delete(key);else next.add(key);
  return next;
}

export function AdminVisitSchedulerSections({timelineEvents,schedulerWork}:{timelineEvents:AdminVisitTimelineEvent[];schedulerWork:AdminVisitSchedulerWorkSummary[]}){
  const[openTimelineGroups,setOpenTimelineGroups]=useState<Set<string>>(()=>new Set());
  const[openWorkItems,setOpenWorkItems]=useState<Set<string>>(()=>new Set());
  const groups=groupTimelineEvents(timelineEvents);

  return <>
    <section className="admin-visits__panel admin-visit-detail__section">
      <h2>Schedulerverloop</h2>
      {groups.length===0
        ? <p className="admin-visit-detail__muted">Geen schedulergebeurtenissen vastgelegd.</p>
        : <div className="admin-table-wrap">
            <table className="admin-table admin-table--fixed admin-visit-detail__timeline-table">
              <thead><tr><th>Type</th><th>Tijdstip</th><th>Gebeurtenissen</th><th aria-label="Details"/></tr></thead>
              <tbody>
                {groups.map(group=>{
                  const open=openTimelineGroups.has(group.key);
                  return <Fragment key={group.key}>
                    <tr>
                      <td><strong>{timelineGroupTitle(group.events[0])}</strong></td>
                      <td>{formatAdminDateTime(group.events[0].occurredAt)}</td>
                      <td className="admin-number">{formatAdminNumber(group.events.length)}</td>
                      <td className="admin-table__actions"><button type="button" className="admin-action-link admin-action-link--muted" onClick={()=>setOpenTimelineGroups(current=>toggleSet(current,group.key))}>{open?"Verbergen ▴":"Tonen ▾"}</button></td>
                    </tr>
                    {open&&<tr className="admin-table__detail-row"><td colSpan={4}><div className="admin-table__detail-panel admin-visit-detail__timeline-events">
                      {group.events.map(event=><article className="admin-visit-detail__timeline-event" key={event.id}>
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
                      <td>{work.endReason??"—"}</td>
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