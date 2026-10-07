import { useEffect,useState } from "react";
import {
  changeVisitEndTime,
  getAdminDashboard,
  getAdminUserParkingPolicy,
  getVisit,
  stopVisit,
  type ActiveVisit,
  type AdminActiveVisitSummary,
  type AdminParkingPolicySummary,
  type AuthenticatedUser,
  type VehicleSummary
} from "../../api/client";
import { ActiveVisitCard } from "../../components/ActiveVisitCard";
import { activeVisitHealthMessage,canExtendActiveVisit,canStopActiveVisit } from "../../components/activeVisitPresentation";
import { LicensePlate } from "../../components/LicensePlate";
import { VisitActions } from "../../components/VisitActions";
import { Button } from "../../design/primitives/Button";
import { Card } from "../../design/primitives/Card";
import { Loading } from "../../design/primitives/Loading";
import { clearPendingOperation,getOrCreatePendingOperation } from "../../pendingOperations";
import "./VisitDetailPage.css";

type Props={
  visitId:string;
  vehicles:VehicleSummary[];
  onBack:()=>void;
  backLabel:string;
  currentUserId:string;
  userRole:AuthenticatedUser["role"];
};

function format(value:string|null){
  return value?new Date(value).toLocaleString("nl-NL",{dateStyle:"medium",timeStyle:"short"}):"—";
}

function statusLabel(status:ActiveVisit["status"]){
  switch(status){
    case "Completed": return "Afgerond";
    case "Cancelled": return "Geannuleerd";
    case "Active": return "Actief";
    case "Starting": return "Wordt gestart";
    case "Stopping": return "Wordt gestopt";
  }
}

function duration(visit:ActiveVisit){
  if(!visit.actualEndAt)return null;
  const minutes=Math.max(0,Math.floor((new Date(visit.actualEndAt).getTime()-new Date(visit.startAt).getTime())/60000));
  const hours=Math.floor(minutes/60);
  const rest=minutes%60;
  return hours>0?`${hours} u ${rest} min`:`${rest} min`;
}

function elapsedSince(startAt:string,now:number){
  const seconds=Math.max(0,Math.floor((now-new Date(startAt).getTime())/1000));
  const hours=Math.floor(seconds/3600);
  const minutes=Math.floor((seconds%3600)/60);
  const rest=seconds%60;
  return [hours,minutes,rest].map(value=>value.toString().padStart(2,"0")).join(":");
}

