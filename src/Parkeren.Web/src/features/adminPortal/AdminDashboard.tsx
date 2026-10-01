import { useEffect,useMemo,useState } from "react";
import {
  getAdminDashboard,
  getAdminUserParkingPolicy,
  getAssignedVehicles,
  getUsers,
  startVisit,
  stopVisit,
  type AdminDashboardSummary,
  type AdminParkingPolicySummary,
  type UserSummary,
  type VehicleSummary
} from "../../api/client";
import { LicensePlate } from "../../components/LicensePlate";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import { clearPendingOperation,getOrCreatePendingOperation } from "../../pendingOperations";
import "./AdminDashboard.css";

function formatDateTime(value:string|null){
  return value
    ? new Date(value).toLocaleString("nl-NL",{dateStyle:"short",timeStyle:"short"})
    : "Tot handmatig stoppen";
}

function formatPaidMinutes(value:number|null){
  if(value===null)return "Niet beschikbaar";
  if(value<60)return `${value} min`;
  const hours=Math.floor(value/60);
  const minutes=value%60;
  return minutes===0?`${hours} u`:`${hours} u ${minutes} min`;
}

function statusLabel(status:AdminDashboardSummary["activeVisits"][number]["status"]){
  switch(status){
    case "Starting": return "Wordt gestart";
    case "Active": return "Actief";
    case "Stopping": return "Wordt gestopt";
    case "Completed": return "Afgerond";
    case "Cancelled": return "Geannuleerd";
  }
}

function maxStartDurationMinutes(policy:AdminParkingPolicySummary|null|undefined){
  if(!policy)return null;
  const limits=[policy.maxPaidParkingDurationMinutes,policy.maxVisitElapsedDurationMinutes]
    .filter((value):value is number=>value!==null&&value>0);
  return limits.length>0?Math.min(...limits):480;
}

function durationOptions(maxMinutes:number|null){
  if(maxMinutes===null||maxMinutes<=0)return [];
  const values:number[]=[];
  for(let minutes=60;minutes<=maxMinutes;minutes+=60)values.push(minutes);
  if(values.length===0||values[values.length-1]!==maxMinutes)values.push(maxMinutes);
  return values;
}

