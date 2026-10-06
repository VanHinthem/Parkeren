import { Fragment,useEffect,useState } from "react";
import { getAdminAuditEvents,type AdminAuditEvent,type AdminAuditQuery } from "../../api/adminAudit";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import { formatAdminDateTimePrecise,formatAdminJson } from "./adminFieldFormatters";
import "./adminFieldPresentation.css";
import "./AdminSystem.css";

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
  const[expandedId,setExpandedId]=useState<string>();
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
    setExpandedId(undefined);
    void load(query);
  }

  function clearFilters(){
    setActorUserId("");
    setAction("");
    setTargetType("");
    setTargetId("");
    setFrom("");
    setTo("");
    setExpandedId(undefined);
    const query={limit:100};
    setAppliedQuery(query);
    void load(query);
  }

  const subnav=<nav className="admin-subnav" aria-label="Systeem">
    <a className="admin-subnav__link" href="/beheer/systeem">Instellingen</a>
    <a className="admin-subnav__link active" href="/beheer/systeem/audit">Audit</a>
    <a className="admin-subnav__link" href="/beheer/systeem/diagnostiek">Diagnostiek</a>
  </nav>;

  if(!events&&!error)return <Loading label="Auditlog laden"/>;
  if(!events)return <div className="admin-system">
    {subnav}
    <Alert tone="danger">{error??"Auditlog kon niet worden geladen."}</Alert>
    <div className="admin-action-group admin-action-group--start">
      <Button className="admin-action--compact" onClick={()=>void load()}>Opnieuw proberen</Button>
    </div>
  </div>;

  return <div className="admin-system">
    {subnav}
    {error&&<Alert tone="danger">{error}</Alert>}

    <section className="admin-system__panel">
      <div className="admin-system__panel-heading">
        <div>
          <h2>Auditlog filteren</h2>
          <p>Filter op actor, actie, target en periode. Maximaal 100 nieuwste resultaten worden getoond.</p>
        </div>
      </div>
      <div className="admin-system__audit-filters">
        <label className="admin-field"><span>Actor user-id</span><input value={actorUserId} onChange={event=>setActorUserId(event.target.value)} placeholder="GUID"/></label>
        <label className="admin-field"><span>Actie</span><input value={action} onChange={event=>setAction(event.target.value)} placeholder="bijv. UserPolicyChanged"/></label>
        <label className="admin-field"><span>Targettype</span><input value={targetType} onChange={event=>setTargetType(event.target.value)} placeholder="bijv. User"/></label>
        <label className="admin-field"><span>Target-id</span><input value={targetId} onChange={event=>setTargetId(event.target.value)} placeholder="GUID of externe sleutel"/></label>
        <label className="admin-field"><span>Vanaf</span><input type="datetime-local" value={from} onChange={event=>setFrom(event.target.value)}/></label>
        <label className="admin-field"><span>Tot en met</span><input type="datetime-local" value={to} onChange={event=>setTo(event.target.value)}/></label>
      </div>
      <div className="admin-action-group admin-action-group--start">
        <Button className="admin-action--compact" onClick={applyFilters}>Filters toepassen</Button>
        <Button className="admin-action--compact" variant="secondary" onClick={clearFilters}>Wissen</Button>
        <Button className="admin-action--compact" variant="secondary" onClick={()=>void load()}>Vernieuwen</Button>
      </div>
    </section>

    {events.length===0?(
      <section className="admin-system__panel">
        <h2>Geen beheeracties gevonden</h2>
        <p>Er zijn geen audit-events die aan de gekozen filters voldoen.</p>
      </section>
    ):(
      <section className="admin-system__panel">
        <div className="admin-table-wrap">
          <table className="admin-table admin-table--fixed admin-system__audit-table">
            <thead>
              <tr>
                <th>Datum/tijd</th>
                <th>Actor</th>
                <th>Actie</th>
                <th>Targettype</th>
                <th aria-label="Acties"/>
              </tr>
            </thead>
            <tbody>
              {events.map(event=>{
                const expanded=expandedId===event.id;
                return <Fragment key={event.id}>
                  <tr>
                    <td>{formatAdminDateTimePrecise(event.createdAt)}</td>
                    <td><span className="admin-identity"><strong>{event.actorUsername}</strong></span></td>
                    <td><span className="admin-code admin-code--table">{event.action}</span></td>
                    <td>{event.targetType}</td>
                    <td className="admin-table__actions">
                      <button
                        className="admin-action-link admin-action-link--muted"
                        type="button"
                        aria-expanded={expanded}
                        onClick={()=>setExpandedId(expanded?undefined:event.id)}
                      >
                        {expanded?"Verbergen ▴":"Tonen ▾"}
                      </button>
                    </td>
                  </tr>
                  {expanded&&<tr className="admin-table__detail-row">
                    <td colSpan={5}>
                      <div className="admin-table__detail-panel">
                        <strong>Technische context</strong>
                        <dl className="admin-facts admin-facts--grid admin-system__audit-detail-facts">
                          <div className="admin-fact"><dt>Audit event-ID</dt><dd className="admin-code">{event.id}</dd></div>
                          <div className="admin-fact"><dt>Actor user-ID</dt><dd className="admin-code">{event.actorUserId}</dd></div>
                          <div className="admin-fact"><dt>Target</dt><dd>{event.targetType}{event.targetId?<> · <span className="admin-code">{event.targetId}</span></>:null}</dd></div>
                        </dl>
                        <pre className="admin-code-block">{formatAdminJson(event.contextJson)}</pre>
                      </div>
                    </td>
                  </tr>}
                </Fragment>;
              })}
            </tbody>
          </table>
        </div>
      </section>
    )}
  </div>;
}
