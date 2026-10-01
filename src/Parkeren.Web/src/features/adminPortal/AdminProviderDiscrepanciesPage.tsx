import { useEffect,useState } from "react";
import {
  getAdminProviderDiscrepancies,
  type AdminProviderDiscrepancy
} from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import "./AdminProvider.css";

function formatDateTime(value:string|null){
  return value
    ? new Date(value).toLocaleString("nl-NL",{dateStyle:"short",timeStyle:"short"})
    : "—";
}

function typeLabel(type:AdminProviderDiscrepancy["type"]){
  switch(type){
    case "MissingProviderAction": return "Actie ontbreekt bij 2Park";
    case "ProviderActionStatusMismatch": return "Providerstatus wijkt af";
    case "ProviderActionEndMismatch": return "Eindtijd wijkt af";
    case "ExternalProviderAction": return "Externe 2Park-actie";
    case "BalanceMismatch": return "Saldo-afwijking";
  }
}

function statusLabel(status:AdminProviderDiscrepancy["status"]){
  return status==="Open"?"Open":"Opgelost";
}

function contextLabel(item:AdminProviderDiscrepancy){
  if(item.localProviderActionState&&item.providerStatus)
    return `Lokaal: ${item.localProviderActionState} · 2Park: ${item.providerStatus}`;
  if(item.localProviderActionState)
    return `Lokaal: ${item.localProviderActionState}`;
  if(item.providerStatus)
    return `2Park: ${item.providerStatus}`;
  return "Geen aanvullende statuscontext";
}

export function AdminProviderDiscrepanciesPage(){
  const[items,setItems]=useState<AdminProviderDiscrepancy[]>();
  const[includeResolved,setIncludeResolved]=useState(false);
  const[loading,setLoading]=useState(true);
  const[error,setError]=useState<string>();

  async function load(includeHistory=includeResolved){
    setLoading(true);
    setError(undefined);
    try{
      setItems(await getAdminProviderDiscrepancies(includeHistory));
    }catch(e){
      setError(e instanceof Error?e.message:"Afwijkingen konden niet worden geladen.");
    }finally{
      setLoading(false);
    }
  }

  useEffect(()=>{void load(includeResolved);},[includeResolved]);

  const openCount=items?.filter(item=>item.status==="Open").length??0;
  const resolvedCount=items?.filter(item=>item.status==="Resolved").length??0;

  return <div className="admin-provider">
    <section className="admin-provider__panel">
      <div className="admin-provider__toolbar">
        <div>
          <h2>Afwijkingen & reconciliatie</h2>
          <p>
            2Park is leidend voor providerstatus. Afwijkingen worden apart vastgelegd en
            overschrijven lokale historie niet stilzwijgend.
          </p>
        </div>
        <Button variant="secondary" onClick={()=>void load()} disabled={loading}>
          {loading?"Vernieuwen…":"Vernieuwen"}
        </Button>
      </div>

      <div className="admin-provider__discrepancy-summary">
        <span className={openCount>0?"admin-provider__state admin-provider__state--error":"admin-provider__state"}>
          {openCount} open
        </span>
        {includeResolved&&<span className="admin-provider__state">{resolvedCount} opgelost</span>}
        <label className="admin-provider__history-toggle">
          <input
            type="checkbox"
            checked={includeResolved}
            onChange={event=>setIncludeResolved(event.target.checked)}
          />
          Opgeloste afwijkingen tonen
        </label>
      </div>
    </section>

    {error&&<Alert tone="danger">{error}</Alert>}
    {loading&&!items?<Loading label="Afwijkingen laden"/>:null}

    {!loading&&items?.length===0
      ? <section className="admin-provider__panel admin-provider__notice">
          <h2>Geen afwijkingen</h2>
          <p>
            Er zijn momenteel geen {includeResolved?"geregistreerde":"open"} afwijkingen tussen
            de lokale administratie en 2Park.
          </p>
        </section>
      : null}

    {items?.map(item=>
      <section className="admin-provider__panel admin-provider__discrepancy" key={item.id}>
        <div className="admin-provider__discrepancy-heading">
          <div>
            <div className="admin-provider__discrepancy-title">
              <h2>{typeLabel(item.type)}</h2>
              <span className={item.status==="Open"
                ?"admin-provider__state admin-provider__state--error"
                :"admin-provider__state"}>
                {statusLabel(item.status)}
              </span>
            </div>
            <p>{contextLabel(item)}</p>
          </div>
          {item.visitId&&<a className="admin-provider__link" href={`/beheer/bezoeken/${item.visitId}`}>
            Visit openen
          </a>}
        </div>

        <dl className="admin-provider__facts admin-provider__facts--wide">
          <div className="admin-provider__fact"><dt>Providerproduct</dt><dd>{item.providerProductName} ({item.providerProductExternalId})</dd></div>
          <div className="admin-provider__fact"><dt>Provider action-ID</dt><dd>{item.providerActionId??"—"}</dd></div>
          <div className="admin-provider__fact"><dt>Lokale action</dt><dd>{item.providerParkingActionId??"—"}</dd></div>
          <div className="admin-provider__fact"><dt>Lokale geplande eindtijd</dt><dd>{formatDateTime(item.localPlannedEndAt)}</dd></div>
          <div className="admin-provider__fact"><dt>2Park start</dt><dd>{formatDateTime(item.providerStartAt)}</dd></div>
          <div className="admin-provider__fact"><dt>2Park eindtijd</dt><dd>{formatDateTime(item.providerEndAt)}</dd></div>
          <div className="admin-provider__fact"><dt>Eerste detectie</dt><dd>{formatDateTime(item.detectedAt)}</dd></div>
          <div className="admin-provider__fact"><dt>Laatst waargenomen</dt><dd>{formatDateTime(item.lastObservedAt)}</dd></div>
          <div className="admin-provider__fact"><dt>Opgelost</dt><dd>{formatDateTime(item.resolvedAt)}</dd></div>
        </dl>
      </section>)}

    <section className="admin-provider__panel admin-provider__notice">
      <a className="admin-provider__link" href="/beheer/provider">Terug naar providerstatus</a>
      <p>
        Opgeloste records blijven bestaan voor audit. Herstel van Visit/providerstatus loopt via
        de bestaande recovery- en reconciliationflows; deze pagina wijzigt geen providerhistorie.
      </p>
    </section>
  </div>;
}