export function AdminDashboard(){
  const[dashboard,setDashboard]=useState<AdminDashboardSummary>();
  const[users,setUsers]=useState<UserSummary[]>([]);
  const[selectedUserId,setSelectedUserId]=useState("");
  const[vehicles,setVehicles]=useState<VehicleSummary[]>();
  const[selectedVehicleId,setSelectedVehicleId]=useState("");
  const[policy,setPolicy]=useState<AdminParkingPolicySummary|null>();
  const[durationMinutes,setDurationMinutes]=useState(240);
  const[loading,setLoading]=useState(true);
  const[error,setError]=useState<string>();
  const[message,setMessage]=useState<string>();
  const[starting,setStarting]=useState(false);
  const[stoppingVisitId,setStoppingVisitId]=useState<string>();

  async function refreshDashboard(){
    const result=await getAdminDashboard();
    setDashboard(result);
    return result;
  }

  async function refreshAll(){
    setLoading(true);
    setError(undefined);
    try{
      const[result,userList]=await Promise.all([getAdminDashboard(),getUsers()]);
      setDashboard(result);
      const visitors=userList.filter(user=>user.role==="Visitor"&&user.isActive);
      setUsers(visitors);
      setSelectedUserId(current=>visitors.some(user=>user.id===current)?current:(visitors[0]?.id??""));
    }catch(e){
      setError(e instanceof Error?e.message:"Beheeroverzicht kon niet worden geladen.");
    }finally{
      setLoading(false);
    }
  }

  useEffect(()=>{void refreshAll();},[]);

  useEffect(()=>{
    if(!selectedUserId){
      setVehicles([]);
      setPolicy(null);
      setSelectedVehicleId("");
      return;
    }

    let cancelled=false;
    setVehicles(undefined);
    setPolicy(undefined);
    Promise.all([
      getAssignedVehicles(selectedUserId),
      getAdminUserParkingPolicy(selectedUserId)
    ]).then(([assigned,parkingPolicy])=>{
      if(cancelled)return;
      const activeVehicles=assigned.filter(vehicle=>vehicle.isActive);
      setVehicles(activeVehicles);
      setPolicy(parkingPolicy);
      setSelectedVehicleId(current=>activeVehicles.some(vehicle=>vehicle.id===current)?current:(activeVehicles[0]?.id??""));
    }).catch(e=>{
      if(cancelled)return;
      setVehicles([]);
      setPolicy(null);
      setSelectedVehicleId("");
      setError(e instanceof Error?e.message:"Startgegevens voor de bezoeker konden niet worden geladen.");
    });

    return()=>{cancelled=true;};
  },[selectedUserId]);

  const maxDuration=maxStartDurationMinutes(policy);
  const durations=useMemo(()=>durationOptions(maxDuration),[maxDuration]);

  useEffect(()=>{
    if(durations.length===0)return;
    if(!durations.includes(durationMinutes)){
      const preferred=durations.filter(value=>value<=240).at(-1)??durations[0];
      setDurationMinutes(preferred);
    }
  },[durations,durationMinutes]);

  const attentionCount=dashboard?.activeVisits.filter(visit=>visit.health!=="Healthy").length??0;
  const selectedUserActiveCount=dashboard?.activeVisits.filter(visit=>visit.userId===selectedUserId).length??0;
  const capacityFull=dashboard!==undefined&&dashboard.used>=dashboard.total;
  const userLimitReached=policy!==undefined&&policy!==null&&selectedUserActiveCount>=policy.maxConcurrentVisits;
  const startDisabled=
    starting||
    !selectedUserId||
    !selectedVehicleId||
    policy===undefined||
    policy===null||
    vehicles===undefined||
    vehicles.length===0||
    durations.length===0||
    capacityFull||
    userLimitReached;

  let startDisabledMessage:string|undefined;
  if(capacityFull)startDisabledMessage=`Alle ${dashboard?.total??0} parkeerplaatsen zijn in gebruik.`;
  else if(userLimitReached)startDisabledMessage=`Deze bezoeker heeft het maximum van ${policy?.maxConcurrentVisits??0} actieve parkeeractie(s) bereikt.`;
  else if(policy===null)startDisabledMessage="Het parkeerbeleid voor deze bezoeker is niet beschikbaar.";
  else if(vehicles!==undefined&&vehicles.length===0)startDisabledMessage="Deze bezoeker heeft geen actief toegewezen voertuig.";

  async function handleStart(){
    if(startDisabled||!selectedUserId||!selectedVehicleId)return;
    setStarting(true);
    setError(undefined);
    setMessage(undefined);

    const desiredEndAt=new Date(Date.now()+durationMinutes*60_000).toISOString();
    const logicalKey=`${selectedUserId}:${selectedVehicleId}:${desiredEndAt}`;
    const operationId=getOrCreatePendingOperation("start",logicalKey);

    try{
      const result=await startVisit(selectedVehicleId,desiredEndAt,operationId,selectedUserId);
      if(!result.reconciliationRequired)clearPendingOperation("start",logicalKey);
      setMessage(result.reconciliationRequired
        ?"Parkeren is aangevraagd; de providerbevestiging loopt nog."
        :"Parkeerbezoek is gestart.");
      await refreshDashboard();
    }catch(e){
      setError(e instanceof Error?e.message:"Parkeerbezoek kon niet worden gestart.");
    }finally{
      setStarting(false);
    }
  }

  async function handleStop(visitId:string){
    setStoppingVisitId(visitId);
    setError(undefined);
    setMessage(undefined);
    const operationId=getOrCreatePendingOperation("stop",visitId);

    try{
      const result=await stopVisit(visitId,operationId);
      if(!result.reconciliationRequired)clearPendingOperation("stop",visitId);
      setMessage(result.reconciliationRequired
        ?"Stoppen is aangevraagd; de providerbevestiging loopt nog."
        :"Parkeerbezoek is gestopt.");
      await refreshDashboard();
    }catch(e){
      setError(e instanceof Error?e.message:"Parkeerbezoek kon niet worden gestopt.");
    }finally{
      setStoppingVisitId(undefined);
    }
  }

  if(loading)return <Loading label="Beheeroverzicht laden"/>;

  return <div className="admin-dashboard">
    {error&&<Alert tone="danger">{error}</Alert>}
    {message&&<Alert>{message}</Alert>}

    <div className="admin-dashboard__metrics">
      <section className="admin-dashboard__metric">
        <span>Actieve bezoeken</span>
        <strong>{dashboard?.used??0} / {dashboard?.total??0}</strong>
      </section>
      <section className="admin-dashboard__metric">
        <span>Aandacht vereist</span>
        <strong>{attentionCount}</strong>
      </section>
    </div>

    <div className="admin-dashboard__grid">
      <section className="admin-dashboard__panel">
        <div className="admin-dashboard__panel-header">
          <div>
            <h2>Parkeren starten</h2>
            <p>Start een Visit namens een actieve bezoeker met diens eigen parkeerbeleid.</p>
          </div>
        </div>

        {users.length===0
          ? <p className="admin-dashboard__empty">Er zijn geen actieve bezoekers beschikbaar.</p>
          : <div className="admin-dashboard__form">
              <label className="admin-dashboard__field">
                <span>Bezoeker</span>
                <select value={selectedUserId} onChange={event=>setSelectedUserId(event.target.value)} disabled={starting}>
                  {users.map(user=><option key={user.id} value={user.id}>{user.username}</option>)}
                </select>
              </label>

              <label className="admin-dashboard__field">
                <span>Voertuig</span>
                <select value={selectedVehicleId} onChange={event=>setSelectedVehicleId(event.target.value)} disabled={starting||vehicles===undefined||vehicles.length===0}>
                  {(vehicles??[]).map(vehicle=><option key={vehicle.id} value={vehicle.id}>{vehicle.licensePlate}</option>)}
                </select>
              </label>

              <label className="admin-dashboard__field">
                <span>Parkeerduur</span>
                <select value={durationMinutes} onChange={event=>setDurationMinutes(Number(event.target.value))} disabled={starting||durations.length===0}>
                  {durations.map(minutes=><option key={minutes} value={minutes}>{formatPaidMinutes(minutes)}</option>)}
                </select>
              </label>

              {startDisabledMessage&&<p className="admin-dashboard__hint">{startDisabledMessage}</p>}
              <Button onClick={()=>void handleStart()} disabled={startDisabled}>
                {starting?"Starten…":"Parkeren starten"}
              </Button>
            </div>}
      </section>

      <section className="admin-dashboard__panel">
        <div className="admin-dashboard__panel-header">
          <div>
            <h2>Actieve bezoeken</h2>
            <p>Serverstatus van alle Visits die momenteel capaciteit bezetten.</p>
          </div>
          <Button className="admin-dashboard__refresh" variant="secondary" onClick={()=>void refreshAll()}>
            Vernieuwen
          </Button>
        </div>

        {!dashboard||dashboard.activeVisits.length===0
          ? <p className="admin-dashboard__empty">Er zijn momenteel geen actieve bezoeken.</p>
          : <div className="admin-dashboard__table-wrap">
              <table className="admin-dashboard__table">
                <thead>
                  <tr>
                    <th>Bezoeker</th>
                    <th>Kenteken</th>
                    <th>Gestart</th>
                    <th>Gepland tot</th>
                    <th>Betaalde tijd</th>
                    <th>Status</th>
                    <th aria-label="Acties"/>
                  </tr>
                </thead>
                <tbody>
                  {dashboard.activeVisits.map(visit=><tr key={visit.id}>
                    <td>
                      <span className="admin-dashboard__visit-user">
                        <strong>{visit.username}</strong>
                        {visit.health!=="Healthy"&&<small>Aandacht: {visit.health}</small>}
                      </span>
                    </td>
                    <td><LicensePlate value={visit.licensePlate}/></td>
                    <td>{formatDateTime(visit.startAt)}</td>
                    <td>{formatDateTime(visit.desiredEndAt)}</td>
                    <td>{formatPaidMinutes(visit.paidDurationMinutes)}</td>
                    <td>
                      <span className={"admin-dashboard__status "+(visit.health!=="Healthy"?"admin-dashboard__status--warning":"")}>
                        {statusLabel(visit.status)}
                      </span>
                    </td>
                    <td>
                      <div className="admin-dashboard__actions">
                        <a className="admin-dashboard__details" href={`/beheer/bezoeken/${visit.id}`}>Details</a>
                        <Button
                          variant="secondary"
                          disabled={visit.status!=="Active"||stoppingVisitId===visit.id}
                          onClick={()=>void handleStop(visit.id)}
                        >
                          {stoppingVisitId===visit.id?"Stoppen…":"Stoppen"}
                        </Button>
                      </div>
                    </td>
                  </tr>)}
                </tbody>
              </table>
            </div>}
      </section>
    </div>
  </div>;
}
