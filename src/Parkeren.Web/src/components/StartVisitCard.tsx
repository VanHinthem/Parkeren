import { useState } from "react";
import type { VehicleSummary } from "../api/client";
import { Card } from "../design/primitives/Card";
import "./StartVisitCard.css";

type Props={vehicles:VehicleSummary[];onStart:(vehicleId:string)=>void;starting?:boolean};

export function StartVisitCard({vehicles,onStart,starting=false}:Props){
  const[selectedVehicleId,setSelectedVehicleId]=useState(vehicles[0]?.id??"");
  const vehicle=vehicles.find(item=>item.id===selectedVehicleId)??vehicles[0];
  return <Card><div className="start-visit"><div><h2>Parkeren starten</h2><p>{vehicle?"Kies de auto waarvoor je wilt parkeren.":"Er is geen auto aan je account toegewezen."}</p></div>{vehicles.length>1?<label className="start-visit__field"><span>Auto</span><select value={vehicle?.id??""} onChange={event=>setSelectedVehicleId(event.target.value)} disabled={starting}>{vehicles.map(item=><option key={item.id} value={item.id}>{item.licensePlate}</option>)}</select></label>:vehicle?<strong className="start-visit__vehicle">{vehicle.licensePlate}</strong>:null}<button className="start-visit__button" disabled={!vehicle||starting} onClick={()=>vehicle&&onStart(vehicle.id)}>{starting?"Starten…":"Start parkeren"}</button></div></Card>;
}
