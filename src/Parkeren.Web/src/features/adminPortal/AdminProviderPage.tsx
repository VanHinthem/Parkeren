import { useEffect,useState } from "react";
import { getAdminProviderStatus,type AdminProviderStatus } from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import "./AdminProvider.css";

function formatDateTime(value:string|null){
  return value
    ? new Date(value).toLocaleString("nl-NL",{dateStyle:"short",timeStyle:"short"})
    : "—";
}

function formatBalance(status:AdminProviderStatus){
  const balance=status.balance;
  if(!balance)return "Niet beschikbaar";

  switch(balance.unit){
    case "Euro":
      return new Intl.NumberFormat("nl-NL",{style:"currency",currency:"EUR"}).format(balance.remainingBalance);
    case "Minute":
      return `${balance.remainingBalance} min`;
    case "Times":
      return `${balance.remainingBalance} keer`;
    default:
      return String(balance.remainingBalance);
  }
}

function stateLabel(status:AdminProviderStatus){
  if(status.balanceError&&status.balance===null)return {label:"Niet beschikbaar",className:"admin-provider__state admin-provider__state--error"};
  if(status.balanceIsStale)return {label:"Verouderd",className:"admin-provider__state admin-provider__state--stale"};
  return {label:"Actueel",className:"admin-provider__state"};
}

export function AdminProviderPage(){
  const[status,setStatus]=useState<AdminProviderStatus>();
  const[error,setError]=useState<string>();
  const[loading,setLoading]=useState(true);

  async function load(){
    setLoading(true);
    setError(undefined);
    try{
      setStatus(await getAdminProviderStatus());
    }catch(e){
      setError(e instanceof Error?e.message:"Providerstatus kon niet worden geladen.");
    }finally{
      setLoading(false);
    }
  }

  useEffect(()=>{void load();},[]);

  if(loading&&!status)return <Loading label="Providerstatus laden"/>;

  const state=status?stateLabel(status):null;

  return <div className="admin-provider">
    {error&&<Alert tone="danger">{error}</Alert>}
    {status?.balanceError&&<Alert tone={status.balance?"warning":"danger"}>
      2Park-saldo kon niet actueel worden opgehaald. {status.balance?"De laatst succesvolle waarde wordt als verouderd getoond.":"Er is geen eerder succesvol saldo beschikbaar."}
    </Alert>}
    {status?.actionsError&&<Alert tone="warning">
      De actuele 2Park-actielijst kon niet worden opgehaald. Er wordt geen oude actielijst als actueel getoond.
    </Alert>}

    {status&&<>
      <div className="admin-provider__grid">
        <section className="admin-provider__panel">
          <h2>Officieel 2Park-saldo</h2>
          <div className="admin-provider__balance">
            <div className="admin-provider__balance-value">
              <strong>{formatBalance(status)}</strong>
              <span>Autoritatieve providerwaarde</span>
            </div>
            {state&&<span className={state.className}>{state.label}</span>}
          </div>
          <p className="admin-provider__meta">
            Laatst succesvol: {formatDateTime(status.lastSuccessfulBalanceAt)} · Laatste poging: {formatDateTime(status.lastBalanceAttemptAt)}
          </p>
        </section>

        <section className="admin-provider__panel">
          <h2>Provider</h2>
          <dl className="admin-provider__facts">
            <div className="admin-provider__fact"><dt>Product</dt><dd>{status.product?.name??"—"}</dd></div>
            <div className="admin-provider__fact"><dt>Product-ID</dt><dd>{status.product?.id??"—"}</dd></div>
            <div className="admin-provider__fact"><dt>Locatie</dt><dd>{status.product?.location??"—"}</dd></div>
            <div className="admin-provider__fact"><dt>Saldo-eenheid</dt><dd>{status.balance?.unit??"—"}</dd></div>
          </dl>
        </section>
      </div>

      <section className="admin-provider__panel">
        <div className="admin-provider__toolbar">
          <div>
            <h2>Actuele provideracties</h2>
            <p>Rechtstreeks uit de parkeerprovider; lokale Visit-status wordt hier niet als vervanging gebruikt.</p>
          </div>
          <Button variant="secondary" onClick={()=>void load()} disabled={loading}>
            {loading?"Vernieuwen…":"Vernieuwen"}
          </Button>
        </div>

        <p className="admin-provider__meta">
          Opgehaald: {formatDateTime(status.actionsRetrievedAt)}
        </p>

        {status.actions.length===0
          ? <p className="admin-provider__empty">Geen actuele provideracties ontvangen.</p>
          : <div className="admin-provider__table-wrap">
              <table className="admin-provider__table">
                <thead>
                  <tr>
                    <th>Action-ID</th>
                    <th>Kenteken</th>
                    <th>Start</th>
                    <th>Einde</th>
                    <th>Locatie</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {status.actions.map(action=><tr key={action.providerActionId}>
                    <td>{action.providerActionId}</td>
                    <td>{action.licensePlate}</td>
                    <td>{formatDateTime(action.start)}</td>
                    <td>{formatDateTime(action.end)}</td>
                    <td>{action.location}</td>
                    <td>{action.status}</td>
                  </tr>)}
                </tbody>
              </table>
            </div>}
      </section>

      <section className="admin-provider__panel admin-provider__notice">
        <h2>Afwijkingen & reconciliatie</h2>
        <p>
          2Park blijft leidend voor providerstatus en officieel saldo. Verschillen met de lokale administratie worden later als aparte discrepancy-records vastgelegd.
        </p>
        <a className="admin-provider__link" href="/beheer/provider/afwijkingen">Naar afwijkingen</a>
      </section>
    </>}
  </div>;
}
