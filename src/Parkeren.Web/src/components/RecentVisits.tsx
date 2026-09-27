import type { ActiveVisit,VehicleSummary } from "../api/client";import { Icon } from "../design/icons/Icon";import "./RecentVisits.css";

function duration(visit:ActiveVisit){
  if(!visit.actualEndAt)return "—";
  const minutes=Math.max(0,Math.floor((new Date(visit.actualEndAt).getTime()-new Date(visit.startAt).getTime())/60000));
  const hours=Math.floor(minutes/60);
  const rest=minutes%60;
  return hours>0?`${hours}u ${rest}m`:`${rest}m`;
}

export function RecentVisits({visits,vehicles}:{visits:ActiveVisit[];vehicles:VehicleSummary[]}){
  return <section className="recent"><header><strong>Recente parkeeracties</strong><a href="/acties">Alles bekijken</a></header>{visits.length===0?<div className="recent__row"><span>Nog geen afgeronde parkeeracties.</span></div>:visits.map((visit,index)=>{const vehicle=vehicles.find(item=>item.id===visit.vehicleId);return <div className="recent__row" key={visit.id}><span className={`recent__icon${index>0?" muted":""}`}><Icon name="car"/></span><span><strong>{vehicle?.licensePlate??"Onbekend kenteken"}</strong><small>{new Date(visit.startAt).toLocaleString("nl-NL",{day:"numeric",month:"short",year:"numeric",hour:"2-digit",minute:"2-digit"})}</small></span><small>{duration(visit)}</small></div>;})}</section>;
}