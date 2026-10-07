import { useEffect,useState } from "react";
import {
  getAdminUserParkingPolicy,
  getAssignedVehicles,
  getUsers,
  previewVisitStart,
  startVisit,
  type AdminActiveVisitSummary,
  type AdminParkingPolicySummary,
  type StartVisitPreview,
  type UserSummary,
  type VehicleSummary
} from "../api/client";
import {
  createDefaultVisitEndAt,
  isVisitEndAtAllowed,
  VisitEndTimeField
} from "./VisitEndTimeField";
import { Dialog } from "../design/primitives/Dialog";
import { Button } from "../design/primitives/Button";
import { clearPendingOperation,getOrCreatePendingOperation } from "../pendingOperations";

type Props={
  open:boolean;
  onClose:()=>void;
  activeVisits:AdminActiveVisitSummary[];
  onStarted:()=>Promise<void>|void;
};

function formatPreviewMinutes(minutes:number|null){
  if(minutes===null)return null;
  const hours=Math.floor(minutes/60);
  const rest=minutes%60;
  if(hours===0)return `${rest} min`;
  if(rest===0)return `${hours} uur`;
  return `${hours} uur ${rest} min`;
}

function previewValidationMessage(preview:StartVisitPreview|null){
  if(!preview||preview.isAllowed)return undefined;
  switch(preview.rejectionReason){
    case "OpenEndedNotAllowed": return "Open einde is niet toegestaan volgens het parkeerbeleid van deze bezoeker.";
    case "MaxVisitElapsedDurationExceeded": return "Deze eindtijd overschrijdt de maximale duur van het parkeerbezoek.";
    case "MaxPaidParkingDurationExceeded": return "Deze eindtijd overschrijdt de maximale betaalde parkeertijd van deze bezoeker.";
    case "EndNotAfterStart": return "De eindtijd moet na de starttijd liggen.";
    default: return "Deze eindtijd is niet toegestaan volgens het parkeerbeleid van deze bezoeker.";
  }
}

