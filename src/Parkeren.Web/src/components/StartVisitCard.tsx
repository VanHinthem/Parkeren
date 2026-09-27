import type { VehicleSummary } from "../api/client";
import { Card } from "../design/primitives/Card";
import "./StartVisitCard.css";

type Props={vehicles:VehicleSummary[];onStart:(vehicleId:string)=>void;starting?:boolean};

export function StartVisitCard({vehicles,onStart,starting=false}:Props){
  const vehicle=vehicles[0];
  return <Card><div className="start-visit"><div><h2>Parkeren starten</h2><p>{vehicle?"Start een parkeeractie voor "+vehicle.licensePlate+".":"Er is geen auto aan je account toegewezen."}</p></div><button className="start-visit__button" disabled={!vehicle||starting} onClick={()=>vehicle&&onStart(vehicle.id)}>{starting?"Starten…":"Start parkeren"}</button></div></Card>;
}
