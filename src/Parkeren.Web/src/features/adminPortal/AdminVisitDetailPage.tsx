import { Fragment,lazy,Suspense,useEffect,useState } from "react";
import {
  getAdminVisit,
  stopVisit,
  type AdminVisitDetail
} from "../../api/client";
import { LicensePlate } from "../../components/LicensePlate";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import { clearPendingOperation,getOrCreatePendingOperation } from "../../pendingOperations";
import {
  adminChangeResultTone,
  adminOperationalHealthTone,
  adminProcessStatusTone,
  adminProviderActionStatusTone,
  adminStatusClass,
  formatAdminBoolean,
  formatAdminChangeResult,
  formatAdminDateTime,
  formatAdminDuration,
  formatAdminNumber,
  formatAdminOperationalHealth,
  formatAdminParkingContinuation,
  formatAdminProcessStatus,
  formatAdminProviderActionStatus,
  formatAdminVisitStatus,
  type AdminOperationalHealth
} from "./adminFieldFormatters";
import "./AdminVisits.css";

const AdminVisitSchedulerSections=lazy(()=>
  import("./AdminVisitSchedulerSections").then(module=>({default:module.AdminVisitSchedulerSections}))
);

function providerProductLabel(name:string|null,id:string|null){
  if(name&&id)return `${name} (${id})`;
  return name??id??"—";
}

function operationalHealth(value:AdminOperationalHealth){
  return <span className={adminStatusClass(adminOperationalHealthTone(value))}>{formatAdminOperationalHealth(value)}</span>;
}

function dateRange(start:string|null,end:string|null,emptyStart="—",emptyEnd="—"){
  return <span className="admin-visit-detail__time-range">
    <span>{formatAdminDateTime(start,emptyStart)}</span>
    <span>{formatAdminDateTime(end,emptyEnd)}</span>
  </span>;
}

function toggleSet(current:Set<string>,key:string){
  const next=new Set(current);
  if(next.has(key))next.delete(key);else next.add(key);
  return next;
}

