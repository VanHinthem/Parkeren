import { useEffect,useState } from "react";
import { getVisit,type ActiveVisit,type VehicleSummary } from "../../api/client";
import { LicensePlate } from "../../components/LicensePlate";
import { Button } from "../../design/primitives/Button";
import { Card } from "../../design/primitives/Card";
import { Loading } from "../../design/primitives/Loading";
import "./VisitDetailPage.css";

type Props={visitId:string;vehicles:VehicleSummary[];onBack:()=>void};

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

export function VisitDetailPage({visitId,vehicles,onBack}:Props){
  const[visit,setVisit]=useState<ActiveVisit|null|undefined>();
  const[error,setError]=useState<string|null>(null);

  useEffect(()=>{
    setVisit(undefined);setError(null);
    if(!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(visitId)){setVisit(null);return;}
    getVisit(visitId).then(setVisit).catch(e=>setError(e instanceof Error?e.message:"Parkeeractie kon niet worden geladen."));
  },[visitId]);

  if(error)return <Card><div className="visit-detail"><p role="alert" style={{margin:0}}>{error}</p><Button variant="secondary" onClick={onBack}>← Terug naar parkeeracties</Button></div></Card>;
  if(visit===undefined)return <Loading label="Parkeeractie laden"/>;
  if(visit===null)return <Card><div className="visit-detail"><span>Deze parkeeractie bestaat niet meer of kon niet worden gevonden.</span><Button variant="secondary" onClick={onBack}>← Terug naar parkeeracties</Button></div></Card>;

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

      <Button className="visit-detail__back" variant="secondary" onClick={onBack}>← Terug naar parkeeracties</Button>
    </div>
  </Card>;
}
