import { useEffect,useState } from "react";
import {
  getAdminProviderProducts,
  getAdminProviderStatus,
  setAdminDefaultProviderProduct,
  syncAdminProviderProducts,
  type AdminProviderProduct,
  type AdminProviderStatus
} from "../../api/client";
import { LicensePlate } from "../../components/LicensePlate";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import {
  adminProviderActionStatusTone,
  adminStatusClass,
  formatAdminAvailability,
  formatAdminDateTime,
  formatAdminProviderActionStatus,
  formatAdminProviderBalance,
  formatAdminProviderBalanceUnit,
  type AdminStatusTone
} from "./adminFieldFormatters";
import "./AdminProvider.css";

function balanceState(status:AdminProviderStatus):{label:string;tone:AdminStatusTone}{
  if(status.balanceError&&status.balance===null)return {label:"Niet beschikbaar",tone:"danger"};
  if(status.balanceIsStale)return {label:"Verouderd",tone:"warning"};
  return {label:"Actueel",tone:"active"};
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

  const state=status?balanceState(status):null;

  return <div className="admin-provider">
    <nav className="admin-subnav" aria-label="Provider & reconciliatie">
      <a className="admin-subnav__link active" href="/beheer/provider">Provider</a>
      <a className="admin-subnav__link" href="/beheer/provider/afwijkingen">Reconciliatie</a>
    </nav>

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
            <strong className="admin-duration">{formatAdminProviderBalance(status.balance?.remainingBalance??null,status.balance?.unit??null)}</strong>
            <span>Autoritatieve providerwaarde van het defaultproduct</span>
          </div>
          {state&&<span className={adminStatusClass(state.tone)}>{state.label}</span>}
        </div>
        <p className="admin-provider__meta">
          Laatst succesvol: {formatAdminDateTime(status.lastSuccessfulBalanceAt)} · Laatste poging: {formatAdminDateTime(status.lastBalanceAttemptAt)}
        </p>
      </section>

      <section className="admin-provider__panel">
        <h2>Actief defaultproduct</h2>
        <dl className="admin-facts">
          <div className="admin-fact"><dt>Product</dt><dd>{status.product?.name??"Niet ingesteld"}</dd></div>
          <div className="admin-fact"><dt>Provider product-ID</dt><dd className="admin-code">{status.product?.id??"—"}</dd></div>
          <div className="admin-fact"><dt>Location</dt><dd className="admin-code">{status.product?.location??"—"}</dd></div>
          <div className="admin-fact"><dt>Saldo-eenheid</dt><dd>{formatAdminProviderBalanceUnit(status.balance?.unit??null)}</dd></div>
        </dl>
      </section>
    </div>}

    <section className="admin-provider__panel">
      <div className="admin-provider__toolbar">
        <div>
          <h2>2Park-producten</h2>
          <p>Providergegevens zijn read-only. Alleen het defaultproduct voor nieuwe Visits is lokaal beheerbaar.</p>
        </div>
        <Button className="admin-action--compact" variant="secondary" onClick={()=>void syncProducts()} disabled={syncing}>
          {syncing?"Synchroniseren…":"Producten synchroniseren"}
        </Button>
      </div>

      {!products||products.length===0
        ? <p className="admin-provider__empty">Nog geen producten lokaal vastgelegd. Synchroniseer met 2Park; bij precies één product wordt dit bij de eerste sync automatisch default.</p>
        : <div className="admin-table-wrap">
            <table className="admin-table admin-table--fixed admin-provider__products-table">
              <thead>
                <tr>
                  <th>Product</th>
                  <th>Categorie</th>
                  <th>Provider product-ID</th>
                  <th>Location</th>
                  <th>Status</th>
                  <th>Laatst gezien</th>
                  <th aria-label="Acties"/>
                </tr>
              </thead>
              <tbody>
                {products.map(product=><tr key={product.id}>
                  <td><strong>{product.name}</strong>{product.isDefault&&<span className="admin-tag admin-tag--active">Default</span>}</td>
                  <td>{product.categoryName??product.categoryId??"—"}</td>
                  <td className="admin-code admin-code--table">{product.providerProductId}</td>
                  <td className="admin-code admin-code--table">{product.location}</td>
                  <td><span className={adminStatusClass(product.isAvailable?"active":"neutral")}>{formatAdminAvailability(product.isAvailable)}</span></td>
                  <td>{formatAdminDateTime(product.lastSeenAt)}</td>
                  <td className="admin-table__actions">
                    {!product.isDefault&&product.isAvailable
                      ? <Button className="admin-action--compact" variant="secondary" onClick={()=>void makeDefault(product)} disabled={changingDefault!==undefined}>
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
          <p>Rechtstreeks uit 2Park voor het default parkeerproduct. Start, einde en status hieronder zijn providerwaarden; de lokale Visit en lokale ProviderParkingAction kunnen daarvan afwijken.</p>
        </div>
        <Button className="admin-action--compact" variant="secondary" onClick={()=>void load()} disabled={loading}>
          {loading?"Vernieuwen…":"Vernieuwen"}
        </Button>
      </div>

      <p className="admin-provider__meta">Opgehaald: {formatAdminDateTime(status.actionsRetrievedAt)}</p>

      {status.actions.length===0
        ? <p className="admin-provider__empty">Geen actuele provideracties ontvangen.</p>
        : <div className="admin-table-wrap">
            <table className="admin-table admin-table--fixed admin-provider__actions-table">
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
                {status.actions.map(action=>{
                  const tone=adminProviderActionStatusTone(action.status);
                  return <tr key={action.providerActionId}>
                    <td className="admin-code admin-code--table">{action.providerActionId}</td>
                    <td><LicensePlate value={action.licensePlate}/></td>
                    <td>{formatAdminDateTime(action.start)}</td>
                    <td>{formatAdminDateTime(action.end)}</td>
                    <td className="admin-code admin-code--table">{action.location}</td>
                    <td><span className={adminStatusClass(tone)}>{formatAdminProviderActionStatus(action.status)}</span></td>
                  </tr>;
                })}
              </tbody>
            </table>
          </div>}
    </section>}
  </div>;
}
