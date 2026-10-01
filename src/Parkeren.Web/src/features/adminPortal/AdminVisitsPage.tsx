import { useEffect,useState,type FormEvent } from "react";
import {
  getAdminVisit,
  getAdminVisits,
  getUsers,
  stopVisit,
  type AdminVisitDetail,
  type AdminVisitFilters,
  type AdminVisitSummary,
  type UserSummary
} from "../../api/client";
import { LicensePlate } from "../../components/LicensePlate";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import { clearPendingOperation,getOrCreatePendingOperation } from "../../pendingOperations";
import "./AdminVisits.css";

type AdminVisitStatus="Starting"|"Active"|"Stopping"|"Completed"|"Cancelled";
type AdminVisitStatusFilter=AdminVisitStatus|"";

function formatDateTime(value:string|null){
  return value
    ? new Date(value).toLocaleString("nl-NL",{dateStyle:"short",timeStyle:"short"})
    : "—";
}

function formatMinutes(value:number|null){
  if(value===null)return "Niet beschikbaar";
  if(value<60)return `${value} min`;
  const hours=Math.floor(value/60);
  const minutes=value%60;
  return minutes===0?`${hours} u`:`${hours} u ${minutes} min`;
}

function statusLabel(status:AdminVisitStatus){
  switch(status){
    case "Starting": return "Wordt gestart";
    case "Active": return "Actief";
    case "Stopping": return "Wordt gestopt";
    case "Completed": return "Afgerond";
    case "Cancelled": return "Geannuleerd";
  }
}

function localDayStart(value:string){
  return value?new Date(`${value}T00:00:00`).toISOString():undefined;
}

function localDayEnd(value:string){
  return value?new Date(`${value}T23:59:59.999`).toISOString():undefined;
}

function visitStatusClass(visit:AdminVisitSummary){
  if(visit.health!=="Healthy")return "admin-visits__status admin-visits__status--warning";
  if(visit.status==="Active")return "admin-visits__status admin-visits__status--active";
  return "admin-visits__status";
}

