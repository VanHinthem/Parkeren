import { AdminProviderSubnav } from "./AdminProviderSubnav";
import { Fragment,useEffect,useState } from "react";
import {
  getAdminProviderDiscrepancies,
  type AdminProviderDiscrepancy
} from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import {
  adminProviderDiscrepancyStatusTone,
  adminStatusClass,
  formatAdminDateTime,
  formatAdminNumber,
  formatAdminProviderDiscrepancyStatus
} from "./adminFieldFormatters";
import {
  providerDiscrepancyContextLabel,
  providerDiscrepancyTypeLabel
} from "./providerDiscrepancyPresentation";
import "./adminFieldPresentation.css";
import "./AdminProvider.css";

export function AdminProviderDiscrepanciesPage(){
  const[items,setItems]=useState<AdminProviderDiscrepancy[]>();
  const[includeResolved,setIncludeResolved]=useState(false);
  const[expandedId,setExpandedId]=useState<string>();
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
    <AdminProviderSubnav current="reconciliation" />

    <section className="admin-provider__panel">
      <div className="admin-provider__toolbar">
        <div>
          <h2>Afwijkingen & reconciliatie</h2>
          <p>
            2Park is leidend voor providerstatus. Afwijkingen worden apart vastgelegd en overschrijven lokale historie niet stilzwijgend. Opgeloste records blijven beschikbaar voor audit.
          </p>
        </div>
        <Button className="admin-action--compact" variant="secondary" onClick={()=>void load()} disabled={loading}>
          {loading?"Vernieuwen…":"Vernieuwen"}
        </Button>
      </div>

      <div className="admin-provider__discrepancy-summary">
        <span className={adminStatusClass(openCount>0?"danger":"active")}>{formatAdminNumber(openCount)} open</span>
        {includeResolved&&<span className={adminStatusClass("active")}>{formatAdminNumber(resolvedCount)} opgelost</span>}
        <label className="admin-check admin-provider__history-toggle">
          <input
            type="checkbox"
            checked={includeResolved}
            onChange={event=>setIncludeResolved(event.target.checked)}
          />
          <span>Opgeloste afwijkingen tonen</span>
        </label>
      </div>
    </section>

    {error&&<Alert tone="danger">{error}</Alert>}
    {loading&&!items?<Loading label="Afwijkingen laden"/>:null}

    {!loading&&items?.length===0
      ? <section className="admin-provider__panel admin-provider__notice">
          <h2>Geen afwijkingen</h2>
          <p>
            Er zijn momenteel geen {includeResolved?"geregistreerde":"open"} afwijkingen tussen de lokale administratie en 2Park.
          </p>
        </section>
      : null}

    {items&&items.length>0
      ? <section className="admin-provider__panel">
          <div className="admin-table-wrap">
            <table className="admin-table admin-table--fixed admin-provider__discrepancy-table">
              <thead>
                <tr>
                  <th>Afwijking</th>
                  <th>Providerproduct</th>
                  <th>Eerste detectie</th>
                  <th>Laatst gezien</th>
                  <th>Status</th>
                  <th aria-label="Acties"/>
                </tr>
              </thead>
              <tbody>
                {items.map(item=>{
                  const expanded=expandedId===item.id;
                  const tone=adminProviderDiscrepancyStatusTone(item.status);
                  return <Fragment key={item.id}>
                    <tr>
                      <td>
                        <span className="admin-identity admin-provider__discrepancy-list-title">
                          <strong>{providerDiscrepancyTypeLabel(item.type)}</strong>
                          <small>{providerDiscrepancyContextLabel(item)}</small>
                        </span>
                      </td>
                      <td>{item.providerProductName}</td>
                      <td>{formatAdminDateTime(item.detectedAt)}</td>
                      <td>{formatAdminDateTime(item.lastObservedAt)}</td>
                      <td><span className={adminStatusClass(tone)}>{formatAdminProviderDiscrepancyStatus(item.status)}</span></td>
                      <td className="admin-table__actions">
                        <button
                          className="admin-action-link admin-action-link--muted"
                          type="button"
                          aria-expanded={expanded}
                          onClick={()=>setExpandedId(expanded?undefined:item.id)}
                        >
                          {expanded?"Verbergen":"Details"}
                        </button>
                      </td>
                    </tr>
                    {expanded&&<tr className="admin-table__detail-row">
                      <td colSpan={6}>
                        <div className="admin-table__detail-panel">
                          <div className="admin-provider__discrepancy-detail-head">
                            <strong>Technische details</strong>
                            {item.visitId&&<a className="admin-action-link" href={`/beheer/bezoeken/${item.visitId}`}>Visit openen</a>}
                          </div>
                          <dl className="admin-facts admin-facts--grid">
                            <div className="admin-fact"><dt>Providerproduct</dt><dd>{item.providerProductName} <span className="admin-code">({item.providerProductExternalId})</span></dd></div>
                            <div className="admin-fact"><dt>Provider action-ID</dt><dd className="admin-code">{item.providerActionId??"—"}</dd></div>
                            <div className="admin-fact"><dt>Lokale action</dt><dd className="admin-code">{item.providerParkingActionId??"—"}</dd></div>
                            <div className="admin-fact"><dt>Lokale provideractie eindtijd</dt><dd>{formatAdminDateTime(item.localPlannedEndAt)}</dd></div>
                            <div className="admin-fact"><dt>2Park start</dt><dd>{formatAdminDateTime(item.providerStartAt)}</dd></div>
                            <div className="admin-fact"><dt>2Park provideractie eindtijd</dt><dd>{formatAdminDateTime(item.providerEndAt)}</dd></div>
                            <div className="admin-fact"><dt>Eerste detectie</dt><dd>{formatAdminDateTime(item.detectedAt)}</dd></div>
                            <div className="admin-fact"><dt>Laatst waargenomen</dt><dd>{formatAdminDateTime(item.lastObservedAt)}</dd></div>
                            <div className="admin-fact"><dt>Opgelost</dt><dd>{formatAdminDateTime(item.resolvedAt)}</dd></div>
                          </dl>
                        </div>
                      </td>
                    </tr>}
                  </Fragment>;
                })}
              </tbody>
            </table>
          </div>
        </section>
      : null}
  </div>;
}
