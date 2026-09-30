import { useEffect,useState } from "react";
import { getVisit,type ActiveVisit,type VehicleSummary } from "../../api/client";
import { Card } from "../../design/primitives/Card";
import { Loading } from "../../design/primitives/Loading";

type Props={visitId:string;vehicles:VehicleSummary[]};

function format(value:string|null){return value?new Date(value).toLocaleString("nl-NL",{dateStyle:"medium",timeStyle:"short"}):"—";}

export function VisitDetailPage({visitId,vehicles}:Props){
  const[visit,setVisit]=useState<ActiveVisit|null|undefined>();
  const[error,setError]=useState<string|null>(null);

  useEffect(()=>{
    setVisit(undefined);setError(null);
    getVisit(visitId).then(setVisit).catch(e=>setError(e instanceof Error?e.message:"Parkeeractie kon niet worden geladen."));
  },[visitId]);

  if(error)return <Card><p role="alert" style={{margin:0}}>{error}</p></Card>;
  if(visit===undefined)return <Loading label="Parkeeractie laden"/>;
  if(visit===null)return <Card>Deze parkeeractie bestaat niet meer of kon niet worden gevonden.</Card>;

  const vehicle=vehicles.find(x=>x.id===visit.vehicleId);
  return <Card>
    <div style={{display:"grid",gap:".65rem"}}>
      <strong>{vehicle?.licensePlate??"Parkeeractie"}</strong>
      <span>Status: {visit.status}</span>
      <span>Gestart: {format(visit.startAt)}</span>
      <span>Gewenste eindtijd: {format(visit.desiredEndAt)}</span>
      {visit.actualEndAt&&<span>Gestopt: {format(visit.actualEndAt)}</span>}
    </div>
  </Card>;
}