export function AdminStartVisitDialog({open,onClose,activeVisits,onStarted}:Props){
  const[users,setUsers]=useState<UserSummary[]>([]);
  const[usersLoading,setUsersLoading]=useState(false);
  const[usersError,setUsersError]=useState<string|null>(null);
  const[selectedUser,setSelectedUser]=useState<UserSummary|null>(null);
  const[vehicles,setVehicles]=useState<VehicleSummary[]>();
  const[selectedVehicleId,setSelectedVehicleId]=useState("");
  const[policy,setPolicy]=useState<AdminParkingPolicySummary|null|undefined>();
  const[startAt,setStartAt]=useState(()=>new Date());
  const[desiredEndAt,setDesiredEndAt]=useState<string|null>(null);
  const[preview,setPreview]=useState<StartVisitPreview|null>(null);
  const[previewing,setPreviewing]=useState(false);
  const[previewError,setPreviewError]=useState<string|null>(null);
  const[starting,setStarting]=useState(false);
  const[startError,setStartError]=useState<string|null>(null);

  useEffect(()=>{
    if(!open)return;
    let cancelled=false;
    setUsersLoading(true);
    setUsersError(null);
    setSelectedUser(null);
    setVehicles(undefined);
    setPolicy(undefined);
    setSelectedVehicleId("");
    setPreview(null);
    setStarting(false);
    setStartError(null);
    getUsers()
      .then(result=>{
        if(!cancelled)setUsers(result.filter(user=>user.role==="Visitor"&&user.isActive));
      })
      .catch(error=>{
        if(cancelled)return;
        setUsers([]);
        setUsersError(error instanceof Error?error.message:"Bezoekers konden niet worden geladen.");
      })
      .finally(()=>{if(!cancelled)setUsersLoading(false);});
    return()=>{cancelled=true;};
  },[open]);

  useEffect(()=>{
    if(!selectedUser)return;
    let cancelled=false;
    const nextStartAt=new Date();
    setStartAt(nextStartAt);
    setVehicles(undefined);
    setPolicy(undefined);
    setSelectedVehicleId("");
    setPreview(null);
    setPreviewError(null);
    setStartError(null);

    Promise.all([
      getAssignedVehicles(selectedUser.id),
      getAdminUserParkingPolicy(selectedUser.id)
    ]).then(([assigned,parkingPolicy])=>{
      if(cancelled)return;
      const activeVehicles=assigned.filter(vehicle=>vehicle.isActive);
      setVehicles(activeVehicles);
      setPolicy(parkingPolicy);
      setSelectedVehicleId(activeVehicles[0]?.id??"");
      if(parkingPolicy){
        setDesiredEndAt(
          parkingPolicy.allowOpenEndedVisits
            ? null
            : createDefaultVisitEndAt(nextStartAt,parkingPolicy.maxVisitElapsedDurationMinutes)
        );
      }
    }).catch(error=>{
      if(cancelled)return;
      setVehicles([]);
      setPolicy(null);
      setPreviewError(error instanceof Error?error.message:"Startgegevens voor deze bezoeker konden niet worden geladen.");
    });

    return()=>{cancelled=true;};
  },[selectedUser]);

  useEffect(()=>{
    if(!selectedUser||!selectedVehicleId||!policy){
      setPreview(null);
      setPreviewing(false);
      return;
    }
    if(!isVisitEndAtAllowed(
      startAt,
      desiredEndAt,
      policy.allowOpenEndedVisits,
      policy.maxVisitElapsedDurationMinutes
    )){
      setPreview(null);
      return;
    }

    let cancelled=false;
    setPreviewing(true);
    setPreviewError(null);
    const timer=window.setTimeout(()=>{
      previewVisitStart(selectedVehicleId,desiredEndAt,selectedUser.id)
        .then(result=>{if(!cancelled)setPreview(result);})
        .catch(error=>{
          if(cancelled)return;
          setPreview(null);
          setPreviewError(error instanceof Error?error.message:"Parkeeractie kon niet worden gecontroleerd.");
        })
        .finally(()=>{if(!cancelled)setPreviewing(false);});
    },250);

    return()=>{
      cancelled=true;
      window.clearTimeout(timer);
    };
  },[desiredEndAt,policy,selectedUser,selectedVehicleId,startAt]);

  function selectUser(visitor:UserSummary){
    setSelectedUser(visitor);
  }

  const activeCountFor=(userId:string)=>activeVisits.filter(visit=>visit.userId===userId).length;

  const selectedUserLimitReached=selectedUser!==null&&
    selectedUser.maxConcurrentVisits!==null&&
    activeCountFor(selectedUser.id)>=selectedUser.maxConcurrentVisits;

  const startDisabled=
    starting||
    selectedUser===null||
    selectedVehicleId===""||
    !policy||
    selectedUserLimitReached||
    !isVisitEndAtAllowed(
      startAt,
      desiredEndAt,
      policy?.allowOpenEndedVisits===true,
      policy?.maxVisitElapsedDurationMinutes??null
    )||
    previewing||
    previewError!==null||
    preview?.isAllowed!==true;

  async function handleStart(){
    if(startDisabled||!selectedUser||!selectedVehicleId)return;
    setStarting(true);
    setStartError(null);

    const logicalKey=`${selectedUser.id}:${selectedVehicleId}:${desiredEndAt??"open"}`;
    const operationId=getOrCreatePendingOperation("start",logicalKey);

    try{
      const result=await startVisit(
        selectedVehicleId,
        desiredEndAt,
        operationId,
        selectedUser.id
      );
      if(!result.reconciliationRequired)clearPendingOperation("start",logicalKey);
      await onStarted();
      onClose();
    }catch(error){
      setStartError(error instanceof Error?error.message:"Parkeeractie kon niet worden gestart.");
    }finally{
      setStarting(false);
    }
  }

  return <Dialog open={open} title="Parkeren starten voor bezoeker" onClose={()=>{if(!starting)onClose();}}>
    <div className="actions-page__visitor-dialog">
      {selectedUser
        ? <>
            <button type="button" className="actions-page__visitor-back" onClick={()=>setSelectedUser(null)} disabled={starting}>← Andere bezoeker kiezen</button>
            <div className="actions-page__visitor-selection">
              <strong>{selectedUser.username}</strong>
              <small>{activeCountFor(selectedUser.id)===0?"Geen lopende actie":`${activeCountFor(selectedUser.id)} lopende actie(s)`}</small>
            </div>

            {vehicles===undefined||policy===undefined
              ? <p role="status">Startgegevens laden…</p>
              : policy===null
                ? <p role="alert">Het parkeerbeleid voor deze bezoeker is niet beschikbaar.</p>
                : vehicles.length===0
                  ? <p role="alert">Deze bezoeker heeft geen actief toegewezen voertuig.</p>
                  : <>
                      <label className="actions-page__visitor-field">
                        <span>Voertuig</span>
                        <select value={selectedVehicleId} onChange={event=>setSelectedVehicleId(event.target.value)} disabled={starting}>
                          {vehicles.map(vehicle=><option key={vehicle.id} value={vehicle.id}>{vehicle.licensePlate}</option>)}
                        </select>
                      </label>

                      <VisitEndTimeField
                        startAt={startAt}
                        value={desiredEndAt}
                        onChange={setDesiredEndAt}
                        allowOpenEnded={policy.allowOpenEndedVisits}
                        maxDurationMinutes={policy.maxVisitElapsedDurationMinutes}
                        disabled={starting}
                      />

                      {previewing?<p className="actions-page__visitor-hint">Parkeerduur controleren…</p>:null}
                      {!previewing&&preview?.isAllowed&&preview.paidDurationMinutes!==null
                        ? <p className="actions-page__visitor-hint">
                            Betaalde parkeertijd: <strong>{formatPreviewMinutes(preview.paidDurationMinutes)}</strong>
                            {preview.elapsedDurationMinutes!==null
                              ? <> · Totale duur: <strong>{formatPreviewMinutes(preview.elapsedDurationMinutes)}</strong></>
                              : null}
                          </p>
                        : null}
                      {previewValidationMessage(preview)
                        ? <p className="actions-page__visitor-hint actions-page__visitor-hint--error" role="alert">{previewValidationMessage(preview)}</p>
                        : null}
                      {previewError
                        ? <p className="actions-page__visitor-hint actions-page__visitor-hint--error" role="alert">{previewError}</p>
                        : null}
                      {selectedUserLimitReached
                        ? <p className="actions-page__visitor-hint actions-page__visitor-hint--error" role="alert">Deze bezoeker heeft het maximum aantal lopende parkeeracties bereikt.</p>
                        : null}
                      {startError
                        ? <p className="actions-page__visitor-hint actions-page__visitor-hint--error" role="alert">{startError}</p>
                        : null}
                      <Button onClick={()=>void handleStart()} disabled={startDisabled}>
                        {starting?"Starten…":"Parkeren starten"}
                      </Button>
                    </>}
          </>
        : usersLoading
          ? <p role="status">Bezoekers laden…</p>
          : usersError
            ? <p role="alert">{usersError}</p>
            : users.length===0
              ? <p>Er zijn geen actieve bezoekers beschikbaar.</p>
              : <div className="actions-page__visitor-list">
                  {users.map(visitor=>{
                    const activeCount=activeCountFor(visitor.id);
                    const max=visitor.maxConcurrentVisits;
                    const limitReached=max!==null&&activeCount>=max;
                    return <button
                      type="button"
                      className="actions-page__visitor-row"
                      key={visitor.id}
                      disabled={limitReached}
                      onClick={()=>selectUser(visitor)}
                    >
                      <span>
                        <strong>{visitor.username}</strong>
                        <small>{activeCount===0?"Geen lopende actie":activeCount===1?"1 lopende actie":`${activeCount} lopende acties`}</small>
                      </span>
                      <span className={limitReached?"actions-page__visitor-status actions-page__visitor-status--blocked":"actions-page__visitor-status"}>
                        {limitReached?"Maximum bereikt":"Beschikbaar"}
                      </span>
                    </button>;
                  })}
                </div>}
    </div>
  </Dialog>;
}
