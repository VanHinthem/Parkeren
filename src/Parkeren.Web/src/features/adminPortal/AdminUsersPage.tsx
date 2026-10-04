import { useEffect,useMemo,useState,type FormEvent } from "react";
import {
  assignVehicle,
  archiveUser,
  archiveVehicle,
  createUser,
  createVehicle,
  deleteUser,
  deleteVehicle,
  getAdminUserDetail,
  getUsers,
  getVehicles,
  resetUserPin,
  revokeUserSessions,
  setAdminUserPolicy,
  setUserActive,
  setVehicleActive,
  unassignVehicle,
  type AdminUserDetail,
  type AdminUserPolicyUpdate,
  type PolicyDurationOverrideMode,
  type UserSummary,
  type VehicleSummary
} from "../../api/client";
import { LicensePlate } from "../../components/LicensePlate";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import "./AdminUsers.css";

type BooleanOverrideMode="default"|"true"|"false";

function formatDuration(minutes:number|null){
  if(minutes===null)return "Onbeperkt";
  if(minutes%60===0)return `${minutes/60} uur`;
  return `${minutes} min`;
}

function booleanText(value:boolean){return value?"Ja":"Nee";}

function hoursValue(minutes:number|null,fallback:number){
  return String((minutes??fallback)/60);
}

function hoursToMinutes(value:string){
  const hours=Number(value);
  if(!Number.isFinite(hours)||hours<=0)return null;
  return Math.round(hours*60);
}

function modeToBoolean(value:BooleanOverrideMode){
  if(value==="default")return null;
  return value==="true";
}

