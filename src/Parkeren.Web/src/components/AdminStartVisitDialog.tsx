import { useEffect,useState } from "react";
import {
  getAdminUserParkingPolicy,
  getAssignedVehicles,
  getUsers,
  startVisit,
  type AdminActiveVisitSummary,
  type AdminParkingPolicySummary,
  type UserSummary,
  type VehicleSummary
} from "../api/client";
import { Dialog } from "../design/primitives/Dialog";
import { clearPendingOperation,getOrCreatePendingOperation } from "../pendingOperations";
import { StartVisitCard } from "./StartVisitCard";

type Props={
  open:boolean;
  onClose:()=>void;
  activeVisits:AdminActiveVisitSummary[];
  onStarted:()=>Promise<void>|void;
};

export function AdminStartVisitDialog({open,onClose,activeVisits,onStarted}:Props){
  const[users,setUsers]=useState<UserSummary[]>([]);
  const[usersLoading,setUsersLoading]=useState(false);
  const[usersError,setUsersError]=useState<string|null>(null);
  const[selectedUser,setSelectedUser]=useState<UserSummary|null>(null);
  const[vehicles,setVehicles]=useState<VehicleSummary[]>();
  const[policy,setPolicy]=useState<AdminParkingPolicySummary|null|undefined>();
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
    setVehicles(undefined);
    setPolicy(undefined);
    setStartError(null);

    Promise.all([
      getAssignedVehicles(selectedUser.id),
      getAdminUserParkingPolicy(selectedUser.id)
    ]).then(([assigned,parkingPolicy])=>{
      if(cancelled)return;
      setVehicles(assigned.filter(vehicle=>vehicle.isActive));
      setPolicy(parkingPolicy);
    }).catch(error=>{
      if(cancelled)return;
      setVehicles([]);
      setPolicy(null);
      setStartError(error instanceof Error?error.message:"Startgegevens voor deze bezoeker konden niet worden geladen.");
    });

    return()=>{cancelled=true;};
  },[selectedUser]);

  const activeCountFor=(userId:string)=>activeVisits.filter(visit=>visit.userId===userId).length;
  const selectedUserLimitReached=selectedUser!==null&&
    selectedUser.maxConcurrentVisits!==null&&
    activeCountFor(selectedUser.id)>=selectedUser.maxConcurrentVisits;

  async function handleStart(vehicleId:string,desiredEndAt:string|null){
    if(!selectedUser||starting||selectedUserLimitReached)return;
    setStarting(true);
    setStartError(null);

    const logicalKey=`${selectedUser.id}:${vehicleId}:${desiredEndAt??"open"}`;
    const operationId=getOrCreatePendingOperation("start",logicalKey);

    try{
      const result=await startVisit(vehicleId,desiredEndAt,operationId,selectedUser.id);
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
                  : <StartVisitCard
                      vehicles={vehicles}
                      starting={starting}
                      error={startError}
                      disabled={selectedUserLimitReached}
                      disabledMessage={selectedUserLimitReached?"Deze bezoeker heeft het maximum aantal lopende parkeeracties bereikt.":undefined}
                      maxDurationMinutes={policy.maxVisitElapsedDurationMinutes}
                      allowOpenEnded={policy.allowOpenEndedVisits}
                      ownerUserId={selectedUser.id}
                      embedded
                      title={null}
                      buttonLabel="Parkeren starten"
                      onStart={handleStart}
                    />}
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
                      onClick={()=>setSelectedUser(visitor)}
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
