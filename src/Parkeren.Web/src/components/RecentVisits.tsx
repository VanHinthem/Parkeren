import type { ActiveVisit,VehicleSummary } from "../api/client";import { LicensePlate } from "./LicensePlate";import { Icon } from "../design/icons/Icon";import "./RecentVisits.css";

function duration(visit:ActiveVisit){
  if(!visit.actualEndAt)return "—";
  const minutes=Math.max(0,Math.floor((new Date(visit.actualEndAt).getTime()-new Date(visit.startAt).getTime())/60000));
  if(minutes===0)return "<1 min";
  const hours=Math.floor(minutes/60);
  const rest=minutes%60;
  return hours>0?`${hours} u ${rest} min`:`${rest} min`;
}

export function RecentVisits({visits=[],vehicles=[],showAllLink=true}:{visits?:ActiveVisit[];vehicles?:VehicleSummary[];showAllLink?:boolean}){
  return <section className="recent"><header><strong>{showAllLink?"Recente parkeeracties":"Parkeerhistorie"}</strong>{showAllLink?<a href="/acties">Alles bekijken</a>:null}</header>{visits.length===0?<div className="recent__row"><span>Nog geen afgeronde parkeeracties.</span></div>:visits.map((visit)=>{const vehicle=vehicles.find(item=>item.id===visit.vehicleId);return <a className="recent__row recent__row--link" href={`/acties/${visit.id}`} key={visit.id}><span className={`recent__icon${visit.status==="Cancelled"?" muted":""}`}><Icon name="car"/></span><span className="recent__details">{vehicle?<LicensePlate value={vehicle.licensePlate}/>:<strong>Onbekend kenteken</strong>}<small>{new Date(visit.startAt).toLocaleString("nl-NL",{day:"numeric",month:"short",year:"numeric",hour:"2-digit",minute:"2-digit"})}</small></span><span className="recent__meta"><strong>{visit.status==="Cancelled"?"Geannuleerd":duration(visit)}</strong><span className="recent__chevron" aria-hidden="true">›</span></span></a>;})}</section>;
}