export function AdminVisitDetailPage({visitId}:{visitId:string}){
  const[detail,setDetail]=useState<AdminVisitDetail|null|undefined>();
  const[error,setError]=useState<string>();
  const[message,setMessage]=useState<string>();
  const[stopping,setStopping]=useState(false);
  const[openProviderOperations,setOpenProviderOperations]=useState<Set<string>>(()=>new Set());

  async function load(){
    setError(undefined);
    try{
      setDetail(await getAdminVisit(visitId));
    }catch(e){
      setError(e instanceof Error?e.message:"Visit-detail kon niet worden geladen.");
      setDetail(null);
    }
  }

  useEffect(()=>{void load();},[visitId]);

  async function handleStop(){
    if(!detail||detail.visit.status!=="Active"||stopping)return;
    setStopping(true);
    setError(undefined);
    setMessage(undefined);
    const operationId=getOrCreatePendingOperation("stop",detail.visit.id);
    try{
      const result=await stopVisit(detail.visit.id,operationId);
      if(!result.reconciliationRequired)clearPendingOperation("stop",detail.visit.id);
      setMessage(result.reconciliationRequired
        ?"Stoppen is aangevraagd; de providerbevestiging loopt nog."
        :"Parkeerbezoek is gestopt.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Parkeerbezoek kon niet worden gestopt.");
    }finally{
      setStopping(false);
    }
  }

  if(detail===undefined)return <Loading label="Visit-detail laden"/>;

  return <div className="admin-visit-detail">
    <div className="admin-visit-detail__toolbar">
      <a className="admin-action-link admin-action-link--muted admin-visit-detail__back" href="/beheer/bezoeken">← Terug naar bezoeken</a>
      {detail?.visit.status==="Active"&&
        <Button className="admin-action--compact" variant="secondary" onClick={()=>void handleStop()} disabled={stopping}>
          {stopping?"Stoppen…":"Visit stoppen"}
        </Button>}
    </div>

    {error&&<Alert tone="danger">{error}</Alert>}
    {message&&<Alert>{message}</Alert>}

    {!detail
      ? <section className="admin-visits__panel"><p className="admin-visit-detail__muted">Deze Visit kon niet worden gevonden.</p></section>
      : <>
          <section className="admin-visits__panel">
            <dl className="admin-visit-detail__facts">
              <div className="admin-visit-detail__fact"><dt>Bezoeker</dt><dd><span className="admin-identity"><strong>{detail.visit.username}</strong></span></dd></div>
              <div className="admin-visit-detail__fact"><dt>Kenteken</dt><dd><LicensePlate value={detail.visit.licensePlate}/></dd></div>
              <div className="admin-visit-detail__fact"><dt>Status</dt><dd className="admin-visit-detail__status-stack"><span className={adminStatusClass(adminProviderActionStatusTone(detail.visit.status))}>{formatAdminVisitStatus(detail.visit.status)}</span>{operationalHealth(detail.visit.health)}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Gestart door</dt><dd><span className="admin-identity"><strong>{detail.visit.startedByUsername}</strong></span></dd></div>
              <div className="admin-visit-detail__fact"><dt>Gestart</dt><dd>{formatAdminDateTime(detail.visit.startAt)}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Gepland tot</dt><dd>{formatAdminDateTime(detail.visit.desiredEndAt,"Handmatig stoppen")}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Werkelijk einde</dt><dd>{formatAdminDateTime(detail.visit.actualEndAt)}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Betaalde tijd</dt><dd className="admin-duration">{formatAdminDuration(detail.visit.paidDurationMinutes)}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Providerproduct</dt><dd>{providerProductLabel(detail.providerProductName,detail.providerProductExternalId)}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Providerlocatie</dt><dd className="admin-code">{detail.providerLocation??"—"}</dd></div>
            </dl>
          </section>

          <section className="admin-visits__panel admin-visit-detail__section">
            <h2>Policy snapshot</h2>
            <dl className="admin-visit-detail__facts admin-visit-detail__facts--policy">
              <div className="admin-visit-detail__fact"><dt>Max. betaalde tijd</dt><dd className="admin-duration">{formatAdminDuration(detail.policySnapshot.maxPaidParkingDurationMinutes,"Onbeperkt")}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Max. Visitduur</dt><dd className="admin-duration">{formatAdminDuration(detail.policySnapshot.maxVisitElapsedDurationMinutes,"Onbeperkt")}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Verlengen toegestaan</dt><dd>{formatAdminBoolean(detail.policySnapshot.allowVisitExtension)}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Open einde toegestaan</dt><dd>{formatAdminBoolean(detail.policySnapshot.allowOpenEndedVisits)}</dd></div>
            </dl>
          </section>

          <section className="admin-visits__panel admin-visit-detail__section">
            <h2>Provideracties</h2>
            {detail.providerActions.length===0
              ? <p className="admin-visit-detail__muted">Geen provideracties vastgelegd.</p>
              : <div className="admin-table-wrap"><table className="admin-table admin-table--fixed admin-visit-detail__provider-actions-table">
                  <thead><tr><th>Provideractie</th><th>Gepland</th><th>Werkelijk</th><th>Status</th><th>Provider</th></tr></thead>
                  <tbody>{detail.providerActions.map(action=><tr id={`provider-action-${action.id}`} key={action.id}>
                    <td><span className="admin-code admin-code--table">{action.providerActionId??"Nog geen provider action-ID"}</span></td>
                    <td>{dateRange(action.plannedStartAt,action.plannedEndAt)}</td>
                    <td>{dateRange(action.actualStartAt,action.actualEndAt)}</td>
                    <td><span className="admin-visit-detail__status-stack"><span className={adminStatusClass(adminProviderActionStatusTone(action.state))}>{formatAdminProviderActionStatus(action.state)}</span>{operationalHealth(action.health)}</span></td>
                    <td><span className="admin-identity"><strong>{action.providerStatus??"—"}</strong><small>{action.providerProductId??"—"} · {action.providerLocation??"—"}</small></span></td>
                  </tr>)}</tbody>
                </table></div>}
          </section>

          <Suspense fallback={<Loading label="Visitverloop laden"/>}>
            <AdminVisitSchedulerSections
              visit={detail.visit}
              endTimeChanges={detail.endTimeChanges}
              timelineEvents={detail.timelineEvents}
              schedulerWork={detail.schedulerWork}
            />
          </Suspense>

          <section className="admin-visits__panel admin-visit-detail__section">
            <h2>Provideroperations</h2>
            {detail.providerOperations.length===0
              ? <p className="admin-visit-detail__muted">Geen provideroperations vastgelegd.</p>
              : <div className="admin-table-wrap"><table className="admin-table admin-table--fixed admin-visit-detail__provider-operations-table">
                  <thead><tr><th>Type</th><th>Aangemaakt</th><th>Pogingen</th><th>Status</th><th>Voltooid</th><th aria-label="Details"/></tr></thead>
                  <tbody>{detail.providerOperations.map(operation=>{
                    const open=openProviderOperations.has(operation.id);
                    return <Fragment key={operation.id}>
                      <tr id={`provider-operation-${operation.id}`}>
                        <td><strong>{operation.type}</strong></td>
                        <td>{formatAdminDateTime(operation.createdAt)}</td>
                        <td className="admin-number admin-number--table">{formatAdminNumber(operation.attemptCount)}</td>
                        <td><span className={adminStatusClass(adminProcessStatusTone(operation.status))}>{formatAdminProcessStatus(operation.status)}</span></td>
                        <td>{formatAdminDateTime(operation.completedAt)}</td>
                        <td className="admin-table__actions"><button type="button" className="admin-action-link admin-action-link--muted" onClick={()=>setOpenProviderOperations(current=>toggleSet(current,operation.id))}>{open?"Verbergen ▴":"Tonen ▾"}</button></td>
                      </tr>
                      {open&&<tr className="admin-table__detail-row"><td colSpan={6}><div className="admin-table__detail-panel">
                        <dl className="admin-facts admin-facts--grid admin-visit-detail__technical-facts">
                          <div className="admin-fact"><dt>Operation-ID</dt><dd className="admin-code">{operation.operationId}</dd></div>
                          <div className="admin-fact"><dt>Provideractie</dt><dd>{operation.providerParkingActionId?<a className="admin-action-link admin-code" href={`#provider-action-${operation.providerParkingActionId}`}>Gekoppelde provideractie</a>:"—"}</dd></div>
                          <div className="admin-fact"><dt>Parent operation</dt><dd className="admin-code">{operation.parentOperationId??"—"}</dd></div>
                          <div className="admin-fact"><dt>Poging gestart</dt><dd>{formatAdminDateTime(operation.attemptStartedAt)}</dd></div>
                          <div className="admin-fact"><dt>Aangevraagd einde</dt><dd>{formatAdminDateTime(operation.requestedEndAt)}</dd></div>
                          <div className="admin-fact"><dt>Foutcode</dt><dd className="admin-code">{operation.lastErrorCode??"—"}</dd></div>
                        </dl>
                      </div></td></tr>}
                    </Fragment>;
                  })}</tbody>
                </table></div>}
          </section>

          <section className="admin-visits__panel admin-visit-detail__section">
            <h2>Eindtijdwijzigingen</h2>
            {detail.endTimeChanges.length===0
              ? <p className="admin-visit-detail__muted">Geen eindtijdwijzigingen vastgelegd.</p>
              : <div className="admin-table-wrap"><table className="admin-table admin-table--fixed admin-visit-detail__end-time-table">
                  <thead><tr><th>Tijdstip</th><th>Actor</th><th>Van</th><th>Naar</th><th>Resultaat</th></tr></thead>
                  <tbody>{detail.endTimeChanges.map(change=><tr key={change.id}>
                    <td>{formatAdminDateTime(change.createdAt)}</td>
                    <td><span className="admin-identity"><strong>{change.actorUsername}</strong></span></td>
                    <td>{formatAdminDateTime(change.previousDesiredEndAt,"Open einde")}</td>
                    <td>{formatAdminDateTime(change.requestedDesiredEndAt,"Open einde")}</td>
                    <td><span className={adminStatusClass(adminChangeResultTone(change.result))}>{formatAdminChangeResult(change.result)}</span></td>
                  </tr>)}</tbody>
                </table></div>}
          </section>

          <section className="admin-visits__panel admin-visit-detail__section">
            <h2>Relevante parkeerregelversies</h2>
            {detail.relevantRuleSets.length===0
              ? <p className="admin-visit-detail__muted">Geen parkeerregelversie gevonden voor deze periode.</p>
              : <div className="admin-table-wrap"><table className="admin-table admin-table--fixed admin-visit-detail__rule-sets-table">
                  <thead><tr><th>Geldig vanaf</th><th>Geldig tot</th><th>Max. provideractie</th><th>Continuation</th><th>Feestdagen</th></tr></thead>
                  <tbody>{detail.relevantRuleSets.map(rule=><tr key={rule.id}>
                    <td>{formatAdminDateTime(rule.validFrom)}</td>
                    <td>{formatAdminDateTime(rule.validUntil,"Doorlopend")}</td>
                    <td className="admin-duration">{formatAdminDuration(rule.maxProviderActionDurationMinutes)}</td>
                    <td>{formatAdminParkingContinuation(rule.continuation)}</td>
                    <td>{formatAdminBoolean(rule.publicHolidaysAreFree)}</td>
                  </tr>)}</tbody>
                </table></div>}
          </section>
        </>}
  </div>;
}