export function AdminUsersPage({mode}:{mode:"users"|"vehicles"}){
  const[users,setUsers]=useState<UserSummary[]>([]);
  const[vehicles,setVehicles]=useState<VehicleSummary[]>([]);
  const[username,setUsername]=useState("");
  const[pin,setPin]=useState("");
  const[licensePlate,setLicensePlate]=useState("");
  const[displayName,setDisplayName]=useState("");
  const[error,setError]=useState<string>();
  const[message,setMessage]=useState<string>();
  const[loading,setLoading]=useState(true);

  async function load(){
    setLoading(true);
    setError(undefined);
    try{
      const[userRows,vehicleRows]=await Promise.all([getUsers(),getVehicles()]);
      setUsers(userRows);
      setVehicles(vehicleRows);
    }catch(e){
      setError(e instanceof Error?e.message:"Beheerdata kon niet worden geladen.");
    }finally{
      setLoading(false);
    }
  }

  useEffect(()=>{void load();},[]);

  async function addUser(event:FormEvent){
    event.preventDefault();
    setError(undefined);
    setMessage(undefined);
    try{
      await createUser(username,pin);
      setUsername("");
      setPin("");
      setMessage("Bezoeker is aangemaakt.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Bezoeker kon niet worden aangemaakt.");
    }
  }

  async function addVehicle(event:FormEvent){
    event.preventDefault();
    setError(undefined);
    setMessage(undefined);
    try{
      await createVehicle(licensePlate,displayName);
      setLicensePlate("");
      setDisplayName("");
      setMessage("Voertuig is aangemaakt.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Voertuig kon niet worden aangemaakt.");
    }
  }

  async function toggleUser(user:UserSummary){
    setError(undefined);
    setMessage(undefined);
    try{
      await setUserActive(user.id,!user.isActive);
      setMessage(user.isActive?"Gebruiker is gedeactiveerd.":"Gebruiker is geactiveerd.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Gebruiker kon niet worden gewijzigd.");
    }
  }

  async function toggleVehicle(vehicle:VehicleSummary){
    setError(undefined);
    setMessage(undefined);
    try{
      await setVehicleActive(vehicle.id,!vehicle.isActive);
      setMessage(vehicle.isActive?"Voertuig is gedeactiveerd.":"Voertuig is geactiveerd.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Voertuig kon niet worden gewijzigd.");
    }
  }

  async function archiveUserRecord(user:UserSummary){
    if(!window.confirm(`Gebruiker ${user.username} archiveren? Historie blijft behouden.`))return;
    setError(undefined);
    setMessage(undefined);
    try{
      await archiveUser(user.id);
      setMessage("Gebruiker is gearchiveerd.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Gebruiker kon niet worden gearchiveerd.");
    }
  }

  async function deleteUserRecord(user:UserSummary){
    if(!window.confirm(`Gebruiker ${user.username} permanent verwijderen? Dit kan niet ongedaan worden gemaakt.`))return;
    setError(undefined);
    setMessage(undefined);
    try{
      await deleteUser(user.id);
      setMessage("Gebruiker is verwijderd.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Gebruiker kon niet worden verwijderd.");
    }
  }

  async function archiveVehicleRecord(vehicle:VehicleSummary){
    if(!window.confirm(`Kenteken ${vehicle.licensePlate} archiveren? Historie blijft behouden.`))return;
    setError(undefined);
    setMessage(undefined);
    try{
      await archiveVehicle(vehicle.id);
      setMessage("Voertuig is gearchiveerd.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Voertuig kon niet worden gearchiveerd.");
    }
  }

  async function deleteVehicleRecord(vehicle:VehicleSummary){
    if(!window.confirm(`Kenteken ${vehicle.licensePlate} permanent verwijderen? Dit kan niet ongedaan worden gemaakt.`))return;
    setError(undefined);
    setMessage(undefined);
    try{
      await deleteVehicle(vehicle.id);
      setMessage("Voertuig is verwijderd.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Voertuig kon niet worden verwijderd.");
    }
  }

  return <div className="admin-users">
    <nav className="admin-users__tabs" aria-label="Gebruikers en voertuigen">
      <a className={"admin-users__tab "+(mode==="users"?"active":"")} href="/beheer/gebruikers">Gebruikers</a>
      <a className={"admin-users__tab "+(mode==="vehicles"?"active":"")} href="/beheer/voertuigen">Voertuigen</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}
    {message&&<Alert>{message}</Alert>}

    {mode==="users"
      ? <section className="admin-users__panel">
          <h2>Gebruikers</h2>
          <form className="admin-users__create" onSubmit={addUser}>
            <label className="admin-users__field">
              <span>Gebruikersnaam</span>
              <input value={username} onChange={event=>setUsername(event.target.value)} required/>
            </label>
            <label className="admin-users__field">
              <span>Tijdelijke PIN</span>
              <input type="password" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} value={pin} onChange={event=>setPin(event.target.value.replace(/\D/g,"").slice(0,6))} required/>
            </label>
            <Button disabled={pin.length!==6}>Bezoeker toevoegen</Button>
          </form>

          {loading
            ? <Loading label="Gebruikers laden"/>
            : <div className="admin-users__table-wrap">
                <table className="admin-users__table">
                  <thead><tr><th>Gebruiker</th><th>Rol</th><th>Status</th><th>Concurrency override</th><th aria-label="Acties"/></tr></thead>
                  <tbody>
                    {users.map(user=><tr key={user.id}>
                      <td><strong>{user.username}</strong></td>
                      <td>{user.role==="Admin"?"Beheerder":"Bezoeker"}</td>
                      <td><span className={"admin-users__status "+(user.isActive?"admin-users__status--active":"")}>{user.status==="Archived"?"Gearchiveerd":user.isActive?"Actief":"Inactief"}</span></td>
                      <td>{user.maxConcurrentVisits??"Standaard"}</td>
                      <td>
                        <div className="admin-users__actions">
                          <a className="admin-users__link" href={`/beheer/gebruikers/${user.id}`}>Openen</a>
                          {user.status!=="Archived"&&<Button variant="secondary" onClick={()=>void toggleUser(user)}>{user.isActive?"Deactiveren":"Activeren"}</Button>}
                          {user.status!=="Archived"&&<Button variant="secondary" onClick={()=>void archiveUserRecord(user)}>Archiveren</Button>}
                          {user.canDelete&&<Button variant="secondary" onClick={()=>void deleteUserRecord(user)}>Verwijderen</Button>}
                        </div>
                      </td>
                    </tr>)}
                  </tbody>
                </table>
              </div>}
        </section>
      : <section className="admin-users__panel">
          <h2>Voertuigen</h2>
          <form className="admin-users__create" onSubmit={addVehicle}>
            <label className="admin-users__field">
              <span>Kenteken</span>
              <input value={licensePlate} onChange={event=>setLicensePlate(event.target.value.toUpperCase())} required/>
            </label>
            <label className="admin-users__field">
              <span>Omschrijving</span>
              <input value={displayName} onChange={event=>setDisplayName(event.target.value)} placeholder="Optioneel"/>
            </label>
            <Button>Voertuig toevoegen</Button>
          </form>

          {loading
            ? <Loading label="Voertuigen laden"/>
            : <div className="admin-users__table-wrap">
                <table className="admin-users__table">
                  <thead><tr><th>Kenteken</th><th>Omschrijving</th><th>Status</th><th aria-label="Acties"/></tr></thead>
                  <tbody>
                    {vehicles.map(vehicle=><tr key={vehicle.id}>
                      <td><LicensePlate value={vehicle.licensePlate}/></td>
                      <td>{vehicle.displayName??"—"}</td>
                      <td><span className={"admin-users__status "+(vehicle.isActive?"admin-users__status--active":"")}>{vehicle.status==="Archived"?"Gearchiveerd":vehicle.isActive?"Actief":"Inactief"}</span></td>
                      <td><div className="admin-users__actions">
                        {vehicle.status!=="Archived"&&<Button variant="secondary" onClick={()=>void toggleVehicle(vehicle)}>{vehicle.isActive?"Deactiveren":"Activeren"}</Button>}
                        {vehicle.status!=="Archived"&&<Button variant="secondary" onClick={()=>void archiveVehicleRecord(vehicle)}>Archiveren</Button>}
                        {vehicle.canDelete&&<Button variant="secondary" onClick={()=>void deleteVehicleRecord(vehicle)}>Verwijderen</Button>}
                      </div></td>
                    </tr>)}
                  </tbody>
                </table>
              </div>}
        </section>}
  </div>;
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
    if(!availableVehicles.some(vehicle=>vehicle.id===selectedVehicleId))
      setSelectedVehicleId(availableVehicles[0]?.id??"");
  },[availableVehicles,selectedVehicleId]);

  async function savePolicy(){
    if(!detail)return;

    const paidMinutes=paidMode==="Value"?hoursToMinutes(paidHours):null;
    const elapsedMinutes=elapsedMode==="Value"?hoursToMinutes(elapsedHours):null;
    const concurrent=concurrencyOverride?Number(concurrency):null;
    if((paidMode==="Value"&&paidMinutes===null)||
       (elapsedMode==="Value"&&elapsedMinutes===null)||
       (concurrencyOverride&&(concurrent===null||!Number.isInteger(concurrent)||concurrent<=0))){
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
    }catch(e){
      setError(e instanceof Error?e.message:"Voertuig kon niet worden toegewezen.");
    }
  }

  async function unassign(vehicleId:string){
    setError(undefined);
    setMessage(undefined);
    try{
      await unassignVehicle(userId,vehicleId);
      setMessage("Toewijzing is verwijderd.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Toewijzing kon niet worden verwijderd.");
    }
  }

  async function toggleActive(){
    if(!detail||detail.user.status==="Archived")return;
    setError(undefined);
    setMessage(undefined);
    try{
      await setUserActive(userId,!detail.user.isActive);
      setMessage(detail.user.isActive?"Gebruiker is gedeactiveerd.":"Gebruiker is geactiveerd.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Gebruiker kon niet worden gewijzigd.");
    }
  }

  if(detail===undefined)return <Loading label="Gebruikersdetail laden"/>;

  return <div className="admin-user-detail">
    <div className="admin-user-detail__toolbar">
      <a className="admin-users__link" href="/beheer/gebruikers">← Terug naar gebruikers</a>
      {detail&&detail.user.status!=="Archived"&&<Button variant="secondary" onClick={()=>void toggleActive()}>{detail.user.isActive?"Deactiveren":"Activeren"}</Button>}
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
              <div className="admin-user-detail__summary-item"><span>Status</span><strong>{detail.user.status==="Archived"?"Gearchiveerd":detail.user.isActive?"Actief":"Inactief"}</strong></div>
              <div className="admin-user-detail__summary-item"><span>Actieve Visits</span><strong>{detail.activeVisitCount}</strong></div>
            </div>
          </section>

          <div className="admin-user-detail__columns">
            <section className="admin-users__panel">
              <h2>Voertuigen</h2>
              <div className="admin-user-detail__assign">
                <label className="admin-users__field">
                  <span>Actief voertuig toevoegen</span>
                  <select value={selectedVehicleId} onChange={event=>setSelectedVehicleId(event.target.value)} disabled={availableVehicles.length===0}>
                    {availableVehicles.length===0?<option value="">Geen voertuig beschikbaar</option>:availableVehicles.map(vehicle=><option value={vehicle.id} key={vehicle.id}>{vehicle.licensePlate}</option>)}
                  </select>
                </label>
                <Button onClick={()=>void assign()} disabled={!selectedVehicleId}>Toewijzen</Button>
              </div>
              <div className="admin-user-detail__list">
                {detail.assignedVehicles.length===0
                  ? <p className="admin-users__empty">Geen voertuigen toegewezen.</p>
                  : detail.assignedVehicles.map(vehicle=><div className="admin-user-detail__row" key={vehicle.id}>
                      <span className="admin-user-detail__row-copy">
                        <LicensePlate value={vehicle.licensePlate}/>
                        <small>{vehicle.isActive?"Actief":"Inactief"}{vehicle.displayName?` · ${vehicle.displayName}`:""}</small>
                      </span>
                      <Button variant="secondary" onClick={()=>void unassign(vehicle.id)}>Ontkoppelen</Button>
                    </div>)}
              </div>
            </section>

            <section className="admin-users__panel">
              <h2>Accountbeheer</h2>
              <label className="admin-users__field">
                <span>Nieuwe tijdelijke PIN</span>
                <input type="password" inputMode="numeric" maxLength={6} value={adminPin} onChange={event=>setAdminPin(event.target.value.replace(/\D/g,"").slice(0,6))}/>
              </label>
              <div className="admin-user-detail__account-actions">
                <Button variant="secondary" disabled={adminPin.length!==6} onClick={async()=>{
                  try{
                    await resetUserPin(userId,adminPin);
                    setAdminPin("");
                    setError(undefined);
                    setMessage("PIN is gereset; bestaande sessies zijn ingetrokken.");
                  }catch(e){
                    setError(e instanceof Error?e.message:"PIN resetten mislukt.");
                  }
                }}>PIN resetten</Button>
                <Button variant="secondary" onClick={async()=>{
                  try{
                    await revokeUserSessions(userId);
                    setError(undefined);
                    setMessage("Alle sessies zijn ingetrokken.");
                  }catch(e){
                    setError(e instanceof Error?e.message:"Sessies intrekken mislukt.");
                  }
                }}>Sessies intrekken</Button>
              </div>
            </section>
          </div>

          <section className="admin-users__panel">
            <h2>Gebruikerspolicy</h2>
            {detail.activeVisitCount>0&&<Alert tone="warning">
              Deze gebruiker heeft {detail.activeVisitCount} actieve Visit(s). Een wijziging die de effectieve policy verandert wordt server-side geblokkeerd.
            </Alert>}
            <div className="admin-user-detail__policy">
              <div className="admin-user-detail__policy-row">
                <div className="admin-user-detail__policy-copy"><strong>Max. betaalde parkeertijd</strong><small>Standaard: {formatDuration(detail.policy.defaults.maxPaidParkingDurationMinutes)}</small></div>
                <label className="admin-users__field"><span>Waarde</span><select value={paidMode} onChange={event=>setPaidMode(event.target.value as PolicyDurationOverrideMode)}><option value="Inherit">Standaard</option><option value="Value">Limiet</option><option value="Unlimited">Onbeperkt</option></select></label>
                {paidMode==="Value"
                  ? <label className="admin-users__field"><span>Uren</span><input type="number" min=".25" step=".25" value={paidHours} onChange={event=>setPaidHours(event.target.value)}/></label>
                  : <div className="admin-user-detail__effective"><span>Effectief</span><strong>{formatDuration(detail.policy.effective.maxPaidParkingDurationMinutes)}</strong></div>}
              </div>

              <div className="admin-user-detail__policy-row">
                <div className="admin-user-detail__policy-copy"><strong>Max. totale Visitduur</strong><small>Standaard: {formatDuration(detail.policy.defaults.maxVisitElapsedDurationMinutes)}</small></div>
                <label className="admin-users__field"><span>Waarde</span><select value={elapsedMode} onChange={event=>setElapsedMode(event.target.value as PolicyDurationOverrideMode)}><option value="Inherit">Standaard</option><option value="Value">Limiet</option><option value="Unlimited">Onbeperkt</option></select></label>
                {elapsedMode==="Value"
                  ? <label className="admin-users__field"><span>Uren</span><input type="number" min=".25" step=".25" value={elapsedHours} onChange={event=>setElapsedHours(event.target.value)}/></label>
                  : <div className="admin-user-detail__effective"><span>Effectief</span><strong>{formatDuration(detail.policy.effective.maxVisitElapsedDurationMinutes)}</strong></div>}
              </div>

              <div className="admin-user-detail__policy-row">
                <div className="admin-user-detail__policy-copy"><strong>Visit verlengen</strong><small>Standaard: {booleanText(detail.policy.defaults.allowVisitExtension)}</small></div>
                <label className="admin-users__field"><span>Waarde</span><select value={extensionMode} onChange={event=>setExtensionMode(event.target.value as BooleanOverrideMode)}><option value="default">Standaard</option><option value="true">Ja</option><option value="false">Nee</option></select></label>
                <div className="admin-user-detail__effective"><span>Effectief</span><strong>{booleanText(detail.policy.effective.allowVisitExtension)}</strong></div>
              </div>

              <div className="admin-user-detail__policy-row">
                <div className="admin-user-detail__policy-copy"><strong>Open einde toestaan</strong><small>Standaard: {booleanText(detail.policy.defaults.allowOpenEndedVisits)}</small></div>
                <label className="admin-users__field"><span>Waarde</span><select value={openEndedMode} onChange={event=>setOpenEndedMode(event.target.value as BooleanOverrideMode)}><option value="default">Standaard</option><option value="true">Ja</option><option value="false">Nee</option></select></label>
                <div className="admin-user-detail__effective"><span>Effectief</span><strong>{booleanText(detail.policy.effective.allowOpenEndedVisits)}</strong></div>
              </div>

              <div className="admin-user-detail__policy-row">
                <div className="admin-user-detail__policy-copy"><strong>Max. gelijktijdige Visits</strong><small>Standaard: {detail.policy.defaults.maxConcurrentVisits} · globaal maximum: {detail.policy.globalMaxConcurrentVisits}</small></div>
                <label className="admin-users__field"><span>Bron</span><select value={concurrencyOverride?"override":"default"} onChange={event=>setConcurrencyOverride(event.target.value==="override")}><option value="default">Standaard</option><option value="override">Afwijkend</option></select></label>
                {concurrencyOverride
                  ? <label className="admin-users__field"><span>Aantal</span><input type="number" min="1" max={detail.policy.globalMaxConcurrentVisits} step="1" value={concurrency} onChange={event=>setConcurrency(event.target.value)}/></label>
                  : <div className="admin-user-detail__effective"><span>Effectief</span><strong>{detail.policy.effective.maxConcurrentVisits}</strong></div>}
              </div>
            </div>

            <p className="admin-user-detail__warning">
              Voor duurvelden kan de gebruiker de standaard volgen, een eigen limiet krijgen of expliciet onbeperkt worden ingesteld.
            </p>
            <div className="admin-user-detail__policy-actions">
              <Button onClick={()=>void savePolicy()} disabled={saving}>{saving?"Opslaan…":"Policy opslaan"}</Button>
              <Button variant="secondary" onClick={()=>applyPolicyForm(detail)} disabled={saving}>Wijzigingen terugzetten</Button>
            </div>
          </section>
        </>}
  </div>;
}