export function AdminVisitsPage(){
  const[users,setUsers]=useState<UserSummary[]>([]);
  const[visits,setVisits]=useState<AdminVisitSummary[]>();
  const[userId,setUserId]=useState("");
  const[licensePlate,setLicensePlate]=useState("");
  const[from,setFrom]=useState("");
  const[to,setTo]=useState("");
  const[status,setStatus]=useState<AdminVisitStatusFilter>("");
  const[error,setError]=useState<string>();
  const[loading,setLoading]=useState(true);

  async function load(filters:AdminVisitFilters={}){
    setLoading(true);
    setError(undefined);
    try{
      setVisits(await getAdminVisits(filters));
    }catch(e){
      setError(e instanceof Error?e.message:"Parkeerhistorie kon niet worden geladen.");
      setVisits([]);
    }finally{
      setLoading(false);
    }
  }

  useEffect(()=>{
    getUsers().then(setUsers).catch(()=>setUsers([]));
    void load();
  },[]);

  function submit(event:FormEvent){
    event.preventDefault();
    void load({
      userId:userId||undefined,
      licensePlate:licensePlate.trim()||undefined,
      from:localDayStart(from),
      to:localDayEnd(to),
      status:status||undefined
    });
  }

  function reset(){
    setUserId("");
    setLicensePlate("");
    setFrom("");
    setTo("");
    setStatus("");
    void load();
  }

  return <div className="admin-visits">
    <nav className="admin-visits__tabs" aria-label="Bezoeken">
      <a className="admin-visits__tab active" href="/beheer/bezoeken">Bezoeken</a>
      <a className="admin-visits__tab" href="/beheer/verbruik">Verbruik & kosten</a>
      <a className="admin-visits__tab" href="/beheer/analyse">Analyse</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}

    <section className="admin-visits__panel">
      <form className="admin-visits__filters" onSubmit={submit}>
        <label className="admin-visits__field">
          <span>Bezoeker</span>
          <select value={userId} onChange={event=>setUserId(event.target.value)}>
            <option value="">Alle bezoekers</option>
            {users.map(user=><option key={user.id} value={user.id}>{user.username}</option>)}
          </select>
        </label>

        <label className="admin-visits__field">
          <span>Kenteken</span>
          <input value={licensePlate} onChange={event=>setLicensePlate(event.target.value.toUpperCase())} placeholder="Bijv. 12ABC3"/>
        </label>

        <label className="admin-visits__field">
          <span>Vanaf</span>
          <input type="date" value={from} onChange={event=>setFrom(event.target.value)}/>
        </label>

        <label className="admin-visits__field">
          <span>Tot en met</span>
          <input type="date" value={to} onChange={event=>setTo(event.target.value)}/>
        </label>

        <label className="admin-visits__field">
          <span>Status</span>
          <select value={status} onChange={event=>setStatus(event.target.value as AdminVisitStatusFilter)}>
            <option value="">Alle statussen</option>
            <option value="Starting">Wordt gestart</option>
            <option value="Active">Actief</option>
            <option value="Stopping">Wordt gestopt</option>
            <option value="Completed">Afgerond</option>
            <option value="Cancelled">Geannuleerd</option>
          </select>
        </label>

        <div className="admin-visits__filter-actions">
          <Button type="submit">Filteren</Button>
          <Button type="button" variant="secondary" onClick={reset}>Wissen</Button>
        </div>
      </form>
    </section>

    <section className="admin-visits__panel">
      {loading
        ? <Loading label="Parkeerhistorie laden"/>
        : !visits||visits.length===0
          ? <p className="admin-visits__empty">Geen bezoeken gevonden voor deze filters.</p>
          : <div className="admin-visits__table-wrap">
              <table className="admin-visits__table">
                <thead>
                  <tr>
                    <th>Bezoeker</th>
                    <th>Kenteken</th>
                    <th>Gestart</th>
                    <th>Geëindigd</th>
                    <th>Betaalde tijd</th>
                    <th>Status</th>
                    <th aria-label="Details"/>
                  </tr>
                </thead>
                <tbody>
                  {visits.map(visit=><tr key={visit.id}>
                    <td>
                      <span className="admin-visits__identity">
                        <strong>{visit.username}</strong>
                        {visit.startedByUserId!==visit.userId&&<small>Gestart door {visit.startedByUsername}</small>}
                      </span>
                    </td>
                    <td><LicensePlate value={visit.licensePlate}/></td>
                    <td>{formatDateTime(visit.startAt)}</td>
                    <td>{formatDateTime(visit.actualEndAt)}</td>
                    <td>{formatMinutes(visit.paidDurationMinutes)}</td>
                    <td><span className={visitStatusClass(visit)}>{statusLabel(visit.status)}</span></td>
                    <td><a className="admin-visits__link" href={`/beheer/bezoeken/${visit.id}`}>Openen</a></td>
                  </tr>)}
                </tbody>
              </table>
            </div>}
    </section>
  </div>;
}

function booleanLabel(value:boolean){return value?"Ja":"Nee";}

