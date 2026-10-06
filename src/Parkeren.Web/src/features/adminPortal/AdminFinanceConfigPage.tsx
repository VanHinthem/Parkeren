import { useEffect,useState } from "react";
import {
  createAdminBudgetPeriod,
  createAdminParkingTariff,
  getAdminBudgetPeriods,
  getAdminParkingTariffs,
  getAdminProviderProducts,
  type AdminBudgetPeriod,
  type AdminParkingTariff,
  type AdminProviderProduct
} from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import { formatAdminDate,formatAdminDateTime,formatAdminDuration,formatAdminMoney } from "./adminFieldFormatters";
import "./adminFieldPresentation.css";
import "./AdminFinanceConfig.css";

type Props={mode:"budgets"|"tariffs"};

function dateInput(date:Date){
  const pad=(value:number)=>String(value).padStart(2,"0");
  return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())}`;
}

function localDateTimeInput(date:Date){
  const pad=(value:number)=>String(value).padStart(2,"0");
  return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function defaultBudgetDates(){
  const now=new Date();
  return {
    from:dateInput(new Date(now.getFullYear(),0,1)),
    until:dateInput(new Date(now.getFullYear()+1,0,1))
  };
}

function tariffStatus(item:AdminParkingTariff){
  const now=Date.now();
  const from=new Date(item.validFrom).getTime();
  const until=item.validUntil?new Date(item.validUntil).getTime():null;
  if(now<from)return "Gepland";
  if(until!==null&&now>=until)return "Historisch";
  return "Actief";
}

export function AdminFinanceConfigPage({mode}:Props){
  const[products,setProducts]=useState<AdminProviderProduct[]>();
  const[selectedProductId,setSelectedProductId]=useState<string>();
  const[budgets,setBudgets]=useState<AdminBudgetPeriod[]>();
  const[tariffs,setTariffs]=useState<AdminParkingTariff[]>();
  const[error,setError]=useState<string>();
  const[message,setMessage]=useState<string>();
  const[saving,setSaving]=useState(false);

  const defaults=defaultBudgetDates();
  const[budgetFrom,setBudgetFrom]=useState(defaults.from);
  const[budgetUntil,setBudgetUntil]=useState(defaults.until);
  const[budgetHours,setBudgetHours]=useState("1500");

  const[tariffFrom,setTariffFrom]=useState(localDateTimeInput(new Date()));
  const[tariffUntil,setTariffUntil]=useState("");
  const[tariffRate,setTariffRate]=useState("");

  async function load(productId:string){
    setError(undefined);
    try{
      if(mode==="budgets")setBudgets(await getAdminBudgetPeriods(productId));
      else setTariffs(await getAdminParkingTariffs(productId));
    }catch(e){
      setError(e instanceof Error?e.message:"Configuratie kon niet worden geladen.");
    }
  }

  useEffect(()=>{
    void (async()=>{
      try{
        const rows=await getAdminProviderProducts();
        setProducts(rows);
        const selected=rows.find(product=>product.isDefault)?.id
          ?? rows.find(product=>product.isAvailable)?.id
          ?? rows[0]?.id;
        setSelectedProductId(selected);
        if(!selected){
          setBudgets([]);
          setTariffs([]);
        }
      }catch(e){
        setError(e instanceof Error?e.message:"Providerproducten konden niet worden geladen.");
      }
    })();
  },[]);

  useEffect(()=>{
    if(selectedProductId)void load(selectedProductId);
  },[mode,selectedProductId]);

  async function saveBudget(){
    const selectedProduct=products?.find(product=>product.id===selectedProductId);
    if(!selectedProductId||!selectedProduct){
      setError("Selecteer eerst een 2Park-product.");
      return;
    }
    if(!selectedProduct.isAvailable){
      setError("Dit providerproduct is niet meer beschikbaar; historische configuratie blijft alleen-lezen.");
      return;
    }

    const from=new Date(budgetFrom+"T00:00:00");
    const until=new Date(budgetUntil+"T00:00:00");
    const hours=Number(budgetHours);
    if(Number.isNaN(from.getTime())||Number.isNaN(until.getTime())||until<=from||!Number.isFinite(hours)||hours<=0){
      setError("Controleer de budgetperiode en het aantal uren.");
      return;
    }

    setSaving(true);
    setError(undefined);
    setMessage(undefined);
    try{
      const result=await createAdminBudgetPeriod({
        providerProductId:selectedProductId,
        validFrom:from.toISOString(),
        validUntil:until.toISOString(),
        maximumPaidDurationMinutes:Math.round(hours*60)
      });
      if(result.outcome==="Created"){
        setMessage("Budgetperiode is toegevoegd.");
        await load(selectedProductId);
      }else if(result.outcome==="Overlap"){
        setError("Deze budgetperiode overlapt met een bestaande periode.");
      }else{
        setError("De budgetperiode bevat ongeldige waarden.");
      }
    }catch(e){
      setError(e instanceof Error?e.message:"Budgetperiode kon niet worden opgeslagen.");
    }finally{
      setSaving(false);
    }
  }

  async function saveTariff(){
    const selectedProduct=products?.find(product=>product.id===selectedProductId);
    if(!selectedProductId||!selectedProduct){
      setError("Selecteer eerst een 2Park-product.");
      return;
    }
    if(!selectedProduct.isAvailable){
      setError("Dit providerproduct is niet meer beschikbaar; historische configuratie blijft alleen-lezen.");
      return;
    }

    const from=new Date(tariffFrom);
    const until=tariffUntil?new Date(tariffUntil):null;
    const rate=Number(tariffRate.replace(",","."));
    if(Number.isNaN(from.getTime())||(until&&Number.isNaN(until.getTime()))||(until&&until<=from)||!Number.isFinite(rate)||rate<0){
      setError("Controleer de tariefperiode en het uurtarief.");
      return;
    }

    setSaving(true);
    setError(undefined);
    setMessage(undefined);
    try{
      const result=await createAdminParkingTariff({
        providerProductId:selectedProductId,
        validFrom:from.toISOString(),
        validUntil:until?until.toISOString():null,
        rate,
        unit:"Hour"
      });
      if(result.outcome==="Created"){
        setMessage("Tariefversie is toegevoegd.");
        setTariffRate("");
        await load(selectedProductId);
      }else if(result.outcome==="Overlap"){
        setError("Deze tariefversie overlapt met een bestaande versie.");
      }else{
        setError("De tariefversie bevat ongeldige waarden.");
      }
    }catch(e){
      setError(e instanceof Error?e.message:"Tariefversie kon niet worden opgeslagen.");
    }finally{
      setSaving(false);
    }
  }

  const financeRows=mode==="budgets"?budgets:tariffs;
  const loading=!products||financeRows===undefined;
  if(loading&&!error)return <Loading label={mode==="budgets"?"Budgetten laden":"Tarieven laden"}/>;
  if(!products||financeRows===undefined)return <div className="admin-finance">
    <Alert tone="danger">{error??"Providerproducten of financiële configuratie konden niet worden geladen."}</Alert>
  </div>;

  const selectedProduct=products.find(product=>product.id===selectedProductId);

  return <div className="admin-finance">
    <nav className="admin-subnav" aria-label="Parkeerconfiguratie">
      <a className="admin-subnav__link" href="/beheer/provider">Providerproducten</a>
      <a className="admin-subnav__link" href="/beheer/configuratie/parkeerregels">Parkeerregels</a>
      <a className={`admin-subnav__link ${mode==="tariffs"?"active":""}`} href="/beheer/configuratie/tarieven">Tarieven</a>
      <a className={`admin-subnav__link ${mode==="budgets"?"active":""}`} href="/beheer/configuratie/budgetten">Budgetten</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}
    {message&&<Alert>{message}</Alert>}

    <section className="admin-finance__panel">
      <h2>2Park-product</h2>
      {products.length===0
        ? <p>Er zijn nog geen providerproducten gesynchroniseerd. Synchroniseer ze eerst onder <a href="/beheer/provider">Provider & reconciliatie</a>.</p>
        : <div className="admin-finance__product-grid">
            <label className="admin-field">
              <span>Configuratie voor</span>
              <select value={selectedProductId??""} onChange={event=>setSelectedProductId(event.target.value)}>
                {products.map(product=><option key={product.id} value={product.id}>
                  {product.name}{product.isDefault?" · default":""}{product.isAvailable?"":" · niet beschikbaar"}
                </option>)}
              </select>
            </label>
            {selectedProductId&&<div className="admin-readonly-value admin-readonly-value--field">
              <span>Providercontext</span>
              <strong>{selectedProduct?.location??"—"}</strong>
            </div>}
          </div>}
    </section>

    {mode==="budgets"?<>
      <section className="admin-finance__panel">
        <h2>Budgetperiode toevoegen</h2>
        <p>Budgetperioden zijn append-only en mogen niet overlappen. Voor Oss is 1500 uur de huidige bekende jaarlimiet; pas dit hier aan wanneer de regeling verandert.</p>
        <div className="admin-finance__form">
          <div className="admin-finance__grid">
            <label className="admin-field"><span>Vanaf</span><input type="date" value={budgetFrom} onChange={event=>setBudgetFrom(event.target.value)}/></label>
            <label className="admin-field"><span>Tot</span><input type="date" value={budgetUntil} onChange={event=>setBudgetUntil(event.target.value)}/></label>
            <label className="admin-field"><span>Max. betaalde uren</span><input className="admin-field__control--number" type="number" min="0.25" step="0.25" value={budgetHours} onChange={event=>setBudgetHours(event.target.value)}/></label>
          </div>
          <div className="admin-finance__actions"><Button className="admin-action--compact" onClick={()=>void saveBudget()} disabled={saving||!selectedProduct?.isAvailable}>{saving?"Opslaan…":"Budgetperiode toevoegen"}</Button></div>
        </div>
      </section>

      <div className="admin-finance__items">
        {(budgets??[]).length===0?<section className="admin-finance__item"><p className="admin-finance__empty">Er zijn nog geen budgetperioden geconfigureerd.</p></section>:null}
        {(budgets??[]).map(item=><article className="admin-finance__item" key={item.id}>
          <div className="admin-finance__item-head"><div><h3>{formatAdminDate(item.validFrom)} → {formatAdminDate(item.validUntil,"doorlopend")}</h3><small>{item.id}</small></div></div>
          <div className="admin-finance__facts"><span className="admin-finance__badge admin-duration">{formatAdminDuration(item.maximumPaidDurationMinutes)}</span></div>
        </article>)}
      </div>
    </>:<>
      <section className="admin-finance__panel">
        <h2>Tariefversie toevoegen</h2>
        <p>Tarieven worden historisch bewaard. Een nieuwe open-ended versie sluit automatisch de vorige open-ended versie op dezelfde ingangsdatum. Bounded historische versies kun je backfillen zolang ze niet overlappen.</p>
        <div className="admin-finance__form">
          <div className="admin-finance__grid">
            <label className="admin-field"><span>Geldig vanaf</span><input type="datetime-local" value={tariffFrom} onChange={event=>setTariffFrom(event.target.value)}/></label>
            <label className="admin-field"><span>Geldig tot (optioneel)</span><input type="datetime-local" value={tariffUntil} onChange={event=>setTariffUntil(event.target.value)}/></label>
            <label className="admin-field"><span>Tarief per uur (€)</span><input className="admin-field__control--money" type="text" inputMode="decimal" value={tariffRate} onChange={event=>setTariffRate(event.target.value)} placeholder="bijv. 0,35"/></label>
          </div>
          <div className="admin-finance__actions"><Button className="admin-action--compact" onClick={()=>void saveTariff()} disabled={saving||!selectedProduct?.isAvailable}>{saving?"Opslaan…":"Tariefversie toevoegen"}</Button></div>
        </div>
      </section>

      <section className="admin-finance__panel">
        <h2>Tariefhistorie</h2>
        {(tariffs??[]).length===0
          ? <p className="admin-finance__empty">Er zijn nog geen tarieven geconfigureerd. Kostenrapportage blijft dan expliciet onvolledig.</p>
          : <div className="admin-table-wrap">
              <table className="admin-table admin-table--fixed admin-finance__tariff-table">
                <thead><tr><th>Geldig vanaf</th><th>Geldig tot</th><th className="admin-money admin-money--table">Tarief per uur</th><th>Status</th></tr></thead>
                <tbody>
                  {(tariffs??[]).map(item=>{
                    const status=tariffStatus(item);
                    return <tr key={item.id}>
                      <td>{formatAdminDateTime(item.validFrom)}</td>
                      <td>{formatAdminDateTime(item.validUntil,"Doorlopend")}</td>
                      <td className="admin-money admin-money--table">{formatAdminMoney(item.rate)}</td>
                      <td><span className={`admin-status ${status==="Actief"?"admin-status--active":""}`}>{status}</span></td>
                    </tr>;
                  })}
                </tbody>
              </table>
            </div>}
      </section>
    </>}
  </div>;
}
