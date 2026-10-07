import { useEffect, useState } from "react";
import { previewVisitStart, type StartVisitPreview, type VehicleSummary } from "../api/client";
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
  ownerUserId?:string;
  embedded?:boolean;
  title?:string|null;
  subtitle?:string|null;
  buttonLabel?:string;
};

function formatMinutes(minutes:number|null){
  if(minutes===null)return null;
  const hours=Math.floor(minutes/60);
  const rest=minutes%60;
  if(hours===0)return `${rest} min`;
  if(rest===0)return `${hours} uur`;
  return `${hours} uur ${rest} min`;
}

function previewError(preview:StartVisitPreview|null){
  if(!preview||preview.isAllowed)return null;
  switch(preview.rejectionReason){
    case "OpenEndedNotAllowed":
      return "Open einde is niet toegestaan volgens jouw parkeerbeleid.";
    case "MaxVisitElapsedDurationExceeded":
      return "Deze eindtijd overschrijdt de maximale duur van je parkeerbezoek.";
    case "MaxPaidParkingDurationExceeded":
      return "Deze eindtijd overschrijdt je maximale betaalde parkeertijd.";
    case "EndNotAfterStart":
      return "De eindtijd moet na de starttijd liggen.";
    default:
      return "Deze eindtijd is niet toegestaan volgens jouw parkeerbeleid.";
  }
}

export function StartVisitCard({
  vehicles,
  onStart,
  starting=false,
  error,
  disabled=false,
  disabledMessage,
  maxDurationMinutes=240,
  allowOpenEnded=false,
  ownerUserId,
  embedded=false,
  title="Parkeren starten",
  subtitle,
  buttonLabel="Start parkeren"
}:Props){
  const[selectedVehicleId,setSelectedVehicleId]=useState(vehicles[0]?.id??"");
  const[startAt]=useState(()=>new Date());
  const[desiredEndAt,setDesiredEndAt]=useState<string|null>(()=>allowOpenEnded?null:createDefaultVisitEndAt(startAt,maxDurationMinutes));
  const[previousAllowOpenEnded,setPreviousAllowOpenEnded]=useState(allowOpenEnded);
  const[preview,setPreview]=useState<StartVisitPreview|null>(null);
  const[previewing,setPreviewing]=useState(false);
  const[previewFailure,setPreviewFailure]=useState<string|null>(null);

  useEffect(()=>{
    if(!vehicles.some(vehicle=>vehicle.id===selectedVehicleId))
      setSelectedVehicleId(vehicles[0]?.id??"");
  },[vehicles,selectedVehicleId]);

  useEffect(()=>{
    if(allowOpenEnded!==previousAllowOpenEnded){
      setPreviousAllowOpenEnded(allowOpenEnded);
      setDesiredEndAt(allowOpenEnded?null:createDefaultVisitEndAt(startAt,maxDurationMinutes));
      return;
    }
    if(desiredEndAt===null){
      if(!allowOpenEnded)setDesiredEndAt(createDefaultVisitEndAt(startAt,maxDurationMinutes));
      return;
    }
    if(!isVisitEndAtAllowed(startAt,desiredEndAt,allowOpenEnded,maxDurationMinutes))
      setDesiredEndAt(createDefaultVisitEndAt(startAt,maxDurationMinutes));
  },[allowOpenEnded,desiredEndAt,maxDurationMinutes,previousAllowOpenEnded,startAt]);

  useEffect(()=>{
    if(!selectedVehicleId||disabled){
      setPreview(null);
      setPreviewFailure(null);
      setPreviewing(false);
      return;
    }

    let cancelled=false;
    setPreviewing(true);
    setPreviewFailure(null);
    const timer=window.setTimeout(()=>{
      previewVisitStart(selectedVehicleId,desiredEndAt,ownerUserId)
        .then(result=>{
          if(cancelled)return;
          setPreview(result);
        })
        .catch(e=>{
          if(cancelled)return;
          setPreview(null);
          setPreviewFailure(e instanceof Error?e.message:"Parkeeractie kon niet worden gecontroleerd.");
        })
        .finally(()=>{
          if(!cancelled)setPreviewing(false);
        });
    },250);

    return()=>{
      cancelled=true;
      window.clearTimeout(timer);
    };
  },[desiredEndAt,disabled,ownerUserId,selectedVehicleId]);

  const vehicle=vehicles.find(item=>item.id===selectedVehicleId)??vehicles[0];
  const validEndAt=isVisitEndAtAllowed(startAt,desiredEndAt,allowOpenEnded,maxDurationMinutes);
  const policyError=previewError(preview);
  const canStart=Boolean(vehicle)&&validEndAt&&!previewing&&!previewFailure&&preview?.isAllowed===true;

  const content=<div className="start-visit">
      {title!==null?<div>
        <h2>{title}</h2>
        <p>{subtitle??(vehicle?"Kies de auto waarvoor je wilt parkeren.":"Er is geen auto aan je account toegewezen.")}</p>
      </div>:null}

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

      {!disabled&&previewing?<p className="start-visit__hint" role="status">Parkeerduur controleren…</p>:null}
      {!disabled&&!previewing&&preview?.isAllowed&&preview.paidDurationMinutes!==null
        ? <p className="start-visit__hint">
            Betaalde parkeertijd: <strong>{formatMinutes(preview.paidDurationMinutes)}</strong>
            {preview.elapsedDurationMinutes!==null
              ? <> · Totale duur: <strong>{formatMinutes(preview.elapsedDurationMinutes)}</strong></>
              : null}
          </p>
        : null}
      {policyError?<p className="start-visit__error" role="alert">{policyError}</p>:null}
      {previewFailure?<p className="start-visit__error" role="alert">{previewFailure}</p>:null}
      {disabled&&disabledMessage?<p className="start-visit__error" role="status">{disabledMessage}</p>:null}
      {error?<p className="start-visit__error" role="alert">{error}</p>:null}

      <button
        className="start-visit__button"
        disabled={starting||disabled||!canStart}
        onClick={()=>{
          if(!vehicle||!canStart)return;
          void onStart(vehicle.id,desiredEndAt);
        }}
      >
        {starting?"Starten…":buttonLabel}
      </button>
    </div>;

  return embedded?content:<Card>{content}</Card>;
}
