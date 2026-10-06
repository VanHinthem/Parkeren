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

type FunctionalStep={
  key:string;
  label:string;
  reason:string;
  occurredAt:string;
  events:AdminVisitTimelineEvent[];
};

type FunctionalStepDescriptor={
  key:string;
  label:string;
  reason:string;
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

function workType(event:AdminVisitTimelineEvent,workById:Map<string,AdminVisitSchedulerWorkSummary>){
  return workById.get(event.sourceId)?.type??detailText(event,"workType");
}

function operationType(event:AdminVisitTimelineEvent){
  return detailText(event,"type")?.toLowerCase()??null;
}

function statusValue(event:AdminVisitTimelineEvent){
  return detailText(event,"status")?.toLowerCase()??null;
}

function stateValue(event:AdminVisitTimelineEvent){
  return detailText(event,"state")?.toLowerCase()??null;
}

function stopReason(event:AdminVisitTimelineEvent){
  const reason=detailText(event,"endReason");
  if(reason==="DesiredEndReached")return "Geplande eindtijd bereikt";
  if(reason==="ManualStop")return "Visit handmatig gestopt";
  return reason?`Stopreden: ${reason}`:"Stop van de Visit uitvoeren";
}

function stepDescriptor(
  event:AdminVisitTimelineEvent,
  phase:VisitTimelinePhase,
  workById:Map<string,AdminVisitSchedulerWorkSummary>
):FunctionalStepDescriptor{
  const operation=operationType(event);
  const state=stateValue(event);
  const status=statusValue(event);
  const work=workType(event,workById);
  const extensionPhase=phase.label==="Visit verlengd"||phase.label==="Visit verkort"||phase.label==="Eindtijd gewijzigd"||phase.label==="Open einde ingesteld";

  if(work==="ReconcileProviderAction"||event.eventType==="provider_action.history_changed"||event.eventType==="provider_operation.reconciliation_started"){
    return {
      key:"reconciliation",
      label:"Reconciliatie uitgevoerd",
      reason:"Providerhistorie gecontroleerd en bijgewerkt"
    };
  }

  if(work==="StopVisit"){
    if(event.eventType==="scheduler_work.created"){
      return {
        key:"stop-planned",
        label:"Stop ingepland",
        reason:`Gepland einde van de Visit: ${formatAdminDateTime(detailText(event,"dueAt"))}`
      };
    }
    if(event.eventType==="scheduler_work.completed"){
      return {
        key:"provider-stopped",
        label:"Provideractie gestopt",
        reason:"Stop bevestigd en Visit afgerond"
      };
    }
    return {
      key:"stop-executed",
      label:"Stop uitgevoerd",
      reason:stopReason(event)
    };
  }

  if(operation==="stop"){
    if(event.eventType==="provider_operation.succeeded"){
      return {
        key:"provider-stopped",
        label:"Provideractie gestopt",
        reason:"Stop bevestigd door provider"
      };
    }
    return {
      key:"stop-executed",
      label:"Stop uitgevoerd",
      reason:"Stopactie naar provider uitgevoerd"
    };
  }

  if(event.sourceType==="provider_action"&&(state==="stopping"||state==="stopped"||state==="completed")){
    if(state==="stopped"||state==="completed"){
      return {
        key:"provider-stopped",
        label:"Provideractie gestopt",
        reason:"Stop bevestigd door provider"
      };
    }
    return {
      key:"stop-executed",
      label:"Stop uitgevoerd",
      reason:"Provideractie wordt gestopt"
    };
  }

  if(event.sourceType==="visit"&&event.eventType==="visit.status_changed"){
    if(status==="stopping"){
      return {
        key:"stop-executed",
        label:"Stop uitgevoerd",
        reason:stopReason(event)
      };
    }
    if(status==="completed"||status==="cancelled"){
      return {
        key:"provider-stopped",
        label:"Provideractie gestopt",
        reason:"Visit en provideractie zijn afgerond"
      };
    }
    if(status==="active"){
      return {
        key:extensionPhase?"continuation-active":"provider-active",
        label:extensionPhase?"Vervolgactie gestart":"Provideractie actief",
        reason:extensionPhase?"Providerdekking voor de gewijzigde Visit is actief":"Provider bevestigde de start"
      };
    }
  }

  if(event.eventType.includes("desired_end_change")||event.eventType==="visit.desired_end_changed"){
    return {
      key:"end-time-change",
      label:"Eindtijd gewijzigd",
      reason:"Nieuwe eindtijd op de Visit toegepast"
    };
  }

  if(work==="ContinueProviderCoverage"){
    if(event.eventType==="scheduler_work.created"){
      return {
        key:"continuation-planned",
        label:"Vervolgactie gepland",
        reason:"Providerdekking voor de verlengde Visit gepland"
      };
    }
    if(event.eventType==="scheduler_work.completed"){
      return {
        key:"continuation-active",
        label:"Vervolgactie gestart",
        reason:"Providerdekking voor de verlengde Visit is actief"
      };
    }
    return {
      key:"continuation-starting",
      label:"Vervolgactie starten",
      reason:"Nieuwe providerdekking wordt gestart"
    };
  }

  if(operation==="continuestart"||operation==="extend"){
    if(event.eventType==="provider_operation.succeeded"){
      return {
        key:"continuation-active",
        label:operation==="extend"?"Provideractie verlengd":"Vervolgactie gestart",
        reason:operation==="extend"?"Bestaande provideractie is verlengd":"Nieuwe provideractie is actief"
      };
    }
    return {
      key:"continuation-starting",
      label:operation==="extend"?"Provideractie verlengen":"Vervolgactie starten",
      reason:operation==="extend"?"Verlenging bij provider uitvoeren":"Nieuwe provideractie bij provider starten"
    };
  }

  if(event.sourceType==="provider_action"&&extensionPhase){
    if(state==="planned"||state==="scheduled"||event.eventType==="provider_action.created"){
      return {
        key:"continuation-planned",
        label:"Vervolgactie gepland",
        reason:"Providerdekking voor de gewijzigde Visit gepland"
      };
    }
    if(state==="starting"){
      return {
        key:"continuation-starting",
        label:"Vervolgactie starten",
        reason:"Nieuwe provideractie bij provider starten"
      };
    }
    if(state==="active"){
      return {
        key:"continuation-active",
        label:"Vervolgactie gestart",
        reason:"Nieuwe provideractie is actief"
      };
    }
  }

  if(operation==="start"){
    if(event.eventType==="provider_operation.succeeded"){
      return {
        key:"provider-active",
        label:"Provideractie actief",
        reason:"Provider bevestigde de start"
      };
    }
    return {
      key:"provider-created",
      label:"Provideractie aangemaakt",
      reason:"Parkeeractie bij provider starten"
    };
  }

  if(event.sourceType==="provider_action"){
    if(state==="active"){
      return {
        key:"provider-active",
        label:"Provideractie actief",
        reason:"Provider bevestigde de start"
      };
    }
    if(state==="starting"||event.eventType==="provider_action.created"||event.eventType==="provider_action.timing_changed"){
      return {
        key:"provider-created",
        label:"Provideractie aangemaakt",
        reason:"Parkeeractie bij provider starten"
      };
    }
  }

  if(event.sourceType==="visit"&&event.eventType==="visit.created"){
    return {
      key:"provider-created",
      label:"Provideractie aangemaakt",
      reason:"Parkeeractie bij provider starten"
    };
  }

  return {
    key:`other-${event.id}`,
    label:"Aanvullende verwerking",
    reason:"Technische verwerking van de Visit"
  };
}

function buildFunctionalSteps(
  phase:VisitTimelinePhase,
  workById:Map<string,AdminVisitSchedulerWorkSummary>
):FunctionalStep[]{
  const steps:FunctionalStep[]=[];
  const byKey=new Map<string,FunctionalStep>();

  for(const event of phase.events){
    const descriptor=stepDescriptor(event,phase,workById);
    let step=byKey.get(descriptor.key);
    if(!step){
      step={
        key:`${phase.key}:${descriptor.key}`,
        label:descriptor.label,
        reason:descriptor.reason,
        occurredAt:event.occurredAt,
        events:[]
      };
      byKey.set(descriptor.key,step);
      steps.push(step);
    }
    step.events.push(event);
  }

  return steps;
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
  const[openTechnicalSteps,setOpenTechnicalSteps]=useState<Set<string>>(()=>new Set());
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
              const steps=buildFunctionalSteps(phase,workById);
              return <Fragment key={phase.key}>
                <tr>
                  <td><strong>{phase.label}</strong></td>
                  <td>{formatAdminDateTime(phase.occurredAt)}</td>
                  <td className="admin-number">{formatAdminNumber(steps.length)}</td>
                  <td className="admin-table__actions"><button type="button" className="admin-action-link admin-action-link--muted" onClick={()=>setOpenTimelineGroups(current=>toggleSet(current,phase.key))}>{open?"Verbergen ▴":"Tonen ▾"}</button></td>
                </tr>
                {open&&<tr className="admin-table__detail-row"><td colSpan={4}><div className="admin-table__detail-panel">
                  {steps.length===0
                    ? <p className="admin-visit-detail__muted">Geen stappen vastgelegd voor deze gebeurtenis.</p>
                    : <table className="admin-table admin-table--fixed admin-visit-detail__timeline-steps-table">
                        <thead><tr><th>Stap</th><th>Reden</th><th>Tijdstip</th><th aria-label="Technische details"/></tr></thead>
                        <tbody>{steps.map(step=>{
                          const technicalOpen=openTechnicalSteps.has(step.key);
                          return <Fragment key={step.key}>
                            <tr>
                              <td><strong>{step.label}</strong></td>
                              <td>{step.reason}</td>
                              <td>{formatAdminDateTime(step.occurredAt)}</td>
                              <td className="admin-table__actions"><button type="button" className="admin-action-link admin-action-link--muted" onClick={()=>setOpenTechnicalSteps(current=>toggleSet(current,step.key))}>{technicalOpen?"Verbergen ▴":"Techniek ▾"}</button></td>
                            </tr>
                            {technicalOpen&&<tr className="admin-table__detail-row"><td colSpan={4}><div className="admin-table__detail-panel admin-visit-detail__timeline-events">
                              {step.events.map(event=><article className="admin-visit-detail__timeline-event" key={event.id}>
                                <div className="admin-visit-detail__timeline-event-head"><strong className="admin-code">{event.eventType}</strong><time>{formatAdminDateTime(event.occurredAt)}</time></div>
                                <dl className="admin-facts admin-facts--grid admin-visit-detail__technical-facts">
                                  <div className="admin-fact"><dt>Reden</dt><dd className="admin-code">{event.reasonCode}</dd></div>
                                  <div className="admin-fact"><dt>Bron</dt><dd className="admin-code">{event.sourceType}</dd></div>
                                  <div className="admin-fact"><dt>Record-ID</dt><dd className="admin-code">{timelineSourceHref(event)?<a className="admin-action-link" href={timelineSourceHref(event)!}>{event.sourceId}</a>:event.sourceId}</dd></div>
                                  <div className="admin-fact"><dt>Groep</dt><dd className="admin-code">{event.groupKey}</dd></div>
                                </dl>
                                {event.detailsJson&&<pre className="admin-code-block">{formatAdminJson(event.detailsJson)}</pre>}
                              </article>)}
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