import { Fragment,useState } from "react";
import type {
  AdminVisitEndTimeChangeSummary,
  AdminVisitSchedulerWorkSummary,
  AdminVisitSummary,
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

type VisitTimelinePhase={
  key:string;
  label:string;
  occurredAt:string;
  events:AdminVisitTimelineEvent[];
};

function eventDetails(event:AdminVisitTimelineEvent):Record<string,unknown>{
  if(!event.detailsJson)return {};
  try{
    const value=JSON.parse(event.detailsJson) as unknown;
    return value!==null&&typeof value==="object"&&!Array.isArray(value)?value as Record<string,unknown>:{};
  }catch{
    return {};
  }
}

function detailText(event:AdminVisitTimelineEvent,key:string){
  const value=eventDetails(event)[key];
  return typeof value==="string"?value:null;
}

function providerActionStep(state:string|null){
  switch(state?.toLowerCase()){
    case "planned":
    case "scheduled": return "Provideractie gepland";
    case "starting": return "Provideractie starten";
    case "active": return "Provideractie gestart";
    case "stopping": return "Provideractie stoppen";
    case "stopped":
    case "completed": return "Provideractie gestopt";
    case "failed": return "Provideractie mislukt";
    case "cancelled": return "Provideractie geannuleerd";
    default: return null;
  }
}

function providerOperationStep(event:AdminVisitTimelineEvent){
  const type=detailText(event,"type")?.toLowerCase();
  const outcome=event.eventType.split(".").at(-1);

  if(event.eventType==="provider_operation.reconciliation_started")return "Providerreconciliatie gestart";

  if(type==="start"){
    if(outcome==="created")return "Providerstart voorbereid";
    if(outcome==="attempt_started")return "Provideractie starten";
    if(outcome==="succeeded")return "Provideractie gestart";
    if(outcome==="failed")return "Providerstart mislukt";
  }

  if(type==="stop"){
    if(outcome==="created")return "Providerstop voorbereid";
    if(outcome==="attempt_started")return "Provideractie stoppen";
    if(outcome==="succeeded")return "Provideractie gestopt";
    if(outcome==="failed")return "Providerstop mislukt";
  }

  if(type==="continuestart"){
    if(outcome==="created")return "Vervolgactie voorbereid";
    if(outcome==="attempt_started")return "Vervolgactie starten";
    if(outcome==="succeeded")return "Vervolgactie gestart";
    if(outcome==="failed")return "Vervolgactie mislukt";
  }

  if(type==="extend"){
    if(outcome==="created")return "Providerverlenging voorbereid";
    if(outcome==="attempt_started")return "Provideractie verlengen";
    if(outcome==="succeeded")return "Provideractie verlengd";
    if(outcome==="failed")return "Providerverlenging mislukt";
  }

  if(outcome==="succeeded")return "Provideroperatie afgerond";
  if(outcome==="failed")return "Provideroperatie mislukt";
  if(outcome==="outcome_unknown")return "Provideruitkomst onbekend";
  if(outcome==="retry_ready")return "Provideroperatie opnieuw klaarzetten";
  if(outcome==="attempt_started")return "Provideroperatie uitvoeren";
  return "Provideroperatie voorbereid";
}

function schedulerStep(event:AdminVisitTimelineEvent,workById:Map<string,AdminVisitSchedulerWorkSummary>){
  const work=workById.get(event.sourceId);
  const outcome=event.eventType.split(".").at(-1);

  switch(work?.type){
    case "StopVisit":
      if(outcome==="created")return "Stop gepland";
      if(outcome==="claimed")return "Stop uitvoeren";
      if(outcome==="completed")return "Stop afgerond";
      if(outcome==="cancelled")return "Stopplanning geannuleerd";
      return "Stop opnieuw ingepland";
    case "ContinueProviderCoverage":
      if(outcome==="created")return "Vervolgactie gepland";
      if(outcome==="claimed")return "Vervolgactie uitvoeren";
      if(outcome==="completed")return "Vervolgactie afgerond";
      if(outcome==="cancelled")return "Vervolgactie geannuleerd";
      return "Vervolgactie opnieuw ingepland";
    case "ReconcileProviderAction":
      if(outcome==="created")return "Reconciliatie gepland";
      if(outcome==="claimed")return "Reconciliatie uitvoeren";
      if(outcome==="completed")return "Provider gereconcilieerd";
      if(outcome==="cancelled")return "Reconciliatie geannuleerd";
      return "Reconciliatie opnieuw ingepland";
    case "LongVisitWarning":
      return outcome==="completed"?"Long-Visit waarschuwing verwerkt":"Long-Visit waarschuwing gepland";
    default:
      return "Scheduleractie";
  }
}

function functionalStepTitle(event:AdminVisitTimelineEvent,workById:Map<string,AdminVisitSchedulerWorkSummary>){
  if(event.sourceType==="provider_action"){
    if(event.eventType==="provider_action.state_changed")return providerActionStep(detailText(event,"state"))??"Provideractiestatus gewijzigd";
    if(event.eventType==="provider_action.created")return providerActionStep(detailText(event,"state"))??"Provideractie gepland";
    if(event.eventType==="provider_action.timing_changed")return "Provideractie planning bijgewerkt";
    if(event.eventType==="provider_action.health_changed")return "Provideractie gezondheid bijgewerkt";
    if(event.eventType==="provider_action.history_changed")return "Providerstatus bijgewerkt";
  }

  if(event.sourceType==="provider_operation")return providerOperationStep(event);
  if(event.sourceType==="scheduler_work")return schedulerStep(event,workById);

  switch(event.eventType){
    case "visit.created": return "Visit aangemaakt";
    case "visit.status_changed": return "Visitstatus gewijzigd";
    case "visit.health_changed": return "Visitgezondheid gewijzigd";
    case "visit.desired_end_change_requested": return "Nieuwe eindtijd aangevraagd";
    case "visit.desired_end_change_rejected": return "Eindtijdwijziging afgewezen";
    case "visit.desired_end_change_applied":
    case "visit.desired_end_changed": return "Nieuwe eindtijd toegepast";
    default: return event.reasonCode.replaceAll("_"," ");
  }
}

function sourceLabel(event:AdminVisitTimelineEvent){
  switch(event.sourceType){
    case "provider_action": return "Provideractie";
    case "provider_operation": return "Provideroperatie";
    case "scheduler_work": return "Scheduler";
    case "visit": return "Visit";
    case "visit_end_time_change": return "Eindtijdwijziging";
    default: return event.sourceType;
  }
}

function endTimeChangeLabel(change:AdminVisitEndTimeChangeSummary){
  const previous=change.previousDesiredEndAt?new Date(change.previousDesiredEndAt).getTime():null;
  const requested=change.requestedDesiredEndAt?new Date(change.requestedDesiredEndAt).getTime():null;
  if(requested!==null&&previous!==null&&requested>previous)return "Visit verlengd";
  if(requested!==null&&previous!==null&&requested<previous)return "Visit verkort";
  if(requested===null)return "Open einde ingesteld";
  return "Eindtijd gewijzigd";
}

function buildVisitTimeline(
  visit:AdminVisitSummary,
  endTimeChanges:AdminVisitEndTimeChangeSummary[],
  events:AdminVisitTimelineEvent[]
):VisitTimelinePhase[]{
  const phases:VisitTimelinePhase[]=[{
    key:"visit-start",
    label:"Visit gestart",
    occurredAt:visit.startAt,
    events:[]
  }];

  for(const change of endTimeChanges.filter(change=>change.result==="Applied")){
    phases.push({
      key:`end-time-${change.id}`,
      label:endTimeChangeLabel(change),
      occurredAt:change.createdAt,
      events:[]
    });
  }

  if(visit.actualEndAt){
    phases.push({
      key:"visit-stop",
      label:"Visit gestopt",
      occurredAt:visit.actualEndAt,
      events:[]
    });
  }

  phases.sort((left,right)=>new Date(left.occurredAt).getTime()-new Date(right.occurredAt).getTime());

  for(const event of [...events].sort((left,right)=>
    new Date(left.occurredAt).getTime()-new Date(right.occurredAt).getTime()||
    left.eventOrder-right.eventOrder||
    left.id.localeCompare(right.id)
  )){
    const eventTime=new Date(event.occurredAt).getTime();
    let target=phases[0];
    let distance=Math.abs(eventTime-new Date(target.occurredAt).getTime());

    for(const phase of phases.slice(1)){
      const candidateDistance=Math.abs(eventTime-new Date(phase.occurredAt).getTime());
      if(candidateDistance<distance){
        target=phase;
        distance=candidateDistance;
      }
    }

    target.events.push(event);
  }

  return phases;
}

function toggleSet(current:Set<string>,key:string){
  const next=new Set(current);
  if(next.has(key))next.delete(key);else next.add(key);
  return next;
}

export function AdminVisitSchedulerSections({
  visit,
  endTimeChanges,
  timelineEvents,
  schedulerWork
}:{
  visit:AdminVisitSummary;
  endTimeChanges:AdminVisitEndTimeChangeSummary[];
  timelineEvents:AdminVisitTimelineEvent[];
  schedulerWork:AdminVisitSchedulerWorkSummary[];
}){
  const[openTimelineGroups,setOpenTimelineGroups]=useState<Set<string>>(()=>new Set());
  const[openTechnicalEvents,setOpenTechnicalEvents]=useState<Set<string>>(()=>new Set());
  const[openWorkItems,setOpenWorkItems]=useState<Set<string>>(()=>new Set());
  const phases=buildVisitTimeline(visit,endTimeChanges,timelineEvents);
  const workById=new Map(schedulerWork.map(work=>[work.id,work]));

  return <>
    <section className="admin-visits__panel admin-visit-detail__section">
      <h2>Visitverloop</h2>
      <div className="admin-table-wrap">
        <table className="admin-table admin-table--fixed admin-visit-detail__timeline-table">
          <thead><tr><th>Gebeurtenis</th><th>Tijdstip</th><th>Stappen</th><th aria-label="Details"/></tr></thead>
          <tbody>
            {phases.map(phase=>{
              const open=openTimelineGroups.has(phase.key);
              return <Fragment key={phase.key}>
                <tr>
                  <td><strong>{phase.label}</strong></td>
                  <td>{formatAdminDateTime(phase.occurredAt)}</td>
                  <td className="admin-number">{formatAdminNumber(phase.events.length)}</td>
                  <td className="admin-table__actions"><button type="button" className="admin-action-link admin-action-link--muted" onClick={()=>setOpenTimelineGroups(current=>toggleSet(current,phase.key))}>{open?"Verbergen ▴":"Tonen ▾"}</button></td>
                </tr>
                {open&&<tr className="admin-table__detail-row"><td colSpan={4}><div className="admin-table__detail-panel">
                  {phase.events.length===0
                    ? <p className="admin-visit-detail__muted">Geen technische stappen vastgelegd voor deze gebeurtenis.</p>
                    : <table className="admin-table admin-table--fixed admin-visit-detail__timeline-steps-table">
                        <thead><tr><th>Stap</th><th>Tijdstip</th><th>Bron</th><th aria-label="Technische details"/></tr></thead>
                        <tbody>{phase.events.map(event=>{
                          const technicalOpen=openTechnicalEvents.has(event.id);
                          return <Fragment key={event.id}>
                            <tr>
                              <td><strong>{functionalStepTitle(event,workById)}</strong></td>
                              <td>{formatAdminDateTime(event.occurredAt)}</td>
                              <td>{sourceLabel(event)}</td>
                              <td className="admin-table__actions"><button type="button" className="admin-action-link admin-action-link--muted" onClick={()=>setOpenTechnicalEvents(current=>toggleSet(current,event.id))}>{technicalOpen?"Verbergen ▴":"Techniek ▾"}</button></td>
                            </tr>
                            {technicalOpen&&<tr className="admin-table__detail-row"><td colSpan={4}><div className="admin-table__detail-panel">
                              <dl className="admin-facts admin-facts--grid admin-visit-detail__technical-facts">
                                <div className="admin-fact"><dt>Reden</dt><dd className="admin-code">{event.reasonCode}</dd></div>
                                <div className="admin-fact"><dt>Bron</dt><dd className="admin-code">{event.sourceType}</dd></div>
                                <div className="admin-fact"><dt>Record-ID</dt><dd className="admin-code">{timelineSourceHref(event)?<a className="admin-action-link" href={timelineSourceHref(event)!}>{event.sourceId}</a>:event.sourceId}</dd></div>
                                <div className="admin-fact"><dt>Event type</dt><dd className="admin-code">{event.eventType}</dd></div>
                              </dl>
                              {event.detailsJson&&<pre className="admin-code-block">{formatAdminJson(event.detailsJson)}</pre>}
                            </div></td></tr>}
                          </Fragment>;
                        })}</tbody>
                      </table>}
                </div></td></tr>}
              </Fragment>;
            })}
          </tbody>
        </table>
      </div>
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