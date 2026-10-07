import { useEffect, useState } from "react";
import type { VehicleSummary } from "../api/client";
import { Card } from "../design/primitives/Card";
import { createDefaultVisitEndAt, isVisitEndAtAllowed, VisitEndTimeField } from "./VisitEndTimeField";
import "./StartVisitCard.css";

type Props={
  vehicles:VehicleSummary[];
  onStart:(vehicleId:string,desiredEndAt:string|null)=>Promise<void>;
  starting?:boolean;
  error?:string|null;
  disabled?:boolean;
  disabledMessage?:string;
  maxDurationMinutes?:number|null;
  allowOpenEnded?:boolean;
};

export function StartVisitCard({
  vehicles,
  onStart,
  starting=false,
  error,
  disabled=false,
  disabledMessage,
  maxDurationMinutes=240,
  allowOpenEnded=false
}:Props){
  const[selectedVehicleId,setSelectedVehicleId]=useState(vehicles[0]?.id??"");
  const[startAt]=useState(()=>new Date());
  const[desiredEndAt,setDesiredEndAt]=useState<string|null>(()=>createDefaultVisitEndAt(startAt,maxDurationMinutes));

  useEffect(()=>{
    if(!vehicles.some(vehicle=>vehicle.id===selectedVehicleId))
      setSelectedVehicleId(vehicles[0]?.id??"");
  },[vehicles,selectedVehicleId]);

  useEffect(()=>{
    if(desiredEndAt===null){
      if(!allowOpenEnded)setDesiredEndAt(createDefaultVisitEndAt(startAt,maxDurationMinutes));
      return;
    }
    if(!isVisitEndAtAllowed(startAt,desiredEndAt,allowOpenEnded,maxDurationMinutes))
      setDesiredEndAt(createDefaultVisitEndAt(startAt,maxDurationMinutes));
  },[allowOpenEnded,desiredEndAt,maxDurationMinutes,startAt]);

  const vehicle=vehicles.find(item=>item.id===selectedVehicleId)??vehicles[0];
  const validEndAt=isVisitEndAtAllowed(startAt,desiredEndAt,allowOpenEnded,maxDurationMinutes);

  return <Card>
    <div className="start-visit">
      <div>
        <h2>Parkeren starten</h2>
        <p>{vehicle?"Kies de auto waarvoor je wilt parkeren.":"Er is geen auto aan je account toegewezen."}</p>
      </div>

      {vehicles.length>1
        ? <label className="start-visit__field">
            <span>Auto</span>
            <select value={vehicle?.id??""} onChange={event=>setSelectedVehicleId(event.target.value)} disabled={starting||disabled}>
              {vehicles.map(item=><option key={item.id} value={item.id}>{item.licensePlate}</option>)}
            </select>
          </label>
        : vehicle?<strong className="start-visit__vehicle">{vehicle.licensePlate}</strong>:null}

      <VisitEndTimeField
        startAt={startAt}
        value={desiredEndAt}
        onChange={setDesiredEndAt}
        allowOpenEnded={allowOpenEnded}
        maxDurationMinutes={maxDurationMinutes}
        disabled={starting||disabled}
      />

      {disabled&&disabledMessage?<p className="start-visit__error" role="status">{disabledMessage}</p>:null}
      {error?<p className="start-visit__error" role="alert">{error}</p>:null}

      <button
        className="start-visit__button"
        disabled={!vehicle||starting||disabled||!validEndAt}
        onClick={()=>{
          if(!vehicle||!validEndAt)return;
          void onStart(vehicle.id,desiredEndAt);
        }}
      >
        {starting?"Starten…":"Start parkeren"}
      </button>
    </div>
  </Card>;
}