export function AdminVisitDetailPage({visitId}:{visitId:string}){
  const[detail,setDetail]=useState<AdminVisitDetail|null|undefined>();
  const[error,setError]=useState<string>();
  const[message,setMessage]=useState<string>();
  const[stopping,setStopping]=useState(false);

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
      <a className="admin-visit-detail__back" href="/beheer/bezoeken">← Terug naar bezoeken</a>
      {detail?.visit.status==="Active"&&
        <Button variant="secondary" onClick={()=>void handleStop()} disabled={stopping}>
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
              <div className="admin-visit-detail__fact"><dt>Bezoeker</dt><dd>{detail.visit.username}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Kenteken</dt><dd><LicensePlate value={detail.visit.licensePlate}/></dd></div>
              <div className="admin-visit-detail__fact"><dt>Status</dt><dd>{statusLabel(detail.visit.status)} · {detail.visit.health}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Gestart door</dt><dd>{detail.visit.startedByUsername}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Gestart</dt><dd>{formatDateTime(detail.visit.startAt)}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Gepland tot</dt><dd>{detail.visit.desiredEndAt?formatDateTime(detail.visit.desiredEndAt):"Handmatig stoppen"}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Werkelijk einde</dt><dd>{formatDateTime(detail.visit.actualEndAt)}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Betaalde tijd</dt><dd>{formatMinutes(detail.visit.paidDurationMinutes)}</dd></div>
            </dl>
          </section>

          <section className="admin-visits__panel admin-visit-detail__section">
            <h2>Policy snapshot</h2>
            <dl className="admin-visit-detail__facts">
              <div className="admin-visit-detail__fact"><dt>Max. betaalde tijd</dt><dd>{formatMinutes(detail.policySnapshot.maxPaidParkingDurationMinutes)}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Max. Visitduur</dt><dd>{formatMinutes(detail.policySnapshot.maxVisitElapsedDurationMinutes)}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Verlengen toegestaan</dt><dd>{booleanLabel(detail.policySnapshot.allowVisitExtension)}</dd></div>
              <div className="admin-visit-detail__fact"><dt>Open einde toegestaan</dt><dd>{booleanLabel(detail.policySnapshot.allowOpenEndedVisits)}</dd></div>
            </dl>
          </section>

          <section className="admin-visits__panel admin-visit-detail__section">
            <h2>Provideracties</h2>
            {detail.providerActions.length===0
              ? <p className="admin-visit-detail__muted">Geen provideracties vastgelegd.</p>
              : <div className="admin-visit-detail__list">
                  {detail.providerActions.map(action=><div className="admin-visit-detail__row" key={action.id}>
                    <div className="admin-visit-detail__row-head">
                      <strong>{action.providerActionId??"Nog geen provider action-ID"}</strong>
                      <span>{action.state} · {action.health}</span>
                    </div>
                    <div className="admin-visit-detail__row-meta">
                      <span>Gepland: {formatDateTime(action.plannedStartAt)} – {formatDateTime(action.plannedEndAt)}</span>
                      <span>Werkelijk: {formatDateTime(action.actualStartAt)} – {formatDateTime(action.actualEndAt)}</span>
                      <span>Providerstatus: {action.providerStatus??"—"}</span>
                    </div>
                  </div>)}
                </div>}
          </section>

          <section className="admin-visits__panel admin-visit-detail__section">
            <h2>Provideroperations</h2>
            {detail.providerOperations.length===0
              ? <p className="admin-visit-detail__muted">Geen provideroperations vastgelegd.</p>
              : <div className="admin-visit-detail__list">
                  {detail.providerOperations.map(operation=><div className="admin-visit-detail__row" key={operation.id}>
                    <div className="admin-visit-detail__row-head">
                      <strong>{operation.type}</strong>
                      <span>{operation.status}</span>
                    </div>
                    <div className="admin-visit-detail__row-meta">
                      <span>Operation ID: {operation.operationId}</span>
                      <span>Pogingen: {operation.attemptCount}</span>
                      <span>Foutcode: {operation.lastErrorCode??"—"}</span>
                      <span>Aangemaakt: {formatDateTime(operation.createdAt)}</span>
                      <span>Requested end: {formatDateTime(operation.requestedEndAt)}</span>
                      <span>Voltooid: {formatDateTime(operation.completedAt)}</span>
                    </div>
                  </div>)}
                </div>}
          </section>

          <section className="admin-visits__panel admin-visit-detail__section">
            <h2>Eindtijdwijzigingen</h2>
            {detail.endTimeChanges.length===0
              ? <p className="admin-visit-detail__muted">Geen eindtijdwijzigingen vastgelegd.</p>
              : <div className="admin-visit-detail__list">
                  {detail.endTimeChanges.map(change=><div className="admin-visit-detail__row" key={change.id}>
                    <div className="admin-visit-detail__row-head">
                      <strong>{change.actorUsername}</strong>
                      <span>{change.result}</span>
                    </div>
                    <div className="admin-visit-detail__row-meta">
                      <span>Van: {formatDateTime(change.previousDesiredEndAt)}</span>
                      <span>Naar: {formatDateTime(change.requestedDesiredEndAt)}</span>
                      <span>{formatDateTime(change.createdAt)}</span>
                    </div>
                  </div>)}
                </div>}
          </section>

          <section className="admin-visits__panel admin-visit-detail__section">
            <h2>Relevante parkeerregelversies</h2>
            {detail.relevantRuleSets.length===0
              ? <p className="admin-visit-detail__muted">Geen parkeerregelversie gevonden voor deze periode.</p>
              : <div className="admin-visit-detail__list">
                  {detail.relevantRuleSets.map(rule=><div className="admin-visit-detail__row" key={rule.id}>
                    <div className="admin-visit-detail__row-head">
                      <strong>{rule.id}</strong>
                      <span>{rule.continuation}</span>
                    </div>
                    <div className="admin-visit-detail__row-meta">
                      <span>Geldig: {formatDateTime(rule.validFrom)} – {formatDateTime(rule.validUntil)}</span>
                      <span>Max. provideractie: {formatMinutes(rule.maxProviderActionDurationMinutes)}</span>
                      <span>Feestdagen gratis: {booleanLabel(rule.publicHolidaysAreFree)}</span>
                    </div>
                  </div>)}
                </div>}
          </section>
        </>}
  </div>;
}
