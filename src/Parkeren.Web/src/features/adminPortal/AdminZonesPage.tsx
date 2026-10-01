import { useEffect,useState } from "react";
import {
  closeAdminParkingZone,
  createAdminParkingZone,
  getAdminParkingZones,
  getAdminProviderStatus,
  type AdminParkingZone
} from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import "./AdminZonesPage.css";

function localDateTimeInput(date:Date){
  const pad=(value:number)=>String(value).padStart(2,"0");
  return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function formatDateTime(value:string|null){
  return value
    ? new Date(value).toLocaleString("nl-NL",{dateStyle:"medium",timeStyle:"short"})
    : "doorlopend";
}

function zoneState(zone:AdminParkingZone){
  const now=Date.now();
  const from=new Date(zone.validFrom).getTime();
  const until=zone.validUntil?new Date(zone.validUntil).getTime():null;
  if(now<from)return "Gepland";
  if(until!==null&&now>=until)return "Historisch";
  return "Actief";
}

export function AdminZonesPage(){
  const[zones,setZones]=useState<AdminParkingZone[]>();
  const[error,setError]=useState<string>();
  const[message,setMessage]=useState<string>();
  const[saving,setSaving]=useState(false);

  const[name,setName]=useState("");
  const[providerLocation,setProviderLocation]=useState("");
  const[validFrom,setValidFrom]=useState(localDateTimeInput(new Date()));
  const[validUntil,setValidUntil]=useState("");
  const[isDefault,setIsDefault]=useState(true);

  const[closingZoneId,setClosingZoneId]=useState<string>();
  const[closeAt,setCloseAt]=useState(localDateTimeInput(new Date(Date.now()+24*60*60*1000)));

  async function load(prefill:boolean){
    setError(undefined);
    try{
      const[rows,provider]=await Promise.all([
        getAdminParkingZones(),
        getAdminProviderStatus()
      ]);
      setZones(rows);
      if(prefill&&rows.length===0){
        setName(provider.product?.name?provider.product.name+" zone":"Oss");
        setProviderLocation(provider.product?.location??"");
        setIsDefault(true);
      }
    }catch(e){
      setError(e instanceof Error?e.message:"Parkeerzones konden niet worden geladen.");
    }
  }

  useEffect(()=>{void load(true);},[]);

  async function save(){
    const from=new Date(validFrom);
    const until=validUntil?new Date(validUntil):null;
    if(!name.trim()||!providerLocation.trim()||Number.isNaN(from.getTime())||(until&&Number.isNaN(until.getTime()))||(until&&until<=from)){
      setError("Controleer naam, providerlocatie en geldigheidsperiode.");
      return;
    }

    setSaving(true);
    setError(undefined);
    setMessage(undefined);
    try{
      const result=await createAdminParkingZone({
        name:name.trim(),
        providerLocation:providerLocation.trim(),
        validFrom:from.toISOString(),
        validUntil:until?until.toISOString():null,
        isDefault
      });

      if(result.outcome==="Created"){
        setMessage("Parkeerzone is toegevoegd.");
        setName("");
        setValidFrom(localDateTimeInput(new Date()));
        setValidUntil("");
        await load(false);
      }else if(result.outcome==="DefaultOverlap"){
        setError("Deze defaultzone overlapt met een andere defaultzone. Plan de ingangsdatum na de bestaande defaultperiode.");
      }else{
        setError("De parkeerzone bevat ongeldige waarden.");
      }
    }catch(e){
      setError(e instanceof Error?e.message:"Parkeerzone kon niet worden opgeslagen.");
    }finally{
      setSaving(false);
    }
  }

  async function closeZone(zoneId:string){
    const until=new Date(closeAt);
    if(Number.isNaN(until.getTime())||until.getTime()<=Date.now()){
      setError("De afsluitdatum moet in de toekomst liggen.");
      return;
    }

    setSaving(true);
    setError(undefined);
    setMessage(undefined);
    try{
      const result=await closeAdminParkingZone(zoneId,until.toISOString());
      if(result.outcome==="Closed"){
        setMessage("De zone wordt op de gekozen datum afgesloten.");
        setClosingZoneId(undefined);
        await load(false);
      }else if(result.outcome==="AlreadyClosed"){
        setError("Deze zone heeft al een einddatum.");
      }else if(result.outcome==="NotFound"){
        setError("De zone bestaat niet meer.");
      }else{
        setError("De zone kon niet op deze datum worden afgesloten.");
      }
    }catch(e){
      setError(e instanceof Error?e.message:"Zone afsluiten is mislukt.");
    }finally{
      setSaving(false);
    }
  }

  if(!zones&&!error)return <Loading label="Parkeerzones laden"/>;
  if(!zones)return <div className="admin-zones">
    <Alert tone="danger">{error??"Parkeerzones konden niet worden geladen."}</Alert>
    <div className="admin-zones__actions"><Button onClick={()=>void load(true)}>Opnieuw proberen</Button></div>
  </div>;

  return <div className="admin-zones">
    <nav className="admin-zones__tabs" aria-label="Parkeerconfiguratie">
      <a className="admin-zones__tab active" href="/beheer/configuratie/zones">Zones</a>
      <a className="admin-zones__tab" href="/beheer/configuratie/parkeerregels">Parkeerregels</a>
      <a className="admin-zones__tab" href="/beheer/configuratie/tarieven">Tarieven</a>
      <a className="admin-zones__tab" href="/beheer/configuratie/budgetten">Budgetten</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}
    {message&&<Alert>{message}</Alert>}

    <section className="admin-zones__panel">
      <h2>Parkeerzone toevoegen</h2>
      <p>Meerdere zones mogen tegelijk geldig zijn. Voor nieuwe Visits gebruikt de app de zone die op dat moment als default geldig is. Een nieuwe latere default sluit een bestaande open default automatisch af.</p>

      <div className="admin-zones__grid">
        <label className="admin-zones__field">
          <span>Naam</span>
          <input value={name} onChange={event=>setName(event.target.value)} placeholder="Bijv. Oss centrum"/>
        </label>
        <label className="admin-zones__field">
          <span>2Park provider-location</span>
          <input value={providerLocation} onChange={event=>setProviderLocation(event.target.value)} placeholder="Bijv. OSS_J"/>
        </label>
        <label className="admin-zones__field">
          <span>Geldig vanaf</span>
          <input type="datetime-local" value={validFrom} onChange={event=>setValidFrom(event.target.value)}/>
        </label>
        <label className="admin-zones__field">
          <span>Geldig tot (optioneel)</span>
          <input type="datetime-local" value={validUntil} onChange={event=>setValidUntil(event.target.value)}/>
        </label>
        <label className="admin-zones__check">
          <input type="checkbox" checked={isDefault} onChange={event=>setIsDefault(event.target.checked)}/>
          Defaultzone voor nieuwe Visits
        </label>
      </div>

      <div className="admin-zones__actions">
        <Button onClick={()=>void save()} disabled={saving}>{saving?"Opslaan…":"Zone toevoegen"}</Button>
      </div>
    </section>

    <div className="admin-zones__items">
      {zones.length===0
        ? <section className="admin-zones__item"><p className="admin-zones__empty">Er zijn nog geen zones. Nieuwe Visits kunnen pas worden gestart nadat een geldige defaultzone is geconfigureerd.</p></section>
        : zones.map(zone=>{
            const state=zoneState(zone);
            return <article className="admin-zones__item" key={zone.id}>
              <div className="admin-zones__item-head">
                <div className="admin-zones__copy">
                  <h3>{zone.name}</h3>
                  <small>{zone.id}</small>
                </div>
                <div className="admin-zones__badges">
                  {zone.isDefault&&<span className="admin-zones__badge">Default</span>}
                  <span className="admin-zones__badge">{state}</span>
                </div>
              </div>

              <div className="admin-zones__facts">
                <div className="admin-zones__fact"><span>Provider-location</span><strong>{zone.providerLocation}</strong></div>
                <div className="admin-zones__fact"><span>Vanaf</span><strong>{formatDateTime(zone.validFrom)}</strong></div>
                <div className="admin-zones__fact"><span>Tot</span><strong>{formatDateTime(zone.validUntil)}</strong></div>
              </div>

              {!zone.validUntil&&<div className="admin-zones__actions">
                <Button variant="secondary" onClick={()=>{
                  setClosingZoneId(current=>current===zone.id?undefined:zone.id);
                  setCloseAt(localDateTimeInput(new Date(Date.now()+24*60*60*1000)));
                }}>Zone afsluiten</Button>
              </div>}

              {closingZoneId===zone.id&&<div className="admin-zones__close">
                <label className="admin-zones__field">
                  <span>Afsluiten op</span>
                  <input type="datetime-local" value={closeAt} onChange={event=>setCloseAt(event.target.value)}/>
                </label>
                <Button onClick={()=>void closeZone(zone.id)} disabled={saving}>{saving?"Opslaan…":"Afsluiten plannen"}</Button>
              </div>}
            </article>;
          })}
    </div>
  </div>;
}
