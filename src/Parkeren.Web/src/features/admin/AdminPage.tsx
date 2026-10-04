import { useEffect,useState,type FormEvent } from "react";
import {
  archiveUser,
  archiveVehicle,
  assignVehicle,
  getAssignedVehicles,
  unassignVehicle,
  createUser,
  createVehicle,
  deleteUser,
  deleteVehicle,
  getUsers,
  getVehicles,
  setUserActive,
  setVehicleActive,
  resetUserPin,
  revokeUserSessions,
  type UserSummary,
  type VehicleSummary
} from "../../api/client";
import { LicensePlate } from "../../components/LicensePlate";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Card } from "../../design/primitives/Card";
import { Input } from "../../design/primitives/Input";
import "./AdminPage.css";

export function AdminPage(){
  const[adminPin,setAdminPin]=useState("");
  const[users,setUsers]=useState<UserSummary[]>([]);
  const[vehicles,setVehicles]=useState<VehicleSummary[]>([]);
  const[error,setError]=useState<string>();
  const[username,setUsername]=useState("");
  const[pin,setPin]=useState("");
  const[plate,setPlate]=useState("");
  const[name,setName]=useState("");
  const[selectedUser,setSelectedUser]=useState("");
  const[selectedVehicle,setSelectedVehicle]=useState("");
  const[assigned,setAssigned]=useState<VehicleSummary[]>([]);
  const[message,setMessage]=useState<string>();

  async function refresh(){
    try{
      const[u,v]=await Promise.all([getUsers(),getVehicles()]);
      setUsers(u);
      setVehicles(v);
      if(!selectedUser){
        const defaultUser=u.find(x=>x.role==="Visitor")??u[0];
        if(defaultUser)setSelectedUser(defaultUser.id);
      }
      if(!selectedVehicle&&v[0])setSelectedVehicle(v[0].id);
    }catch(e){
      setError(e instanceof Error?e.message:"Laden mislukt.");
    }
  }

  useEffect(()=>{void refresh();},[]);
  useEffect(()=>{
    if(selectedUser)getAssignedVehicles(selectedUser).then(setAssigned).catch(()=>setAssigned([]));
    else setAssigned([]);
  },[selectedUser]);

  async function addUser(e:FormEvent){
    e.preventDefault();
    setError(undefined);
    try{
      await createUser(username,pin);
      setUsername("");
      setPin("");
      await refresh();
    }catch(e){
      setError(e instanceof Error?e.message:"Aanmaken mislukt.");
    }
  }

  async function addVehicle(e:FormEvent){
    e.preventDefault();
    setError(undefined);
    try{
      await createVehicle(plate,name);
      setPlate("");
      setName("");
      await refresh();
    }catch(e){
      setError(e instanceof Error?e.message:"Aanmaken mislukt.");
    }
  }

  async function link(){
    if(!selectedUser||!selectedVehicle)return;
    setError(undefined);
    setMessage(undefined);
    try{
      await assignVehicle(selectedUser,selectedVehicle);
      setMessage("Voertuig is toegewezen.");
      try{
        setAssigned(await getAssignedVehicles(selectedUser));
      }catch{
        setError("De toewijzing is opgeslagen, maar de lijst kon niet opnieuw worden geladen.");
      }
    }catch(e){
      setError(e instanceof Error?e.message:"Toewijzen mislukt.");
    }
  }

  async function archiveUserRecord(user:UserSummary){
    if(!window.confirm(`Gebruiker ${user.username} archiveren? Historie blijft behouden.`))return;
    setError(undefined);
    try{
      await archiveUser(user.id);
      setMessage("Gebruiker is gearchiveerd.");
      await refresh();
    }catch(e){
      setError(e instanceof Error?e.message:"Archiveren mislukt.");
    }
  }

  async function deleteUserRecord(user:UserSummary){
    if(!window.confirm(`Gebruiker ${user.username} permanent verwijderen? Dit kan niet ongedaan worden gemaakt.`))return;
    setError(undefined);
    try{
      await deleteUser(user.id);
      setMessage("Gebruiker is verwijderd.");
      await refresh();
    }catch(e){
      setError(e instanceof Error?e.message:"Verwijderen mislukt.");
    }
  }

  async function archiveVehicleRecord(vehicle:VehicleSummary){
    if(!window.confirm(`Kenteken ${vehicle.licensePlate} archiveren? Historie blijft behouden.`))return;
    setError(undefined);
    try{
      await archiveVehicle(vehicle.id);
      setMessage("Voertuig is gearchiveerd.");
      await refresh();
    }catch(e){
      setError(e instanceof Error?e.message:"Archiveren mislukt.");
    }
  }

  async function deleteVehicleRecord(vehicle:VehicleSummary){
    if(!window.confirm(`Kenteken ${vehicle.licensePlate} permanent verwijderen? Dit kan niet ongedaan worden gemaakt.`))return;
    setError(undefined);
    try{
      await deleteVehicle(vehicle.id);
      setMessage("Voertuig is verwijderd.");
      await refresh();
    }catch(e){
      setError(e instanceof Error?e.message:"Verwijderen mislukt.");
    }
  }

  const selectedUsername=users.find(u=>u.id===selectedUser)?.username??"";

  return <div className="admin-grid">
    {error&&<Alert tone="danger">{error}</Alert>}
    {message&&<Alert tone="info">{message}</Alert>}

    <Card className="admin-card admin-card--users">
      <h2>Bezoekers</h2>
      <form onSubmit={addUser} className="admin-form">
        <Input label="Gebruikersnaam" value={username} onChange={e=>setUsername(e.target.value)} required/>
        <Input label="Tijdelijke PIN" type="password" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} autoComplete="new-password" value={pin} onChange={e=>setPin(e.target.value.replace(/\D/g,"").slice(0,6))} required/>
        <Button disabled={pin.length!==6}>Bezoeker toevoegen</Button>
      </form>

      <div className="admin-list">
        {users.map(u=>
          <div
            className={"admin-row admin-user"+(u.id===selectedUser?" admin-user--selected":"")}
            key={u.id}
            onClick={()=>{setMessage(undefined);setSelectedUser(u.id)}}
            role="button"
            tabIndex={0}
            onKeyDown={e=>{if(e.key==="Enter"||e.key===" "){e.preventDefault();setSelectedUser(u.id)}}}
          >
            <div className="admin-row__content">
              <strong>{u.username}</strong>
              <small>
                {u.role==="Admin"?"Beheerder":"Bezoeker"}
                <span className={"admin-status "+(u.isActive?"admin-status--active":"admin-status--inactive")}>
                  {u.status==="Archived"?"Gearchiveerd":u.isActive?"Actief":"Inactief"}
                </span>
              </small>
            </div>
            <div className="admin-row__actions">
              {u.status!=="Archived"&&<Button variant="secondary" onClick={async e=>{
                e.stopPropagation();
                try{
                  await setUserActive(u.id,!u.isActive);
                  await refresh();
                }catch(error){
                  setError(error instanceof Error?error.message:"Status wijzigen mislukt.");
                }
              }}>
                {u.isActive?"Deactiveren":"Activeren"}
              </Button>}
              {u.status!=="Archived"&&<Button variant="secondary" onClick={e=>{e.stopPropagation();void archiveUserRecord(u);}}>Archiveren</Button>}
              {u.canDelete&&<Button variant="secondary" onClick={e=>{e.stopPropagation();void deleteUserRecord(u);}}>Verwijderen</Button>}
            </div>
          </div>)}
      </div>
    </Card>

    <Card className="admin-card admin-card--vehicles">
      <h2>Voertuigen</h2>
      <form onSubmit={addVehicle} className="admin-form">
        <Input label="Kenteken" value={plate} onChange={e=>setPlate(e.target.value.toUpperCase())} required/>
        <Input label="Omschrijving (optioneel)" value={name} onChange={e=>setName(e.target.value)}/>
        <Button>Voertuig toevoegen</Button>
      </form>

      <div className="admin-list">
        {vehicles.map(v=>
          <div className="admin-row" key={v.id}>
            <div className="admin-row__content">
              <LicensePlate value={v.licensePlate}/>
              <small>
                {v.displayName||"Geen omschrijving"}
                <span className={"admin-status "+(v.isActive?"admin-status--active":"admin-status--inactive")}>
                  {v.status==="Archived"?"Gearchiveerd":v.isActive?"Actief":"Inactief"}
                </span>
              </small>
            </div>
            <div className="admin-row__actions">
              {v.status!=="Archived"&&<Button variant="secondary" onClick={()=>void setVehicleActive(v.id,!v.isActive).then(refresh).catch(e=>setError(e instanceof Error?e.message:"Status wijzigen mislukt."))}>
                {v.isActive?"Deactiveren":"Activeren"}
              </Button>}
              {v.status!=="Archived"&&<Button variant="secondary" onClick={()=>void archiveVehicleRecord(v)}>Archiveren</Button>}
              {v.canDelete&&<Button variant="secondary" onClick={()=>void deleteVehicleRecord(v)}>Verwijderen</Button>}
            </div>
          </div>)}
      </div>
    </Card>

    <Card className="admin-card admin-card--wide">
      <h2>{selectedUser?"Voertuigen van "+selectedUsername:"Voertuigen toewijzen"}</h2>

      <div className="admin-assign">
        <label>
          Voertuig
          <select aria-label="Voertuig" value={selectedVehicle} onChange={e=>setSelectedVehicle(e.target.value)}>
            {vehicles.filter(v=>v.isActive).map(v=><option value={v.id} key={v.id}>{v.licensePlate}</option>)}
          </select>
        </label>
        <Button onClick={link} disabled={!selectedUser||!selectedVehicle}>Toewijzen</Button>
      </div>

      <div className="admin-list admin-assigned">
        {assigned.length===0
          ? <p className="admin-empty">Geen voertuigen toegewezen.</p>
          : assigned.map(v=>
              <div className="admin-row" key={v.id}>
                <div className="admin-row__content">
                  <LicensePlate value={v.licensePlate}/>
                  <small>Toegewezen aan {selectedUsername}</small>
                </div>
                <Button variant="secondary" onClick={async()=>{
                  await unassignVehicle(selectedUser,v.id);
                  setAssigned(await getAssignedVehicles(selectedUser));
                  setMessage("Toewijzing is verwijderd.");
                }}>Verwijderen</Button>
              </div>)}
      </div>

      <div className="admin-account-actions">
        <h3>Accountbeheer</h3>
        <Input label="Nieuwe tijdelijke PIN" type="password" inputMode="numeric" maxLength={6} autoComplete="new-password" value={adminPin} onChange={e=>setAdminPin(e.target.value.replace(/\D/g,"").slice(0,6))}/>
        <div className="admin-action-buttons">
          <Button variant="secondary" disabled={!selectedUser||adminPin.length!==6} onClick={async()=>{
            try{
              await resetUserPin(selectedUser,adminPin);
              setAdminPin("");
              setMessage("PIN is gereset; bestaande sessies zijn ingetrokken.");
              setError(undefined);
            }catch(e){
              setError(e instanceof Error?e.message:"PIN resetten mislukt.");
            }
          }}>PIN resetten</Button>
          <Button variant="secondary" disabled={!selectedUser} onClick={async()=>{
            try{
              await revokeUserSessions(selectedUser);
              setMessage("Alle sessies van deze gebruiker zijn ingetrokken.");
              setError(undefined);
            }catch(e){
              setError(e instanceof Error?e.message:"Sessies intrekken mislukt.");
            }
          }}>Sessies intrekken</Button>
        </div>
      </div>
    </Card>
  </div>;
}
