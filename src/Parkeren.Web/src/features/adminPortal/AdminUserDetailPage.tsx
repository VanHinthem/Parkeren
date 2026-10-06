import { useEffect,useMemo,useState } from "react";
import {
  assignVehicle,
  getAdminUserDetail,
  getVehicles,
  resetUserPin,
  revokeUserSessions,
  setAdminUserPolicy,
  setUserActive,
  unassignVehicle,
  type AdminUserDetail,
  type AdminUserPolicyUpdate,
  type PolicyDurationOverrideMode,
  type VehicleSummary
} from "../../api/client";
import { LicensePlate } from "../../components/LicensePlate";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import { formatAdminDuration,formatAdminNumber,formatAdminRecordStatus } from "./adminFieldFormatters";
import "./AdminUsers.css";

type BooleanOverrideMode="default"|"true"|"false";

function booleanText(value:boolean){return value?"Ja":"Nee";}
function hoursValue(minutes:number|null,fallback:number){return String((minutes??fallback)/60);}
function hoursToMinutes(value:string){
  const hours=Number(value);
  if(!Number.isFinite(hours)||hours<=0)return null;
  return Math.round(hours*60);
}
function modeToBoolean(value:BooleanOverrideMode){
  if(value==="default")return null;
  return value==="true";
}