export function VisitDetailPage({
  visitId,
  vehicles,
  onBack,
  backLabel,
  currentUserId,
  userRole
}:Props){
  const[visit,setVisit]=useState<ActiveVisit|null|undefined>();
  const[error,setError]=useState<string|null>(null);
  const[adminSummary,setAdminSummary]=useState<AdminActiveVisitSummary|null>(null);
  const[adminPolicy,setAdminPolicy]=useState<AdminParkingPolicySummary|null|undefined>();
  const[adminLoading,setAdminLoading]=useState(false);
  const[now,setNow]=useState(()=>Date.now());
  const[stopping,setStopping]=useState(false);
  const[extending,setExtending]=useState(false);
  const[actionError,setActionError]=useState<string|null>(null);
  const[statusMessage,setStatusMessage]=useState<string|null>(null);

  useEffect(()=>{
    setVisit(undefined);
    setError(null);
    setAdminSummary(null);
    setAdminPolicy(undefined);
    if(!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(visitId)){
      setVisit(null);
      return;
    }
    getVisit(visitId).then(setVisit).catch(e=>setError(e instanceof Error?e.message:"Parkeeractie kon niet worden geladen."));
  },[visitId]);

  useEffect(()=>{
    if(!visit||userRole!=="Admin"||visit.userId===currentUserId||!["Starting","Active","Stopping"].includes(visit.status)){
      setAdminSummary(null);
      setAdminPolicy(undefined);
      setAdminLoading(false);
      return;
    }

    let cancelled=false;
    const load=async()=>{
      setAdminLoading(true);
      try{
        const dashboard=await getAdminDashboard();
        const summary=dashboard.activeVisits.find(item=>item.id===visit.id)??null;
        if(cancelled)return;
        setAdminSummary(summary);
        if(summary){
          const policy=await getAdminUserParkingPolicy(summary.userId);
          if(!cancelled)setAdminPolicy(policy);
        }else{
          setAdminPolicy(null);
        }
      }catch(e){
        if(!cancelled)setError(e instanceof Error?e.message:"Lopende parkeeractie kon niet worden geladen.");
      }finally{
        if(!cancelled)setAdminLoading(false);
      }
    };

    void load();
    return()=>{cancelled=true;};
  },[currentUserId,userRole,visit]);

  useEffect(()=>{
    if(!adminSummary)return;
    setNow(Date.now());
    const timer=window.setInterval(()=>setNow(Date.now()),1000);
    return()=>window.clearInterval(timer);
  },[adminSummary?.id]);

  if(error)return <Card><div className="visit-detail"><p role="alert" style={{margin:0}}>{error}</p><Button variant="secondary" onClick={onBack}>← {backLabel}</Button></div></Card>;
  if(visit===undefined||adminLoading)return <Loading label="Parkeeractie laden"/>;
  if(visit===null)return <Card><div className="visit-detail"><span>Deze parkeeractie bestaat niet meer of kon niet worden gevonden.</span><Button variant="secondary" onClick={onBack}>← {backLabel}</Button></div></Card>;

  if(adminSummary){
    const maxExtensionMinutes=adminPolicy?.maxVisitElapsedDurationMinutes===undefined
      ? undefined
      : adminPolicy.maxVisitElapsedDurationMinutes===null
        ? null
        : Math.max(0,Math.floor((
            new Date(visit.startAt).getTime()+
            adminPolicy.maxVisitElapsedDurationMinutes*60000-
            (visit.desiredEndAt?new Date(visit.desiredEndAt).getTime():Date.now())
          )/60000));

    const stopManagedVisit=async()=>{
      if(stopping||extending||!canStopActiveVisit(visit))return;
      setStopping(true);
      setActionError(null);
      try{
        const operationId=getOrCreatePendingOperation("stop",visit.id);
        const result=await stopVisit(visit.id,operationId);
        setVisit(result.visit);
        if(result.reconciliationRequired){
          setStatusMessage("Stoppen is aangevraagd. De parkeerprovider bevestigt de stop nog.");
          return;
        }
        clearPendingOperation("stop",visit.id);
        onBack();
      }catch(e){
        setActionError(e instanceof Error?e.message:"Parkeeractie kon niet worden gestopt.");
      }finally{
        setStopping(false);
      }
    };

    const extendManagedVisit=async(hours:number)=>{
      if(
        extending||
        stopping||
        !canExtendActiveVisit(visit)||
        !adminPolicy||
        !adminPolicy.allowVisitExtension
      )return;

      const remainingMinutes=adminPolicy.maxVisitElapsedDurationMinutes===null
        ? null
        : Math.max(0,Math.floor((
            new Date(visit.startAt).getTime()+
            adminPolicy.maxVisitElapsedDurationMinutes*60000-
            (visit.desiredEndAt?new Date(visit.desiredEndAt).getTime():Date.now())
          )/60000));
      const maxHours=remainingMinutes===null?null:Math.max(1,Math.floor(remainingMinutes/60));
      if(!Number.isInteger(hours)||hours<1||(maxHours!==null&&hours>maxHours))return;

      setExtending(true);
      setActionError(null);
      try{
        const base=visit.desiredEndAt?new Date(visit.desiredEndAt):new Date();
        const desiredEndAt=new Date(base.getTime()+hours*60*60*1000).toISOString();
        const logicalKey=`${visit.id}:${desiredEndAt}`;
        const operationId=getOrCreatePendingOperation("end-time",logicalKey);
        const result=await changeVisitEndTime(visit.id,operationId,desiredEndAt);
        setVisit(result.visit);
        if(!result.reconciliationRequired)clearPendingOperation("end-time",logicalKey);
        setStatusMessage(result.reconciliationRequired
          ?"Wijzigen van de eindtijd is aangevraagd. De parkeerprovider bevestigt de wijziging nog."
          :null);
      }catch(e){
        setActionError(e instanceof Error?e.message:"Parkeeractie kon niet worden verlengd.");
      }finally{
        setExtending(false);
      }
    };

    return <div className="visit-live-detail">
      <div className="visit-live-detail__heading">
        <h2>Lopende parkeeractie van <strong>{adminSummary.username}</strong></h2>
      </div>
      <ActiveVisitCard
        vehicle={adminSummary.licensePlate}
        elapsed={elapsedSince(visit.startAt,now)}
        startTime={visit.startAt}
        endTime={visit.desiredEndAt}
        health={visit.health}
      />
      {activeVisitHealthMessage(visit.health)?<Card><p role="alert" style={{margin:0}}>{activeVisitHealthMessage(visit.health)}</p></Card>:null}
      {statusMessage?<Card><p role="status" style={{margin:0}}>{statusMessage}</p></Card>:null}
      <VisitActions
        stopping={stopping}
        extending={extending}
        error={actionError}
        maxExtensionMinutes={maxExtensionMinutes}
        allowManualStop={canStopActiveVisit(visit)}
        manualStopDisabledMessage={visit.status!=="Active"
          ?"Wacht tot de lopende parkeeractie door de parkeerprovider is bevestigd."
          :visit.health==="Reconciling"
            ?"De parkeerproviderstatus wordt nog gecontroleerd."
            :undefined}
        allowExtension={canExtendActiveVisit(visit)&&adminPolicy?.allowVisitExtension===true}
        extensionDisabledMessage={visit.status!=="Active"
          ?"Wacht tot de lopende parkeeractie door de parkeerprovider is bevestigd."
          :visit.health!=="Healthy"
            ?"Verlengen is geblokkeerd omdat de providerstatus aandacht vraagt."
            :adminPolicy===undefined
              ?"Parkeerbeleid kon niet worden geladen. Verlengen is daarom tijdelijk niet mogelijk."
              :adminPolicy===null
                ?"Parkeerbeleid voor deze gebruiker is niet beschikbaar."
                :!adminPolicy.allowVisitExtension
                  ?"Verlengen is niet toegestaan volgens het parkeerbeleid van deze gebruiker."
                  :undefined}
        onStop={stopManagedVisit}
        onExtend={extendManagedVisit}
      />
      <Button className="visit-detail__back" variant="secondary" onClick={onBack}>
        ← {backLabel}
      </Button>
    </div>;
  }

  const vehicle=vehicles.find(x=>x.id===visit.vehicleId);
  const elapsed=duration(visit);

  return <Card>
    <div className="visit-detail">
      <div className="visit-detail__header">
        {vehicle?<LicensePlate value={vehicle.licensePlate}/>:<strong>Parkeeractie</strong>}
        <span className="visit-detail__status"><span className="visit-detail__status-dot" aria-hidden="true"/>{statusLabel(visit.status)}</span>
      </div>

      <dl className="visit-detail__facts">
        <div className="visit-detail__fact"><dt>Gestart</dt><dd>{format(visit.startAt)}</dd></div>
        {visit.actualEndAt&&<div className="visit-detail__fact"><dt>Gestopt</dt><dd>{format(visit.actualEndAt)}</dd></div>}
        {elapsed&&<div className="visit-detail__fact visit-detail__duration"><dt>Duur</dt><dd>{elapsed}</dd></div>}
        <div className="visit-detail__fact visit-detail__fact--muted"><dt>Gepland tot</dt><dd>{format(visit.desiredEndAt)}</dd></div>
      </dl>

      <Button className="visit-detail__back" variant="secondary" onClick={onBack}>← {backLabel}</Button>
    </div>
  </Card>;
}
