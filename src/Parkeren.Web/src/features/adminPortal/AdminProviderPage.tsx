import { useEffect,useState } from "react";
import {
  getAdminProviderProducts,
  getAdminProviderStatus,
  setAdminDefaultProviderProduct,
  syncAdminProviderProducts,
  type AdminProviderProduct,
  type AdminProviderStatus
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
  const[products,setProducts]=useState<AdminProviderProduct[]>();
  const[error,setError]=useState<string>();
  const[message,setMessage]=useState<string>();
  const[loading,setLoading]=useState(true);
  const[syncing,setSyncing]=useState(false);
  const[changingDefault,setChangingDefault]=useState<string>();

  async function load(){
    setLoading(true);
    setError(undefined);
    try{
      const[providerStatus,providerProducts]=await Promise.all([
        getAdminProviderStatus(),
        getAdminProviderProducts()
      ]);
      setStatus(providerStatus);
      setProducts(providerProducts);
    }catch(e){
      setError(e instanceof Error?e.message:"Providergegevens konden niet worden geladen.");
    }finally{
      setLoading(false);
    }
  }

  async function syncProducts(){
    setSyncing(true);
    setError(undefined);
    setMessage(undefined);
    try{
      const result=await syncAdminProviderProducts();
      setProducts(result.products);
      setMessage(result.defaultAutoSelected
        ?"Producten zijn gesynchroniseerd. Het enige beschikbare product is automatisch als default ingesteld."
        :"Producten zijn gesynchroniseerd.");
      setStatus(await getAdminProviderStatus());
    }catch(e){
      setError(e instanceof Error?e.message:"Providerproducten konden niet worden gesynchroniseerd.");
    }finally{
      setSyncing(false);
    }
  }

  async function makeDefault(product:AdminProviderProduct){
    setChangingDefault(product.id);
    setError(undefined);
    setMessage(undefined);
    try{
      await setAdminDefaultProviderProduct(product.id);
      setMessage(`${product.name} is nu het default parkeerproduct voor nieuwe Visits.`);
      const[providerStatus,providerProducts]=await Promise.all([
        getAdminProviderStatus(),
        getAdminProviderProducts()
      ]);
      setStatus(providerStatus);
      setProducts(providerProducts);
    }catch(e){
      setError(e instanceof Error?e.message:"Default parkeerproduct kon niet worden gewijzigd.");
    }finally{
      setChangingDefault(undefined);
    }
  }

  useEffect(()=>{void load();},[]);

  if(loading&&!status&&!products)return <Loading label="Providerstatus laden"/>;

  const state=status?stateLabel(status):null;

  return <div className="admin-provider">
    {error&&<Alert tone="danger">{error}</Alert>}
    {message&&<Alert>{message}</Alert>}
    {status?.balanceError&&<Alert tone={status.balance?"warning":"danger"}>
      {status.balanceError} {status.balance?"De laatst succesvolle saldowaarde wordt als verouderd getoond.":""}
    </Alert>}
    {status?.actionsError&&status.actionsError!==status.balanceError&&<Alert tone="warning">
      Actuele 2Park-acties konden niet worden opgehaald: {status.actionsError}
    </Alert>}

    {status&&<div className="admin-provider__grid">
      <section className="admin-provider__panel">
        <h2>Officieel 2Park-saldo</h2>
        <div className="admin-provider__balance">
          <div className="admin-provider__balance-value">
            <strong>{formatBalance(status)}</strong>
            <span>Autoritatieve providerwaarde van het defaultproduct</span>
          </div>
          {state&&<span className={state.className}>{state.label}</span>}
        </div>
        <p className="admin-provider__meta">
          Laatst succesvol: {formatDateTime(status.lastSuccessfulBalanceAt)} · Laatste poging: {formatDateTime(status.lastBalanceAttemptAt)}
        </p>
      </section>

      <section className="admin-provider__panel">
        <h2>Actief defaultproduct</h2>
        <dl className="admin-provider__facts">
          <div className="admin-provider__fact"><dt>Product</dt><dd>{status.product?.name??"Niet ingesteld"}</dd></div>
          <div className="admin-provider__fact"><dt>Provider product-ID</dt><dd>{status.product?.id??"—"}</dd></div>
          <div className="admin-provider__fact"><dt>Location</dt><dd>{status.product?.location??"—"}</dd></div>
          <div className="admin-provider__fact"><dt>Saldo-eenheid</dt><dd>{status.balance?.unit??"—"}</dd></div>
        </dl>
      </section>
    </div>}

    <section className="admin-provider__panel">
      <div className="admin-provider__toolbar">
        <div>
          <h2>2Park-producten</h2>
          <p>Providergegevens zijn read-only. Alleen het defaultproduct voor nieuwe Visits is lokaal beheerbaar.</p>
        </div>
        <Button variant="secondary" onClick={()=>void syncProducts()} disabled={syncing}>
          {syncing?"Synchroniseren…":"Producten synchroniseren"}
        </Button>
      </div>

      {!products||products.length===0
        ? <p className="admin-provider__empty">Nog geen producten lokaal vastgelegd. Synchroniseer met 2Park; bij precies één product wordt dit bij de eerste sync automatisch default.</p>
        : <div className="admin-provider__table-wrap">
            <table className="admin-provider__table">
              <thead>
                <tr>
                  <th>Product</th>
                  <th>Categorie</th>
                  <th>Provider product-ID</th>
                  <th>Location</th>
                  <th>Status</th>
                  <th>Laatst gezien</th>
                  <th/>
                </tr>
              </thead>
              <tbody>
                {products.map(product=><tr key={product.id}>
                  <td><strong>{product.name}</strong>{product.isDefault&&<span className="admin-provider__product-default">Default</span>}</td>
                  <td>{product.categoryName??product.categoryId??"—"}</td>
                  <td>{product.providerProductId}</td>
                  <td>{product.location}</td>
                  <td>{product.isAvailable?"Beschikbaar":"Niet meer beschikbaar"}</td>
                  <td>{formatDateTime(product.lastSeenAt)}</td>
                  <td>
                    {!product.isDefault&&product.isAvailable
                      ? <Button variant="secondary" onClick={()=>void makeDefault(product)} disabled={changingDefault!==undefined}>
                          {changingDefault===product.id?"Wijzigen…":"Als default gebruiken"}
                        </Button>
                      : null}
                  </td>
                </tr>)}
              </tbody>
            </table>
          </div>}
    </section>

    {status&&<section className="admin-provider__panel">
      <div className="admin-provider__toolbar">
        <div>
          <h2>Actuele provideracties</h2>
          <p>Rechtstreeks uit het default parkeerproduct; lokale Visit-status wordt hier niet als vervanging gebruikt.</p>
        </div>
        <Button variant="secondary" onClick={()=>void load()} disabled={loading}>
          {loading?"Vernieuwen…":"Vernieuwen"}
        </Button>
      </div>

      <p className="admin-provider__meta">Opgehaald: {formatDateTime(status.actionsRetrievedAt)}</p>

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
                  <th>Location</th>
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
    </section>}

    <section className="admin-provider__panel admin-provider__notice">
      <h2>Afwijkingen & reconciliatie</h2>
      <p>
        2Park blijft leidend voor providerstatus en officieel saldo. Gedetecteerde verschillen worden persistent vastgelegd en blijven na oplossing traceerbaar.
      </p>
      <a className="admin-provider__link" href="/beheer/provider/afwijkingen">Afwijkingen bekijken</a>
    </section>
  </div>;
}
