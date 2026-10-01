import { useEffect,useMemo,useState } from "react";
import { getAdminUsageAnalysis,type AdminUsageAnalysis,type AdminUsageAnalysisGroup } from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import "./AdminAnalysisPage.css";

type Mode="user"|"plate";

function dateInput(date:Date){
  const pad=(value:number)=>String(value).padStart(2,"0");
  return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())}`;
}

function formatMinutes(value:number|null){
  if(value===null)return "Onvolledig";
  if(value<60)return `${value} min`;
  const hours=Math.floor(value/60);
  const minutes=value%60;
  return minutes===0?`${hours} uur`:`${hours} u ${minutes} min`;
}

function formatMoney(value:number|null){
  return value===null?"Onvolledig":new Intl.NumberFormat("nl-NL",{style:"currency",currency:"EUR"}).format(value);
}

function formatDateTime(value:string){
  return new Date(value).toLocaleString("nl-NL",{dateStyle:"short",timeStyle:"short"});
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
    <nav className="admin-analysis__tabs" aria-label="Bezoeken">
      <a className="admin-analysis__tab" href="/beheer/bezoeken">Bezoeken</a>
      <a className="admin-analysis__tab" href="/beheer/verbruik">Verbruik & kosten</a>
      <a className="admin-analysis__tab active" href="/beheer/analyse">Analyse</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}

    <section className="admin-analysis__panel">
      <div className="admin-analysis__controls">
        <label className="admin-analysis__field">
          <span>Van</span>
          <input type="date" value={fromDate} onChange={event=>setFromDate(event.target.value)}/>
        </label>
        <label className="admin-analysis__field">
          <span>Tot en met</span>
          <input type="date" value={toDate} onChange={event=>setToDate(event.target.value)}/>
        </label>
        <Button onClick={()=>void load()} disabled={loading}>{loading?"Analyseren…":"Analyseren"}</Button>
      </div>
    </section>

    <section className="admin-analysis__panel">
      <div className="admin-analysis__switch">
        <Button variant={mode==="user"?"primary":"secondary"} onClick={()=>setMode("user")}>Per bezoeker</Button>
        <Button variant={mode==="plate"?"primary":"secondary"} onClick={()=>setMode("plate")}>Per kenteken</Button>
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
                    <th>Visits</th>
                    <th>Betaalde tijd</th>
                    <th>Kosten</th>
                    <th>Compleet</th>
                    <th/>
                  </tr>
                </thead>
                <tbody>
                  {groups.map(group=><>
                    <tr key={group.key}>
                      <td>
                        <span className="admin-analysis__label">
                          <strong>{group.label}</strong>
                          {group.isArchived&&<span className="admin-analysis__archived">Gearchiveerd</span>}
                        </span>
                      </td>
                      <td>{group.visitCount}</td>
                      <td className={group.paidDurationMinutes===null?"admin-analysis__warning":undefined}>{formatMinutes(group.paidDurationMinutes)}</td>
                      <td className={group.amount===null?"admin-analysis__warning":undefined}>{formatMoney(group.amount)}</td>
                      <td>{group.isComplete?"Ja":"Nee"}</td>
                      <td><button className="admin-analysis__expand" type="button" onClick={()=>toggle(group)}>{expanded===group.key?"Verbergen":"Bekijk Visits"}</button></td>
                    </tr>
                    {expanded===group.key&&<tr className="admin-analysis__details" key={group.key+"-details"}>
                      <td colSpan={6}>
                        <div className="admin-analysis__visit-list">
                          {group.visits.map(visit=><div className="admin-analysis__visit" key={visit.visitId}>
                            <div><strong>{visit.username}</strong><small> · {visit.licensePlate}</small></div>
                            <div><small>{formatDateTime(visit.startAt)}</small></div>
                            <div><small>{formatMinutes(visit.paidDurationMinutes)} · {formatMoney(visit.amount)}</small></div>
                            <a href={"/beheer/bezoeken/"+visit.visitId}>Visit openen</a>
                          </div>)}
                        </div>
                      </td>
                    </tr>}
                  </>)}
                </tbody>
              </table>
            </div>}
    </section>
  </div>;
}
