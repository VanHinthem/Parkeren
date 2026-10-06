import { useEffect,useState } from "react";
import { getAdminAuditEvents,type AdminAuditEvent,type AdminAuditQuery } from "../../api/adminAudit";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import "./AdminSystem.css";

function formatDateTime(value:string){
  return new Date(value).toLocaleString("nl-NL",{dateStyle:"short",timeStyle:"medium"});
}

function formatContext(value:string|null){
  if(!value)return "—";
  try{return JSON.stringify(JSON.parse(value),null,2);}catch{return value;}
}

function toIso(value:string,endOfMinute=false){
  if(!value)return undefined;
  const date=new Date(value);
  if(Number.isNaN(date.getTime()))return undefined;
  if(endOfMinute)date.setSeconds(59,999);
  return date.toISOString();
}

export function AdminSystemAuditPage(){
  const[events,setEvents]=useState<AdminAuditEvent[]>();
  const[error,setError]=useState<string>();
  const[actorUserId,setActorUserId]=useState("");
  const[action,setAction]=useState("");
  const[targetType,setTargetType]=useState("");
  const[targetId,setTargetId]=useState("");
  const[from,setFrom]=useState("");
  const[to,setTo]=useState("");
  const[appliedQuery,setAppliedQuery]=useState<AdminAuditQuery>({limit:100});

  async function load(query:AdminAuditQuery=appliedQuery){
    setError(undefined);
    try{
      setEvents(await getAdminAuditEvents(query));
    }catch(e){
      setError(e instanceof Error?e.message:"Auditlog kon niet worden geladen.");
    }
  }

  useEffect(()=>{void load({limit:100});},[]);

  function applyFilters(){
    if(from&&to&&new Date(from)>new Date(to)){
      setError("Van-datum mag niet na tot-datum liggen.");
      return;
    }
    const query:AdminAuditQuery={
      actorUserId:actorUserId.trim()||undefined,
      action:action.trim()||undefined,
      targetType:targetType.trim()||undefined,
      targetId:targetId.trim()||undefined,
      from:toIso(from),
      to:toIso(to,true),
      limit:100
    };
    setAppliedQuery(query);
    void load(query);
  }

  function clearFilters(){
    setActorUserId("");setAction("");setTargetType("");setTargetId("");setFrom("");setTo("");
    const query={limit:100};
    setAppliedQuery(query);
    void load(query);
  }

  if(!events&&!error)return <Loading label="Auditlog laden"/>;
  if(!events)return <div className="admin-system">
    <nav className="admin-subnav" aria-label="Systeem">
      <a className="admin-subnav__link" href="/beheer/systeem">Instellingen</a>
      <a className="admin-subnav__link active" href="/beheer/systeem/audit">Audit</a>
      <a className="admin-subnav__link" href="/beheer/systeem/diagnostiek">Diagnostiek</a>
    </nav>
    <Alert tone="danger">{error??"Auditlog kon niet worden geladen."}</Alert>
    <div className="admin-system__actions"><Button onClick={()=>void load()}>Opnieuw proberen</Button></div>
  </div>;

  return <div className="admin-system">
    <nav className="admin-subnav" aria-label="Systeem">
      <a className="admin-subnav__link" href="/beheer/systeem">Instellingen</a>
      <a className="admin-subnav__link active" href="/beheer/systeem/audit">Audit</a>
      <a className="admin-subnav__link" href="/beheer/systeem/diagnostiek">Diagnostiek</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}

    <section className="admin-system__panel">
      <div className="admin-system__panel-heading">
        <div>
          <h2>Auditlog filteren</h2>
          <p className="admin-system__navigation-copy">Filter op actor, actie, target en periode. Maximaal 100 nieuwste resultaten worden getoond.</p>
        </div>
      </div>
      <div className="admin-system__audit-filters">
        <label className="admin-system__field"><span>Actor user-id</span><input value={actorUserId} onChange={event=>setActorUserId(event.target.value)} placeholder="GUID"/></label>
        <label className="admin-system__field"><span>Actie</span><input value={action} onChange={event=>setAction(event.target.value)} placeholder="bijv. UserPolicyChanged"/></label>
        <label className="admin-system__field"><span>Targettype</span><input value={targetType} onChange={event=>setTargetType(event.target.value)} placeholder="bijv. User"/></label>
        <label className="admin-system__field"><span>Target-id</span><input value={targetId} onChange={event=>setTargetId(event.target.value)} placeholder="GUID of externe sleutel"/></label>
        <label className="admin-system__field"><span>Vanaf</span><input type="datetime-local" value={from} onChange={event=>setFrom(event.target.value)}/></label>
        <label className="admin-system__field"><span>Tot en met</span><input type="datetime-local" value={to} onChange={event=>setTo(event.target.value)}/></label>
      </div>
      <div className="admin-system__actions">
        <Button onClick={applyFilters}>Filters toepassen</Button>
        <Button variant="secondary" onClick={clearFilters}>Wissen</Button>
        <Button variant="secondary" onClick={()=>void load()}>Vernieuwen</Button>
      </div>
    </section>

    {events.length===0?(
      <section className="admin-system__panel">
        <h2>Geen beheeracties gevonden</h2>
        <p>Er zijn geen audit-events die aan de gekozen filters voldoen.</p>
      </section>
    ):(
      <div className="admin-system__audit-list">
        {events.map(event=><section className="admin-system__panel admin-system__audit-event" key={event.id}>
          <div className="admin-system__panel-heading">
            <div>
              <h2>{event.action}</h2>
              <span className="admin-system__audit-actor">{event.actorUsername}</span>
            </div>
            <span className="admin-system__meta">{formatDateTime(event.createdAt)}</span>
          </div>
          <dl className="admin-system__diagnostics-list">
            <div><dt>Target</dt><dd>{event.targetType}{event.targetId?` · ${event.targetId}`:""}</dd></div>
            <div className="admin-system__audit-context-row"><dt>Context</dt><dd><pre className="admin-system__audit-context">{formatContext(event.contextJson)}</pre></dd></div>
          </dl>
        </section>)}
      </div>
    )}
  </div>;
}
