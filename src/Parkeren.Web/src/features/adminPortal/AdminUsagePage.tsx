import { useEffect,useState } from "react";
import {
  getAdminBudgetPeriods,
  getAdminBudgetUsage,
  getAdminCostReport,
  getAdminProviderStatus,
  type AdminBudgetPeriod,
  type AdminBudgetUsage,
  type AdminCostReport,
  type AdminProviderStatus
} from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import "./AdminUsagePage.css";

function dateInput(date:Date){
  const pad=(value:number)=>String(value).padStart(2,"0");
  return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())}`;
}

function formatDate(value:string){
  return new Date(value).toLocaleDateString("nl-NL",{dateStyle:"medium"});
}

function formatMinutes(value:number|null){
  if(value===null)return "Onbekend";
  if(value<60)return `${value} min`;
  const hours=Math.floor(value/60);
  const minutes=value%60;
  return minutes===0?`${hours} uur`:`${hours} u ${minutes} min`;
}

function formatMoney(value:number|null){
  return value===null?"Onvolledig":new Intl.NumberFormat("nl-NL",{style:"currency",currency:"EUR"}).format(value);
}

function formatProviderBalance(status:AdminProviderStatus|undefined){
  const balance=status?.balance;
  if(!balance)return "Niet beschikbaar";
  switch(balance.unit){
    case "Euro": return new Intl.NumberFormat("nl-NL",{style:"currency",currency:"EUR"}).format(balance.remainingBalance);
    case "Minute": return formatMinutes(Math.round(balance.remainingBalance));
    case "Times": return `${balance.remainingBalance} keer`;
    default: return String(balance.remainingBalance);
  }
}

function currentBudgetId(periods:AdminBudgetPeriod[]){
  const now=Date.now();
  return periods.find(period=>{
    const from=new Date(period.validFrom).getTime();
    const until=new Date(period.validUntil).getTime();
    return from<=now&&now<until;
  })?.id??periods[0]?.id??"";
}

export function AdminUsagePage(){
  const[periods,setPeriods]=useState<AdminBudgetPeriod[]>();
  const[selectedPeriodId,setSelectedPeriodId]=useState("");
  const[usage,setUsage]=useState<AdminBudgetUsage|null>();
  const[providerStatus,setProviderStatus]=useState<AdminProviderStatus>();
  const[report,setReport]=useState<AdminCostReport>();
  const[loading,setLoading]=useState(true);
  const[loadingUsage,setLoadingUsage]=useState(false);
  const[loadingCosts,setLoadingCosts]=useState(false);
  const[error,setError]=useState<string>();

  const now=new Date();
  const[fromDate,setFromDate]=useState(dateInput(new Date(now.getFullYear(),now.getMonth(),1)));
  const[toDate,setToDate]=useState(dateInput(now));

  async function loadInitial(){
    setLoading(true);
    setError(undefined);
    try{
      const[budgetPeriods,provider]=await Promise.all([
        getAdminBudgetPeriods(),
        getAdminProviderStatus()
      ]);
      setPeriods(budgetPeriods);
      setProviderStatus(provider);
      setSelectedPeriodId(currentBudgetId(budgetPeriods));
    }catch(e){
      setError(e instanceof Error?e.message:"Verbruiksgegevens konden niet worden geladen.");
    }finally{
      setLoading(false);
    }
  }

  useEffect(()=>{void loadInitial();},[]);

  useEffect(()=>{
    if(!selectedPeriodId){
      setUsage(null);
      return;
    }
    let cancelled=false;
    setLoadingUsage(true);
    getAdminBudgetUsage(selectedPeriodId)
      .then(result=>{if(!cancelled)setUsage(result);})
      .catch(e=>{if(!cancelled)setError(e instanceof Error?e.message:"Budgetgebruik kon niet worden geladen.");})
      .finally(()=>{if(!cancelled)setLoadingUsage(false);});
    return()=>{cancelled=true;};
  },[selectedPeriodId]);

  async function loadCosts(){
    const from=new Date(fromDate+"T00:00:00");
    const inclusiveTo=new Date(toDate+"T00:00:00");
    const to=new Date(inclusiveTo);
    to.setDate(to.getDate()+1);
    if(Number.isNaN(from.getTime())||Number.isNaN(to.getTime())||to<=from){
      setError("Controleer de geselecteerde kostenperiode.");
      return;
    }

    setLoadingCosts(true);
    setError(undefined);
    try{
      setReport(await getAdminCostReport(from.toISOString(),to.toISOString()));
    }catch(e){
      setError(e instanceof Error?e.message:"Kostenrapport kon niet worden geladen.");
    }finally{
      setLoadingCosts(false);
    }
  }

  useEffect(()=>{if(!loading)void loadCosts();},[loading]);

  if(loading)return <Loading label="Verbruik en kosten laden"/>;

  const providerMinutes=providerStatus?.balance?.unit==="Minute"
    ? Number(providerStatus.balance.remainingBalance)
    : null;
  const localRemaining=usage?.remainingPaidDurationMinutes??null;
  const discrepancy=providerMinutes!==null&&localRemaining!==null
    ? providerMinutes-localRemaining
    : null;

  return <div className="admin-usage">
    <nav className="admin-usage__tabs" aria-label="Bezoeken">
      <a className="admin-usage__tab" href="/beheer/bezoeken">Bezoeken</a>
      <a className="admin-usage__tab active" href="/beheer/verbruik">Verbruik & kosten</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}

    <section className="admin-usage__panel">
      <h2>Budgetgebruik</h2>
      <p>Lokale berekening telt alleen betaalde tijd van afgeronde Visits. Het officiële 2Park-saldo blijft als aparte bron zichtbaar.</p>

      {periods?.length
        ? <label className="admin-usage__field">
            <span>Budgetperiode</span>
            <select value={selectedPeriodId} onChange={event=>setSelectedPeriodId(event.target.value)}>
              {periods.map(period=><option key={period.id} value={period.id}>
                {formatDate(period.validFrom)} – {formatDate(period.validUntil)}
              </option>)}
            </select>
          </label>
        : <Alert tone="warning">Er is nog geen budgetperiode geconfigureerd. Voeg die toe onder Parkeerconfiguratie → Budgetten.</Alert>}

      {loadingUsage?<Loading label="Budgetgebruik berekenen"/>:usage?<div className="admin-usage__metrics">
        <div className="admin-usage__metric"><span>Lokaal gebruikt</span><strong>{formatMinutes(usage.usedPaidDurationMinutes)}</strong><small>Alleen betaalde tijd van afgeronde Visits.</small></div>
        <div className="admin-usage__metric"><span>Lokaal resterend</span><strong>{formatMinutes(usage.remainingPaidDurationMinutes)}</strong><small>Op basis van geconfigureerde budgetlimiet.</small></div>
        <div className="admin-usage__metric"><span>Totaal budget</span><strong>{formatMinutes(usage.period.maximumPaidDurationMinutes)}</strong><small>{formatDate(usage.period.validFrom)} – {formatDate(usage.period.validUntil)}</small></div>
        <div className="admin-usage__metric"><span>Officieel 2Park-saldo</span><strong>{formatProviderBalance(providerStatus)}</strong><small>{providerStatus?.balanceIsStale?"Laatst bekende, verouderde waarde":providerStatus?.balance?"Actueel volgens provider":"Niet beschikbaar"}</small></div>
      </div>:null}

      {usage&&!usage.isComplete&&<Alert tone="warning">De lokale budgetberekening is onvolledig doordat historische parkeerregels niet de volledige periode afdekken.</Alert>}
      {usage&&providerStatus?.balance?.unit!=="Minute"&&providerStatus?.balance&&<p className="admin-usage__empty">Het provider-saldo heeft unit {providerStatus.balance.unit} en is daarom niet rechtstreeks vergelijkbaar met het lokale urenbudget.</p>}
      {discrepancy!==null&&Math.abs(discrepancy)>=1&&<Alert tone="warning">Verschil lokaal versus provider: {formatMinutes(Math.round(Math.abs(discrepancy)))} {discrepancy>0?"meer resterend bij 2Park":"minder resterend bij 2Park"}. Dit wordt niet automatisch gecorrigeerd.</Alert>}
    </section>

    <section className="admin-usage__panel">
      <h2>Parkeerkosten</h2>
      <p>Selecteer een periode. Kosten worden per betaald segment berekend met de tariefversie die op dat moment geldig was.</p>
      <div className="admin-usage__controls">
        <label className="admin-usage__field"><span>Van</span><input type="date" value={fromDate} onChange={event=>setFromDate(event.target.value)}/></label>
        <label className="admin-usage__field"><span>Tot en met</span><input type="date" value={toDate} onChange={event=>setToDate(event.target.value)}/></label>
        <Button onClick={()=>void loadCosts()} disabled={loadingCosts}>{loadingCosts?"Berekenen…":"Berekenen"}</Button>
      </div>

      {report&&<div className="admin-usage__metrics">
        <div className="admin-usage__metric"><span>Betaalde tijd</span><strong>{formatMinutes(report.totalPaidDurationMinutes)}</strong></div>
        <div className="admin-usage__metric"><span>Totale kosten</span><strong>{formatMoney(report.totalAmount)}</strong></div>
        <div className="admin-usage__metric"><span>Visits</span><strong>{report.visits.length}</strong></div>
        <div className="admin-usage__metric"><span>Berekening</span><strong>{report.isComplete?"Compleet":"Onvolledig"}</strong><small>{report.isComplete?"Alle betaalde segmenten hebben rules en tarieven.":"Minimaal één Visit mist historische configuratie."}</small></div>
      </div>}

      {report&&!report.isComplete&&<Alert tone="warning">Kosten zijn niet als totaal ingevuld zolang minimaal één betaald segment geen geldige historische tariefconfiguratie heeft.</Alert>}

      {report&&report.visits.length===0?<p className="admin-usage__empty">Geen afgeronde Visits in deze periode.</p>:null}
      {report&&report.visits.length>0?<div className="admin-usage__table-wrap">
        <table className="admin-usage__table">
          <thead><tr><th>Bezoeker</th><th>Kenteken</th><th>Gestart</th><th>Betaalde tijd</th><th>Kosten</th><th/></tr></thead>
          <tbody>{report.visits.map(visit=><tr key={visit.visitId}>
            <td>{visit.username}</td>
            <td>{visit.licensePlate}</td>
            <td>{new Date(visit.startAt).toLocaleString("nl-NL",{dateStyle:"short",timeStyle:"short"})}</td>
            <td>{formatMinutes(visit.paidDurationMinutes)}</td>
            <td className={!visit.isComplete?"admin-usage__warning":undefined}>{formatMoney(visit.amount)}</td>
            <td><a className="admin-usage__details" href={"/beheer/bezoeken/"+visit.visitId}>Details</a></td>
          </tr>)}</tbody>
        </table>
      </div>:null}
    </section>
  </div>;
}
