import { useEffect,useState,type FormEvent } from "react";
import {
  archiveUser,
  archiveVehicle,
  createUser,
  createVehicle,
  deleteUser,
  deleteVehicle,
  getUsers,
  getVehicles,
  setUserActive,
  setVehicleActive,
  type UserSummary,
  type VehicleSummary
} from "../../api/client";
import { LicensePlate } from "../../components/LicensePlate";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import "./AdminUsers.css";

function statusClass(isActive:boolean,status:string){
  if(status==="Archived")return "admin-status";
  return isActive?"admin-status admin-status--active":"admin-status";
}

function statusLabel(isActive:boolean,status:string){
  return status==="Archived"?"Gearchiveerd":isActive?"Actief":"Inactief";
}

export function AdminUsersOverviewPage({mode}:{mode:"users"|"vehicles"}){
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
    <nav className="admin-subnav" aria-label="Gebruikers en voertuigen">
      <a className={`admin-subnav__link ${mode==="users"?"active":""}`} href="/beheer/gebruikers">Gebruikers</a>
      <a className={`admin-subnav__link ${mode==="vehicles"?"active":""}`} href="/beheer/voertuigen">Voertuigen</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}
    {message&&<Alert>{message}</Alert>}

    {mode==="users"
      ? <section className="admin-users__panel">
          <h2>Gebruikers</h2>
          <form className="admin-users__create" onSubmit={addUser}>
            <label className="admin-field">
              <span>Gebruikersnaam</span>
              <input value={username} onChange={event=>setUsername(event.target.value)} required/>
            </label>
            <label className="admin-field">
              <span>Tijdelijke PIN</span>
              <input type="password" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} value={pin} onChange={event=>setPin(event.target.value.replace(/\D/g,"").slice(0,6))} required/>
            </label>
            <Button className="admin-action--field admin-users__create-action" disabled={pin.length!==6}>Bezoeker toevoegen</Button>
          </form>

          {loading
            ? <Loading label="Gebruikers laden"/>
            : <div className="admin-users__table-wrap">
                <table className="admin-users__table admin-users__table--users">
                  <thead><tr><th>Gebruiker</th><th>Rol</th><th>Status</th><th>Max. gelijktijdig</th><th aria-label="Acties"/></tr></thead>
                  <tbody>
                    {users.map(user=><tr key={user.id}>
                      <td><span className="admin-identity"><strong>{user.username}</strong></span></td>
                      <td>{user.role==="Admin"?"Beheerder":"Bezoeker"}</td>
                      <td><span className={statusClass(user.isActive,user.status)}>{statusLabel(user.isActive,user.status)}</span></td>
                      <td className={user.maxConcurrentVisits===null?undefined:"admin-number"}>{user.maxConcurrentVisits??"Standaard"}</td>
                      <td>
                        <div className="admin-action-group admin-users__actions">
                          <a className="admin-action-link" href={`/beheer/gebruikers/${user.id}`}>Details</a>
                          {user.status!=="Archived"&&<Button className="admin-action--compact" variant="secondary" onClick={()=>void toggleUser(user)}>{user.isActive?"Deactiveren":"Activeren"}</Button>}
                          {user.status!=="Archived"&&<Button className="admin-action--compact" variant="secondary" onClick={()=>void archiveUserRecord(user)}>Archiveren</Button>}
                          {user.canDelete&&<Button className="admin-action--compact" variant="secondary" onClick={()=>void deleteUserRecord(user)}>Verwijderen</Button>}
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
            <label className="admin-field">
              <span>Kenteken</span>
              <input value={licensePlate} onChange={event=>setLicensePlate(event.target.value.toUpperCase())} required/>
            </label>
            <label className="admin-field">
              <span>Omschrijving</span>
              <input value={displayName} onChange={event=>setDisplayName(event.target.value)} placeholder="Optioneel"/>
            </label>
            <Button className="admin-action--field admin-users__create-action">Voertuig toevoegen</Button>
          </form>

          {loading
            ? <Loading label="Voertuigen laden"/>
            : <div className="admin-users__table-wrap">
                <table className="admin-users__table admin-users__table--vehicles">
                  <thead><tr><th>Kenteken</th><th>Omschrijving</th><th>Status</th><th aria-label="Acties"/></tr></thead>
                  <tbody>
                    {vehicles.map(vehicle=><tr key={vehicle.id}>
                      <td><LicensePlate value={vehicle.licensePlate}/></td>
                      <td>{vehicle.displayName??"—"}</td>
                      <td><span className={statusClass(vehicle.isActive,vehicle.status)}>{statusLabel(vehicle.isActive,vehicle.status)}</span></td>
                      <td><div className="admin-action-group admin-users__actions">
                        {vehicle.status!=="Archived"&&<Button className="admin-action--compact" variant="secondary" onClick={()=>void toggleVehicle(vehicle)}>{vehicle.isActive?"Deactiveren":"Activeren"}</Button>}
                        {vehicle.status!=="Archived"&&<Button className="admin-action--compact" variant="secondary" onClick={()=>void archiveVehicleRecord(vehicle)}>Archiveren</Button>}
                        {vehicle.canDelete&&<Button className="admin-action--compact" variant="secondary" onClick={()=>void deleteVehicleRecord(vehicle)}>Verwijderen</Button>}
                      </div></td>
                    </tr>)}
                  </tbody>
                </table>
              </div>}
        </section>}
  </div>;
}
