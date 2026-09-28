import { useEffect, useState } from "react";
import type { VehicleSummary } from "../api/client";
import { Card } from "../design/primitives/Card";
import "./StartVisitCard.css";

type Props={vehicles:VehicleSummary[];onStart:(vehicleId:string,desiredEndAt:string|null)=>Promise<void>;starting?:boolean;error?:string|null;disabled?:boolean;disabledMessage?:string;maxDurationMinutes?:number};

export function StartVisitCard({vehicles,onStart,starting=false,error,disabled=false,disabledMessage,maxDurationMinutes=240}:Props){
  const[selectedVehicleId,setSelectedVehicleId]=useState(vehicles[0]?.id??"");
  const[durationHours,setDurationHours]=useState("4");
  const vehicle=vehicles.find(item=>item.id===selectedVehicleId)??vehicles[0];const maxDurationHours=Math.max(1,Math.floor(maxDurationMinutes/60));const durationOptions=Array.from({length:maxDurationHours},(_,index)=>index+1);useEffect(()=>{if(Number(durationHours)>maxDurationHours)setDurationHours(String(maxDurationHours));},[durationHours,maxDurationHours]);
  return <Card><div className="start-visit"><div><h2>Parkeren starten</h2><p>{vehicle?"Kies de auto waarvoor je wilt parkeren.":"Er is geen auto aan je account toegewezen."}</p></div>{vehicles.length>1?<label className="start-visit__field"><span>Auto</span><select value={vehicle?.id??""} onChange={event=>setSelectedVehicleId(event.target.value)} disabled={starting||disabled}>{vehicles.map(item=><option key={item.id} value={item.id}>{item.licensePlate}</option>)}</select></label>:vehicle?<strong className="start-visit__vehicle">{vehicle.licensePlate}</strong>:null}<label className="start-visit__field"><span>Parkeerduur</span><select value={durationHours} onChange={event=>setDurationHours(event.target.value)} disabled={starting||disabled}>{durationOptions.map(hours=><option key={hours} value={hours}>{hours} uur</option>)}</select></label>{disabled&&disabledMessage?<p className="start-visit__error" role="status">{disabledMessage}</p>:null}{error?<p className="start-visit__error" role="alert">{error}</p>:null}<button className="start-visit__button" disabled={!vehicle||starting||disabled} onClick={()=>{const hours=Number(durationHours);if(!vehicle||!Number.isInteger(hours)||hours<1||hours>maxDurationHours)return;void onStart(vehicle.id,new Date(Date.now()+hours*60*60*1000).toISOString());}}>{starting?"Starten…":"Start parkeren"}</button></div></Card>;
}
