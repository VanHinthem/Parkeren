import { useEffect,useState } from "react";
import {
  getAdminBudgetPeriods,
  getAdminBudgetUsage,
  getAdminCostReport,
  getAdminProviderProducts,
  getAdminProviderStatus,
  type AdminBudgetPeriod,
  type AdminBudgetUsage,
  type AdminCostReport,
  type AdminProviderStatus
} from "../../api/client";
import { LicensePlate } from "../../components/LicensePlate";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import { formatAdminDate,formatAdminDateTime,formatAdminDuration,formatAdminMoney,formatAdminNumber } from "./adminFieldFormatters";
import { providerBalanceDiscrepancyMinutes } from "./providerBalanceComparison";
import "./AdminUsagePage.css";

function dateInput(date:Date){
  const pad=(value:number)=>String(value).padStart(2,"0");
  return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())}`;
}

function formatProviderBalance(status:AdminProviderStatus|undefined){
  const balance=status?.balance;
  if(!balance)return "Niet beschikbaar";
  switch(balance.unit){
    case "Euro": return formatAdminMoney(balance.remainingBalance);
    case "Minute": return formatAdminDuration(Math.round(balance.remainingBalance));
    case "Times": return `${formatAdminNumber(balance.remainingBalance)} keer`;
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
  const[usage,setUsage]=useState<AdminBudgetUsage|null>(null);
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
      const[products,provider]=await Promise.all([
        getAdminProviderProducts(),
        getAdminProviderStatus()
      ]);
      const defaultProduct=products.find(product=>product.isDefault);
      const budgetPeriods=defaultProduct
        ? await getAdminBudgetPeriods(defaultProduct.id)
        : [];
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

  const discrepancy=providerBalanceDiscrepancyMinutes({
    providerUnit:providerStatus?.balance?.unit,
    providerRemainingBalance:providerStatus?.balance?.remainingBalance,
    providerBalanceIsStale:providerStatus?.balanceIsStale??false,
    localRemainingPaidDurationMinutes:usage?.remainingPaidDurationMinutes??null,
    localUsageIsComplete:usage?.isComplete??false,
    periodValidFrom:usage?.period.validFrom,
    periodValidUntil:usage?.period.validUntil
  });

  return <div className="admin-usage">
    <nav className="admin-subnav" aria-label="Bezoeken">
      <a className="admin-subnav__link" href="/beheer/bezoeken">Bezoeken</a>
      <a className="admin-subnav__link active" href="/beheer/verbruik">Verbruik & kosten</a>
      <a className="admin-subnav__link" href="/beheer/analyse">Analyse</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}

    <section className="admin-usage__panel">
      <h2>Budgetgebruik</h2>
      <p>Lokale berekening telt alleen betaalde tijd van afgeronde Visits van het huidige parkeerproduct. Het officiële 2Park-saldo wordt voor datzelfde product als aparte bron getoond.</p>

      {periods?.length
        ? <label className="admin-field admin-usage__budget-period">
            <span>Budgetperiode</span>
            <select value={selectedPeriodId} onChange={event=>setSelectedPeriodId(event.target.value)}>
              {periods.map(period=><option key={period.id} value={period.id}>
                {formatAdminDate(period.validFrom)} – {formatAdminDate(period.validUntil)}
              </option>)}
            </select>
          </label>
        : <Alert tone="warning">Er is nog geen budgetperiode geconfigureerd. Voeg die toe onder Parkeerconfiguratie → Budgetten.</Alert>}

      {loadingUsage?<Loading label="Budgetgebruik berekenen"/>:usage?<div className="admin-metrics">
        <div className="admin-metric"><span>Lokaal gebruikt</span><strong className="admin-duration">{formatAdminDuration(usage.usedPaidDurationMinutes)}</strong><small>Alleen betaalde tijd van afgeronde Visits.</small></div>
        <div className="admin-metric"><span>Lokaal resterend</span><strong className="admin-duration">{formatAdminDuration(usage.remainingPaidDurationMinutes)}</strong><small>Op basis van geconfigureerde budgetlimiet.</small></div>
        <div className="admin-metric"><span>Totaal budget</span><strong className="admin-duration">{formatAdminDuration(usage.period.maximumPaidDurationMinutes)}</strong><small>{formatAdminDate(usage.period.validFrom)} – {formatAdminDate(usage.period.validUntil)}</small></div>
        <div className="admin-metric"><span>Officieel 2Park-saldo</span><strong className={providerStatus?.balance?.unit==="Euro"?"admin-money":providerStatus?.balance?.unit==="Minute"?"admin-duration":"admin-number"}>{formatProviderBalance(providerStatus)}</strong><small>{providerStatus?.balanceIsStale?"Laatst bekende, verouderde waarde":providerStatus?.balance?"Actueel volgens provider":"Niet beschikbaar"}</small></div>
      </div>:null}

      {usage&&!usage.isComplete&&<Alert tone="warning">De lokale budgetberekening is onvolledig doordat historische parkeerregels niet de volledige periode afdekken.</Alert>}
      {usage&&providerStatus?.balance?.unit!=="Minute"&&providerStatus?.balance&&<p className="admin-usage__empty">Het provider-saldo heeft unit {providerStatus.balance.unit} en is daarom niet rechtstreeks vergelijkbaar met het lokale urenbudget.</p>}
      {discrepancy!==null&&Math.abs(discrepancy)>=1&&<Alert tone="warning">Verschil lokaal versus provider: {formatAdminDuration(Math.round(Math.abs(discrepancy)))} {discrepancy>0?"meer resterend bij 2Park":"minder resterend bij 2Park"}. Dit wordt niet automatisch gecorrigeerd.</Alert>}
    </section>

    <section className="admin-usage__panel">
      <h2>Parkeerkosten</h2>
      <p>Selecteer een periode. Kosten worden per Visit berekend met de rules en tariefversie van het providerproduct waarmee die Visit is gestart.</p>
      <div className="admin-usage__controls">
        <label className="admin-field"><span>Van</span><input type="date" value={fromDate} onChange={event=>setFromDate(event.target.value)}/></label>
        <label className="admin-field"><span>Tot en met</span><input type="date" value={toDate} onChange={event=>setToDate(event.target.value)}/></label>
        <Button className="admin-action--field admin-usage__calculate" onClick={()=>void loadCosts()} disabled={loadingCosts}>{loadingCosts?"Berekenen…":"Berekenen"}</Button>
      </div>

      {report&&<div className="admin-metrics">
        <div className="admin-metric"><span>Betaalde tijd</span><strong className="admin-duration">{formatAdminDuration(report.totalPaidDurationMinutes)}</strong></div>
        <div className="admin-metric"><span>Totale kosten</span><strong className="admin-money">{formatAdminMoney(report.totalAmount,"Onvolledig")}</strong></div>
        <div className="admin-metric"><span>Visits</span><strong className="admin-number">{formatAdminNumber(report.visits.length)}</strong></div>
        <div className="admin-metric"><span>Berekening</span><strong className="admin-metric__status"><span className={`admin-status ${report.isComplete?"admin-status--active":"admin-status--warning"}`}>{report.isComplete?"Compleet":"Onvolledig"}</span></strong><small>{report.isComplete?"Providerhistorie, betaalde segmenten en tarieven zijn beschikbaar.":"Minimaal één Visit mist volledige providerhistorie of historische configuratie."}</small></div>
      </div>}

      {report&&!report.isComplete&&<Alert tone="warning">Totale kosten zijn niet beschikbaar zolang minstens één Visit incomplete providerhistorie of ontbrekende historische tariefconfiguratie heeft.</Alert>}

      {report&&report.visits.length===0?<p className="admin-usage__empty">Geen afgeronde Visits in deze periode.</p>:null}
      {report&&report.visits.length>0?<div className="admin-usage__table-wrap">
        <table className="admin-usage__table">
          <thead><tr><th>Bezoeker</th><th>Kenteken</th><th>Gestart</th><th>Geëindigd</th><th className="admin-duration admin-duration--table">Betaalde tijd</th><th className="admin-money admin-money--table">Kosten</th><th/></tr></thead>
          <tbody>{report.visits.map(visit=><tr key={visit.visitId}>
            <td><span className="admin-identity"><strong>{visit.username}</strong></span></td>
            <td><LicensePlate value={visit.licensePlate}/></td>
            <td>{formatAdminDateTime(visit.startAt)}</td>
            <td>{formatAdminDateTime(visit.actualEndAt)}</td>
            <td className="admin-duration admin-duration--table">{formatAdminDuration(visit.paidDurationMinutes)}</td>
            <td className={`${!visit.isComplete?"admin-usage__warning ":""}admin-money admin-money--table`}>{formatAdminMoney(visit.amount,"Onvolledig")}</td>
            <td><a className="admin-action-link" href={"/beheer/bezoeken/"+visit.visitId}>Details</a></td>
          </tr>)}</tbody>
        </table>
      </div>:null}
    </section>
  </div>;
}