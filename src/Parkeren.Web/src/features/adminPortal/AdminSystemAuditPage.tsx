import { useEffect,useState } from "react";
import { getAdminAuditEvents,type AdminAuditEvent } from "../../api/adminAudit";
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

export function AdminSystemAuditPage(){
  const[events,setEvents]=useState<AdminAuditEvent[]>();
  const[error,setError]=useState<string>();

  async function load(){
    setError(undefined);
    try{
      setEvents(await getAdminAuditEvents({limit:100}));
    }catch(e){
      setError(e instanceof Error?e.message:"Auditlog kon niet worden geladen.");
    }
  }

  useEffect(()=>{void load();},[]);

  if(!events&&!error)return <Loading label="Auditlog laden"/>;
  if(!events)return <div className="admin-system">
    <Alert tone="danger">{error??"Auditlog kon niet worden geladen."}</Alert>
    <div className="admin-system__actions"><Button onClick={()=>void load()}>Opnieuw proberen</Button></div>
  </div>;

  return <div className="admin-system">
    {error&&<Alert tone="danger">{error}</Alert>}
    <div className="admin-system__actions">
      <Button variant="secondary" onClick={()=>void load()}>Vernieuwen</Button>
    </div>

    {events.length===0?(
      <section className="admin-system__panel">
        <h2>Geen beheeracties</h2>
        <p>Er zijn nog geen audit-events opgeslagen.</p>
      </section>
    ):(
      events.map(event=><section className="admin-system__panel" key={event.id}>
        <div className="admin-system__panel-heading">
          <h2>{event.action}</h2>
          <span className="admin-system__meta">{formatDateTime(event.createdAt)}</span>
        </div>
        <dl className="admin-system__diagnostics-list">
          <div><dt>Beheerder</dt><dd>{event.actorUsername}</dd></div>
          <div><dt>Target</dt><dd>{event.targetType}{event.targetId?` · ${event.targetId}`:""}</dd></div>
          <div><dt>Context</dt><dd><pre style={{margin:0,whiteSpace:"pre-wrap",textAlign:"left"}}>{formatContext(event.contextJson)}</pre></dd></div>
        </dl>
      </section>)
    )}
  </div>;
}