export function AdminUserDetailPage({userId}:{userId:string}){
  const[detail,setDetail]=useState<AdminUserDetail|null|undefined>();
  const[allVehicles,setAllVehicles]=useState<VehicleSummary[]>([]);
  const[selectedVehicleId,setSelectedVehicleId]=useState("");
  const[adminPin,setAdminPin]=useState("");
  const[error,setError]=useState<string>();
  const[message,setMessage]=useState<string>();
  const[saving,setSaving]=useState(false);

  const[paidMode,setPaidMode]=useState<PolicyDurationOverrideMode>("Inherit");
  const[paidHours,setPaidHours]=useState("4");
  const[elapsedMode,setElapsedMode]=useState<PolicyDurationOverrideMode>("Inherit");
  const[elapsedHours,setElapsedHours]=useState("8");
  const[extensionMode,setExtensionMode]=useState<BooleanOverrideMode>("default");
  const[openEndedMode,setOpenEndedMode]=useState<BooleanOverrideMode>("default");
  const[concurrencyOverride,setConcurrencyOverride]=useState(false);
  const[concurrency,setConcurrency]=useState("1");

  function applyPolicyForm(value:AdminUserDetail){
    const overrides=value.policy.overrides;
    const effective=value.policy.effective;
    setPaidMode(overrides.maxPaidParkingDurationMode);
    setPaidHours(hoursValue(overrides.maxPaidParkingDurationMinutes,effective.maxPaidParkingDurationMinutes??240));
    setElapsedMode(overrides.maxVisitElapsedDurationMode);
    setElapsedHours(hoursValue(overrides.maxVisitElapsedDurationMinutes,effective.maxVisitElapsedDurationMinutes??480));
    setExtensionMode(overrides.allowVisitExtension===null?"default":overrides.allowVisitExtension?"true":"false");
    setOpenEndedMode(overrides.allowOpenEndedVisits===null?"default":overrides.allowOpenEndedVisits?"true":"false");
    setConcurrencyOverride(overrides.maxConcurrentVisits!==null);
    setConcurrency(String(overrides.maxConcurrentVisits??effective.maxConcurrentVisits));
  }

  async function load(){
    setError(undefined);
    try{
      const[value,vehicles]=await Promise.all([getAdminUserDetail(userId),getVehicles()]);
      setDetail(value);
      setAllVehicles(vehicles);
      if(value){
        applyPolicyForm(value);
        const assignedIds=new Set(value.assignedVehicles.map(vehicle=>vehicle.id));
        const first=vehicles.find(vehicle=>vehicle.isActive&&!assignedIds.has(vehicle.id));
        setSelectedVehicleId(first?.id??"");
      }
    }catch(e){
      setError(e instanceof Error?e.message:"Gebruikersdetail kon niet worden geladen.");
      setDetail(null);
    }
  }

  useEffect(()=>{void load();},[userId]);

  const availableVehicles=useMemo(()=>{
    if(!detail)return [];
    const assignedIds=new Set(detail.assignedVehicles.map(vehicle=>vehicle.id));
    return allVehicles.filter(vehicle=>vehicle.isActive&&!assignedIds.has(vehicle.id));
  },[allVehicles,detail]);

  useEffect(()=>{
    if(!availableVehicles.some(vehicle=>vehicle.id===selectedVehicleId))setSelectedVehicleId(availableVehicles[0]?.id??"");
  },[availableVehicles,selectedVehicleId]);

  async function savePolicy(){
    if(!detail)return;
    const paidMinutes=paidMode==="Value"?hoursToMinutes(paidHours):null;
    const elapsedMinutes=elapsedMode==="Value"?hoursToMinutes(elapsedHours):null;
    const concurrent=concurrencyOverride?Number(concurrency):null;
    if((paidMode==="Value"&&paidMinutes===null)||(elapsedMode==="Value"&&elapsedMinutes===null)||(concurrencyOverride&&(concurrent===null||!Number.isInteger(concurrent)||concurrent<=0))){
      setError("Controleer de afwijkende policywaarden.");
      return;
    }
    const policy:AdminUserPolicyUpdate={
      maxPaidParkingDurationMode:paidMode,
      maxPaidParkingDurationMinutes:paidMinutes,
      maxVisitElapsedDurationMode:elapsedMode,
      maxVisitElapsedDurationMinutes:elapsedMinutes,
      allowVisitExtension:modeToBoolean(extensionMode),
      allowOpenEndedVisits:modeToBoolean(openEndedMode),
      maxConcurrentVisits:concurrent
    };
    setSaving(true);
    setError(undefined);
    setMessage(undefined);
    try{
      await setAdminUserPolicy(userId,policy);
      setMessage("Gebruikerspolicy is opgeslagen.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Gebruikerspolicy kon niet worden opgeslagen.");
    }finally{
      setSaving(false);
    }
  }

  async function assign(){
    if(!selectedVehicleId)return;
    setError(undefined);
    setMessage(undefined);
    try{
      await assignVehicle(userId,selectedVehicleId);
      setMessage("Voertuig is toegewezen.");
      await load();
    }catch(e){setError(e instanceof Error?e.message:"Voertuig kon niet worden toegewezen.");}
  }

  async function unassign(vehicleId:string){
    setError(undefined);
    setMessage(undefined);
    try{
      await unassignVehicle(userId,vehicleId);
      setMessage("Toewijzing is verwijderd.");
      await load();
    }catch(e){setError(e instanceof Error?e.message:"Toewijzing kon niet worden verwijderd.");}
  }

  async function toggleActive(){
    if(!detail||detail.user.status==="Archived")return;
    setError(undefined);
    setMessage(undefined);
    try{
      await setUserActive(userId,!detail.user.isActive);
      setMessage(detail.user.isActive?"Gebruiker is gedeactiveerd.":"Gebruiker is geactiveerd.");
      await load();
    }catch(e){setError(e instanceof Error?e.message:"Gebruiker kon niet worden gewijzigd.");}
  }

  if(detail===undefined)return <Loading label="Gebruikersdetail laden"/>;

  return <div className="admin-user-detail">
    <div className="admin-user-detail__toolbar">
      <a className="admin-action-link admin-action-link--muted" href="/beheer/gebruikers">← Terug naar gebruikers</a>
      {detail&&detail.user.status!=="Archived"&&<Button className="admin-action--compact" variant="secondary" onClick={()=>void toggleActive()}>{detail.user.isActive?"Deactiveren":"Activeren"}</Button>}
    </div>

    {error&&<Alert tone="danger">{error}</Alert>}
    {message&&<Alert>{message}</Alert>}

    {!detail
      ? <section className="admin-users__panel"><p className="admin-users__empty">Deze gebruiker kon niet worden gevonden.</p></section>
      : <>
          <section className="admin-users__panel">
            <div className="admin-user-detail__summary">
              <div className="admin-user-detail__summary-item"><span>Gebruiker</span><strong>{detail.user.username}</strong></div>
              <div className="admin-user-detail__summary-item"><span>Rol</span><strong>{detail.user.role==="Admin"?"Beheerder":"Bezoeker"}</strong></div>
              <div className="admin-user-detail__summary-item"><span>Status</span><strong className="admin-user-detail__summary-status"><span className={`admin-status ${detail.user.isActive&&detail.user.status!=="Archived"?"admin-status--active":""}`}>{formatAdminRecordStatus(detail.user.status)}</span></strong></div>
              <div className="admin-user-detail__summary-item"><span>Actieve Visits</span><strong className="admin-number">{formatAdminNumber(detail.activeVisitCount)}</strong></div>
            </div>
          </section>

          <div className="admin-user-detail__columns">
            <section className="admin-users__panel admin-user-detail__column-panel">
              <h2>Voertuigen</h2>
              <div className="admin-user-detail__assign">
                <label className="admin-field">
                  <span>Actief voertuig toevoegen</span>
                  <select value={selectedVehicleId} onChange={event=>setSelectedVehicleId(event.target.value)} disabled={availableVehicles.length===0}>
                    {availableVehicles.length===0?<option value="">Geen voertuig beschikbaar</option>:availableVehicles.map(vehicle=><option value={vehicle.id} key={vehicle.id}>{vehicle.licensePlate}</option>)}
                  </select>
                </label>
                <Button className="admin-action--field" onClick={()=>void assign()} disabled={!selectedVehicleId}>Toewijzen</Button>
              </div>
              <div className="admin-user-detail__list">
                {detail.assignedVehicles.length===0
                  ? <p className="admin-users__empty">Geen voertuigen toegewezen.</p>
                  : detail.assignedVehicles.map(vehicle=><div className="admin-user-detail__row" key={vehicle.id}>
                      <span className="admin-user-detail__row-copy">
                        <span className="admin-user-detail__vehicle-main">
                          <LicensePlate value={vehicle.licensePlate}/>
                          <span className={`admin-status ${vehicle.isActive?"admin-status--active":""}`}>{formatAdminRecordStatus(vehicle.status)}</span>
                        </span>
                        {vehicle.displayName&&<small>{vehicle.displayName}</small>}
                      </span>
                      <Button className="admin-action--compact admin-user-detail__unlink" variant="secondary" onClick={()=>void unassign(vehicle.id)}>Ontkoppelen</Button>
                    </div>)}
              </div>
            </section>

            <section className="admin-users__panel admin-user-detail__column-panel">
              <h2>Accountbeheer</h2>
              <label className="admin-field">
                <span>Nieuwe tijdelijke PIN</span>
                <input type="password" inputMode="numeric" maxLength={6} value={adminPin} onChange={event=>setAdminPin(event.target.value.replace(/\D/g,"").slice(0,6))}/>
              </label>
              <div className="admin-user-detail__account-actions">
                <Button className="admin-action--compact" variant="secondary" disabled={adminPin.length!==6} onClick={async()=>{
                  try{
                    await resetUserPin(userId,adminPin);
                    setAdminPin("");
                    setError(undefined);
                    setMessage("PIN is gereset; bestaande sessies zijn ingetrokken.");
                  }catch(e){setError(e instanceof Error?e.message:"PIN resetten mislukt.");}
                }}>PIN resetten</Button>
                <Button className="admin-action--compact" variant="secondary" onClick={async()=>{
                  try{
                    await revokeUserSessions(userId);
                    setError(undefined);
                    setMessage("Alle sessies zijn ingetrokken.");
                  }catch(e){setError(e instanceof Error?e.message:"Sessies intrekken mislukt.");}
                }}>Sessies intrekken</Button>
              </div>
            </section>
          </div>

          <section className="admin-users__panel">
            <h2>Gebruikerspolicy</h2>
            {detail.activeVisitCount>0&&<Alert tone="warning">Deze gebruiker heeft {formatAdminNumber(detail.activeVisitCount)} actieve Visit(s). Een wijziging die de effectieve policy verandert wordt server-side geblokkeerd.</Alert>}
            <div className="admin-user-detail__policy">
              <div className="admin-user-detail__policy-row">
                <div className="admin-user-detail__policy-copy"><strong>Max. betaalde parkeertijd</strong><small>Standaard: {formatAdminDuration(detail.policy.defaults.maxPaidParkingDurationMinutes,"Onbeperkt")}</small></div>
                <label className="admin-field"><span>Waarde</span><select value={paidMode} onChange={event=>setPaidMode(event.target.value as PolicyDurationOverrideMode)}><option value="Inherit">Standaard</option><option value="Value">Limiet</option><option value="Unlimited">Onbeperkt</option></select></label>
                {paidMode==="Value"?<label className="admin-field"><span>Uren</span><input className="admin-field__control--number" type="number" min=".25" step=".25" value={paidHours} onChange={event=>setPaidHours(event.target.value)}/></label>:<div className="admin-user-detail__effective"><span>Effectief</span><strong className="admin-duration">{formatAdminDuration(detail.policy.effective.maxPaidParkingDurationMinutes,"Onbeperkt")}</strong></div>}
              </div>
              <div className="admin-user-detail__policy-row">
                <div className="admin-user-detail__policy-copy"><strong>Max. totale Visitduur</strong><small>Standaard: {formatAdminDuration(detail.policy.defaults.maxVisitElapsedDurationMinutes,"Onbeperkt")}</small></div>
                <label className="admin-field"><span>Waarde</span><select value={elapsedMode} onChange={event=>setElapsedMode(event.target.value as PolicyDurationOverrideMode)}><option value="Inherit">Standaard</option><option value="Value">Limiet</option><option value="Unlimited">Onbeperkt</option></select></label>
                {elapsedMode==="Value"?<label className="admin-field"><span>Uren</span><input className="admin-field__control--number" type="number" min=".25" step=".25" value={elapsedHours} onChange={event=>setElapsedHours(event.target.value)}/></label>:<div className="admin-user-detail__effective"><span>Effectief</span><strong className="admin-duration">{formatAdminDuration(detail.policy.effective.maxVisitElapsedDurationMinutes,"Onbeperkt")}</strong></div>}
              </div>
              <div className="admin-user-detail__policy-row">
                <div className="admin-user-detail__policy-copy"><strong>Visit verlengen</strong><small>Standaard: {booleanText(detail.policy.defaults.allowVisitExtension)}</small></div>
                <label className="admin-field"><span>Waarde</span><select value={extensionMode} onChange={event=>setExtensionMode(event.target.value as BooleanOverrideMode)}><option value="default">Standaard</option><option value="true">Ja</option><option value="false">Nee</option></select></label>
                <div className="admin-user-detail__effective"><span>Effectief</span><strong>{booleanText(detail.policy.effective.allowVisitExtension)}</strong></div>
              </div>
              <div className="admin-user-detail__policy-row">
                <div className="admin-user-detail__policy-copy"><strong>Open einde toestaan</strong><small>Standaard: {booleanText(detail.policy.defaults.allowOpenEndedVisits)}</small></div>
                <label className="admin-field"><span>Waarde</span><select value={openEndedMode} onChange={event=>setOpenEndedMode(event.target.value as BooleanOverrideMode)}><option value="default">Standaard</option><option value="true">Ja</option><option value="false">Nee</option></select></label>
                <div className="admin-user-detail__effective"><span>Effectief</span><strong>{booleanText(detail.policy.effective.allowOpenEndedVisits)}</strong></div>
              </div>
              <div className="admin-user-detail__policy-row">
                <div className="admin-user-detail__policy-copy"><strong>Max. gelijktijdige Visits</strong><small>Standaard: {formatAdminNumber(detail.policy.defaults.maxConcurrentVisits)} · globaal maximum: {formatAdminNumber(detail.policy.globalMaxConcurrentVisits)}</small></div>
                <label className="admin-field"><span>Bron</span><select value={concurrencyOverride?"override":"default"} onChange={event=>setConcurrencyOverride(event.target.value==="override")}><option value="default">Standaard</option><option value="override">Afwijkend</option></select></label>
                {concurrencyOverride?<label className="admin-field"><span>Aantal</span><input className="admin-field__control--number" type="number" min="1" max={detail.policy.globalMaxConcurrentVisits} step="1" value={concurrency} onChange={event=>setConcurrency(event.target.value)}/></label>:<div className="admin-user-detail__effective"><span>Effectief</span><strong className="admin-number">{formatAdminNumber(detail.policy.effective.maxConcurrentVisits)}</strong></div>}
              </div>
            </div>
            <p className="admin-user-detail__warning">Voor duurvelden kan de gebruiker de standaard volgen, een eigen limiet krijgen of expliciet onbeperkt worden ingesteld.</p>
            <div className="admin-user-detail__policy-actions">
              <Button className="admin-action--field" onClick={()=>void savePolicy()} disabled={saving}>{saving?"Opslaan…":"Policy opslaan"}</Button>
              <Button className="admin-action--field" variant="secondary" onClick={()=>applyPolicyForm(detail)} disabled={saving}>Wijzigingen terugzetten</Button>
            </div>
          </section>
        </>}
  </div>;
}
