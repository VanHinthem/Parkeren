import { Fragment,useEffect,useMemo,useState } from "react";
import { getAdminUsageAnalysis,type AdminUsageAnalysis,type AdminUsageAnalysisGroup } from "../../api/client";
import { LicensePlate } from "../../components/LicensePlate";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import { formatAdminDateTime,formatAdminDuration,formatAdminMoney,formatAdminNumber } from "./adminFieldFormatters";
import "./AdminAnalysisPage.css";

type Mode="user"|"plate";

function dateInput(date:Date){
  const pad=(value:number)=>String(value).padStart(2,"0");
  return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())}`;
}

export function AdminAnalysisPage(){
  const now=new Date();
  const[fromDate,setFromDate]=useState(dateInput(new Date(now.getFullYear(),now.getMonth(),1)));
  const[toDate,setToDate]=useState(dateInput(now));
  const[mode,setMode]=useState<Mode>("user");
  const[analysis,setAnalysis]=useState<AdminUsageAnalysis>();
  const[expanded,setExpanded]=useState<string>();
  const[loading,setLoading]=useState(false);
  const[error,setError]=useState<string>();

  async function load(){
    const from=new Date(fromDate+"T00:00:00");
    const inclusiveTo=new Date(toDate+"T00:00:00");
    const to=new Date(inclusiveTo);
    to.setDate(to.getDate()+1);

    if(Number.isNaN(from.getTime())||Number.isNaN(to.getTime())||to<=from){
      setError("Controleer de geselecteerde analyseperiode.");
      return;
    }

    setLoading(true);
    setError(undefined);
    setExpanded(undefined);
    try{
      setAnalysis(await getAdminUsageAnalysis(from.toISOString(),to.toISOString()));
    }catch(e){
      setError(e instanceof Error?e.message:"Analyse kon niet worden geladen.");
    }finally{
      setLoading(false);
    }
  }

  useEffect(()=>{void load();},[]);

  const groups=useMemo(
    ()=>mode==="user"?(analysis?.byUser??[]):(analysis?.byLicensePlate??[]),
    [analysis,mode]
  );

  function toggle(group:AdminUsageAnalysisGroup){
    setExpanded(current=>current===group.key?undefined:group.key);
  }

  return <div className="admin-analysis">
    <nav className="admin-subnav" aria-label="Bezoeken">
      <a className="admin-subnav__link" href="/beheer/bezoeken">Bezoeken</a>
      <a className="admin-subnav__link" href="/beheer/verbruik">Verbruik & kosten</a>
      <a className="admin-subnav__link active" href="/beheer/analyse">Analyse</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}

    <section className="admin-analysis__panel">
      <div className="admin-analysis__controls">
        <label className="admin-field">
          <span>Van</span>
          <input type="date" value={fromDate} onChange={event=>setFromDate(event.target.value)}/>
        </label>
        <label className="admin-field">
          <span>Tot en met</span>
          <input type="date" value={toDate} onChange={event=>setToDate(event.target.value)}/>
        </label>
        <Button className="admin-action--field admin-analysis__run" onClick={()=>void load()} disabled={loading}>{loading?"Analyseren…":"Analyseren"}</Button>
      </div>
    </section>

    <section className="admin-analysis__panel admin-analysis__mode-panel">
      <div className="admin-segmented" role="group" aria-label="Analyseweergave">
        <button className={`admin-segmented__option ${mode==="user"?"active":""}`} type="button" aria-pressed={mode==="user"} onClick={()=>setMode("user")}>Per bezoeker</button>
        <button className={`admin-segmented__option ${mode==="plate"?"active":""}`} type="button" aria-pressed={mode==="plate"} onClick={()=>setMode("plate")}>Per kenteken</button>
      </div>
    </section>

    <section className="admin-analysis__panel">
      {loading
        ? <Loading label="Analyse laden"/>
        : groups.length===0
          ? <p className="admin-analysis__empty">Geen afgeronde Visits in deze periode.</p>
          : <div className="admin-analysis__table-wrap">
              <table className="admin-analysis__table">
                <thead>
                  <tr>
                    <th>{mode==="user"?"Bezoeker":"Kenteken"}</th>
                    <th className="admin-number admin-number--table">Visits</th>
                    <th className="admin-duration admin-duration--table">Betaalde tijd</th>
                    <th className="admin-money admin-money--table">Kosten</th>
                    <th>Berekening</th>
                    <th aria-label="Details"/>
                  </tr>
                </thead>
                <tbody>
                  {groups.map(group=><Fragment key={group.key}>
                    <tr>
                      <td>
                        <span className="admin-analysis__label">
                          {mode==="plate"?<LicensePlate value={group.label}/>:<strong>{group.label}</strong>}
                          {group.isArchived&&<span className="admin-analysis__archived">Gearchiveerd</span>}
                        </span>
                      </td>
                      <td className="admin-number admin-number--table">{formatAdminNumber(group.visitCount)}</td>
                      <td className={`${group.paidDurationMinutes===null?"admin-analysis__warning ":""}admin-duration admin-duration--table`}>{formatAdminDuration(group.paidDurationMinutes,"Onvolledig")}</td>
                      <td className={`${group.amount===null?"admin-analysis__warning ":""}admin-money admin-money--table`}>{formatAdminMoney(group.amount,"Onvolledig")}</td>
                      <td><span className={`admin-status ${group.isComplete?"admin-status--active":"admin-status--warning"}`}>{group.isComplete?"Compleet":"Onvolledig"}</span></td>
                      <td><button className="admin-analysis__expand admin-action-link admin-action-link--muted" type="button" aria-expanded={expanded===group.key} onClick={()=>toggle(group)}>{expanded===group.key?"Verbergen ▴":"Tonen ▾"}</button></td>
                    </tr>
                    {expanded===group.key&&<tr className="admin-analysis__details">
                      <td colSpan={6}>
                        <div className="admin-analysis__visit-list">
                          <div className="admin-analysis__visit admin-analysis__visit--header" aria-hidden="true">
                            <span>Bezoeker</span><span>Kenteken</span><span>Gestart</span><span>Geëindigd</span><span>Betaalde tijd</span><span>Kosten</span><span/>
                          </div>
                          {group.visits.map(visit=><div className="admin-analysis__visit" key={visit.visitId}>
                            <span className="admin-identity"><strong>{visit.username}</strong></span>
                            <span><LicensePlate value={visit.licensePlate}/></span>
                            <span>{formatAdminDateTime(visit.startAt)}</span>
                            <span>{formatAdminDateTime(visit.actualEndAt)}</span>
                            <span className="admin-duration admin-duration--table">{formatAdminDuration(visit.paidDurationMinutes,"Onvolledig")}</span>
                            <span className="admin-money admin-money--table">{formatAdminMoney(visit.amount,"Onvolledig")}</span>
                            <a className="admin-action-link" href={"/beheer/bezoeken/"+visit.visitId}>Details</a>
                          </div>)}
                        </div>
                      </td>
                    </tr>}
                  </Fragment>)}
                </tbody>
              </table>
            </div>}
    </section>
  </div>;
}
