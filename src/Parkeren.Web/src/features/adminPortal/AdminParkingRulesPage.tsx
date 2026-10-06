import { useEffect,useState } from "react";
import {
  createAdminParkingRuleSetVersion,
  getAdminParkingRuleSets,
  getAdminProviderProducts,
  type AdminParkingRuleSetVersion,
  type AdminProviderProduct,
  type DayOfWeekName
} from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import "./adminFieldPresentation.css";
import "./AdminParkingRules.css";

type WindowRow={key:number;day:DayOfWeekName;start:string;end:string};
type ExceptionRow={key:number;date:string;isPaid:boolean};

let nextKey=1;
const days:DayOfWeekName[]=["Monday","Tuesday","Wednesday","Thursday","Friday","Saturday","Sunday"];
const dayLabels:Record<DayOfWeekName,string>={
  Monday:"Maandag",
  Tuesday:"Dinsdag",
  Wednesday:"Woensdag",
  Thursday:"Donderdag",
  Friday:"Vrijdag",
  Saturday:"Zaterdag",
  Sunday:"Zondag"
};

function localInputValue(date:Date){
  const pad=(value:number)=>String(value).padStart(2,"0");
  return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function nextVersionDate(latest:AdminParkingRuleSetVersion|undefined){
  const now=new Date();
  const latestFrom=latest?new Date(latest.validFrom):now;
  const base=latestFrom>now?latestFrom:now;
  const next=new Date(base);
  next.setDate(next.getDate()+1);
  next.setHours(0,0,0,0);
  return localInputValue(next);
}

function formatDateTime(value:string|null){
  return value
    ? new Date(value).toLocaleString("nl-NL",{dateStyle:"medium",timeStyle:"short"})
    : "doorlopend";
}

function formatDuration(minutes:number){
  return minutes%60===0?`${minutes/60} uur`:`${minutes} min`;
}

function versionState(version:AdminParkingRuleSetVersion){
  const now=Date.now();
  const from=new Date(version.validFrom).getTime();
  const until=version.validUntil?new Date(version.validUntil).getTime():null;
  if(now<from)return "Gepland";
  if(until!==null&&now>=until)return "Historisch";
  return "Actief";
}

function toEditorWindows(version:AdminParkingRuleSetVersion|undefined):WindowRow[]{
  if(!version)return days.slice(0,6).map(day=>({key:nextKey++,day,start:"09:00",end:"20:00"}));
  return version.paidWindows.map(window=>({
    key:nextKey++,
    day:window.day,
    start:window.start.slice(0,5),
    end:window.end.slice(0,5)
  }));
}

function toEditorExceptions(version:AdminParkingRuleSetVersion|undefined):ExceptionRow[]{
  if(!version)return [];
  const today=localInputValue(new Date()).slice(0,10);
  return version.calendarExceptions
    .filter(item=>item.date>=today)
    .map(item=>({key:nextKey++,date:item.date,isPaid:item.isPaid}));
}

export function AdminParkingRulesPage(){
  const[products,setProducts]=useState<AdminProviderProduct[]>();
  const[selectedProductId,setSelectedProductId]=useState<string>();
  const[versions,setVersions]=useState<AdminParkingRuleSetVersion[]>();
  const[error,setError]=useState<string>();
  const[message,setMessage]=useState<string>();
  const[saving,setSaving]=useState(false);

  const[validFrom,setValidFrom]=useState("");
  const[maxActionHours,setMaxActionHours]=useState("4");
  const[continuation,setContinuation]=useState<"ExtendAction"|"StartNewAction">("StartNewAction");
  const[publicHolidaysFree,setPublicHolidaysFree]=useState(true);
  const[windows,setWindows]=useState<WindowRow[]>([]);
  const[exceptions,setExceptions]=useState<ExceptionRow[]>([]);

  function applyLatest(rows:AdminParkingRuleSetVersion[]){
    const latest=rows[0];
    setValidFrom(nextVersionDate(latest));
    setMaxActionHours(String((latest?.maxProviderActionDurationMinutes??240)/60));
    setContinuation(latest?.continuation??"StartNewAction");
    setPublicHolidaysFree(latest?.publicHolidaysAreFree??true);
    setWindows(toEditorWindows(latest));
    setExceptions(toEditorExceptions(latest));
  }

  async function load(productId:string,resetEditor:boolean){
    setError(undefined);
    try{
      const rows=await getAdminParkingRuleSets(productId);
      setVersions(rows);
      if(resetEditor)applyLatest(rows);
    }catch(e){
      setError(e instanceof Error?e.message:"Parkeerregels konden niet worden geladen.");
    }
  }

  useEffect(()=>{
    void (async()=>{
      try{
        const rows=await getAdminProviderProducts();
        setProducts(rows);
        const selected=rows.find(product=>product.isDefault)?.id
          ?? rows.find(product=>product.isAvailable)?.id
          ?? rows[0]?.id;
        setSelectedProductId(selected);
        if(!selected)setVersions([]);
      }catch(e){
        setError(e instanceof Error?e.message:"Providerproducten konden niet worden geladen.");
      }
    })();
  },[]);

  useEffect(()=>{
    if(selectedProductId)void load(selectedProductId,true);
  },[selectedProductId]);

  async function save(){
    const selectedProduct=products?.find(product=>product.id===selectedProductId);
    if(!selectedProductId||!selectedProduct){
      setError("Selecteer eerst een 2Park-product.");
      return;
    }
    if(!selectedProduct.isAvailable){
      setError("Dit providerproduct is niet meer beschikbaar; historische configuratie blijft alleen-lezen.");
      return;
    }

    const startAt=new Date(validFrom);
    const hours=Number(maxActionHours);
    if(!validFrom||Number.isNaN(startAt.getTime())||startAt.getTime()<=Date.now()){
      setError("De nieuwe ruleset moet een toekomstige ingangsdatum hebben.");
      return;
    }
    if(!Number.isFinite(hours)||hours<=0){
      setError("De maximale provider-actieduur moet groter dan 0 zijn.");
      return;
    }
    if(windows.some(window=>!window.start||!window.end||window.end<=window.start)){
      setError("Controleer de betaalvensters: iedere eindtijd moet na de starttijd liggen.");
      return;
    }
    if(exceptions.some(item=>!item.date)){
      setError("Iedere kalenderuitzondering moet een datum hebben.");
      return;
    }

    setSaving(true);
    setError(undefined);
    setMessage(undefined);
    try{
      const result=await createAdminParkingRuleSetVersion({
        providerProductId:selectedProductId,
        validFrom:startAt.toISOString(),
        maxProviderActionDurationMinutes:Math.round(hours*60),
        continuation,
        publicHolidaysAreFree:publicHolidaysFree,
        paidWindows:windows.map(window=>({
          day:window.day,
          start:`${window.start}:00`,
          end:`${window.end}:00`
        })),
        calendarExceptions:exceptions.map(item=>({date:item.date,isPaid:item.isPaid}))
      });

      if(result.outcome==="Created"){
        setMessage("Nieuwe parkeerregelversie is gepland; de vorige versie wordt op de ingangsdatum afgesloten.");
        await load(selectedProductId,true);
        return;
      }
      if(result.outcome==="MustBeFuture"){
        setError("De ingangsdatum moet in de toekomst liggen.");
      }else if(result.outcome==="SequenceConflict"){
        setError("Er is inmiddels een nieuwere rulesetversie. Vernieuw de pagina en plan de versie opnieuw.");
        await load(selectedProductId,true);
      }else{
        setError("De parkeerregels zijn ongeldig. Controleer overlappende vensters, dubbele uitzonderingen en de provider-actieduur.");
      }
    }catch(e){
      setError(e instanceof Error?e.message:"De nieuwe parkeerregelversie kon niet worden opgeslagen.");
    }finally{
      setSaving(false);
    }
  }

  if((!products||!versions)&&!error)return <Loading label="Parkeerregels laden"/>;
  if(!products||!versions)return <div className="admin-rules">
    <Alert tone="danger">{error??"Parkeerregels of providerproducten konden niet worden geladen."}</Alert>
  </div>;

  return <div className="admin-rules">
    <nav className="admin-subnav" aria-label="Parkeerconfiguratie">
      <a className="admin-subnav__link" href="/beheer/provider">Providerproducten</a>
      <a className="admin-subnav__link active" href="/beheer/configuratie/parkeerregels">Parkeerregels</a>
      <a className="admin-subnav__link" href="/beheer/configuratie/tarieven">Tarieven</a>
      <a className="admin-subnav__link" href="/beheer/configuratie/budgetten">Budgetten</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}
    {message&&<Alert>{message}</Alert>}

    <section className="admin-rules__panel">
      <h2>2Park-product</h2>
      {products.length===0
        ? <p>Er zijn nog geen providerproducten gesynchroniseerd. Synchroniseer ze eerst onder <a href="/beheer/provider">Provider & reconciliatie</a>.</p>
        : <div className="admin-rules__grid">
            <label className="admin-field">
              <span>Configuratie voor</span>
              <select value={selectedProductId??""} onChange={event=>setSelectedProductId(event.target.value)}>
                {products.map(product=><option key={product.id} value={product.id}>
                  {product.name}{product.isDefault?" · default":""}{product.isAvailable?"":" · niet beschikbaar"}
                </option>)}
              </select>
            </label>
            {selectedProductId&&<div className="admin-readonly-value admin-readonly-value--field">
              <span>Providercontext</span>
              <strong>{products.find(product=>product.id===selectedProductId)?.location??"—"}</strong>
            </div>}
          </div>}
    </section>

    <section className="admin-rules__panel">
      <h2>Nieuwe rulesetversie plannen</h2>
      <p>Historische regels worden niet gewijzigd. De huidige laatste versie wordt automatisch afgesloten op de ingangsdatum van deze nieuwe versie.</p>

      <div className="admin-rules__form">
        <div className="admin-rules__grid">
          <label className="admin-field">
            <span>Geldig vanaf</span>
            <input type="datetime-local" value={validFrom} onChange={event=>setValidFrom(event.target.value)}/>
          </label>
          <label className="admin-field">
            <span>Max. provider-actieduur (uren)</span>
            <input className="admin-field__control--number" type="number" min=".25" step=".25" value={maxActionHours} onChange={event=>setMaxActionHours(event.target.value)}/>
          </label>
          <label className="admin-field">
            <span>Continuation</span>
            <select value={continuation} onChange={event=>setContinuation(event.target.value as "ExtendAction"|"StartNewAction")}>
              <option value="StartNewAction">Nieuwe aansluitende provideractie</option>
              <option value="ExtendAction">Bestaande provideractie verlengen</option>
            </select>
          </label>
          <label className="admin-check">
            <input type="checkbox" checked={publicHolidaysFree} onChange={event=>setPublicHolidaysFree(event.target.checked)}/>
            <span>Nederlandse feestdagen gratis</span>
          </label>
        </div>

        <div className="admin-rules__subsection admin-rules__subsection--windows">
          <div className="admin-rules__subsection-head">
            <h3>Betaalvensters</h3>
            <Button className="admin-action--compact" variant="secondary" onClick={()=>setWindows(rows=>[...rows,{key:nextKey++,day:"Monday",start:"09:00",end:"20:00"}])}>Venster toevoegen</Button>
          </div>
          {windows.length===0
            ? <p className="admin-rules__empty">Geen betaalvensters: alle reguliere tijden zijn gratis.</p>
            : <div className="admin-table-wrap">
                <table className="admin-table admin-table--fixed admin-rules__window-table">
                  <thead><tr><th>Dag</th><th>Start</th><th>Einde</th><th aria-label="Acties"/></tr></thead>
                  <tbody>
                    {windows.map(row=><tr key={row.key}>
                      <td><select className="admin-table__control" aria-label="Dag" value={row.day} onChange={event=>setWindows(rows=>rows.map(item=>item.key===row.key?{...item,day:event.target.value as DayOfWeekName}:item))}>{days.map(day=><option key={day} value={day}>{dayLabels[day]}</option>)}</select></td>
                      <td><input className="admin-table__control" aria-label="Start" type="time" value={row.start} onChange={event=>setWindows(rows=>rows.map(item=>item.key===row.key?{...item,start:event.target.value}:item))}/></td>
                      <td><input className="admin-table__control" aria-label="Einde" type="time" value={row.end} onChange={event=>setWindows(rows=>rows.map(item=>item.key===row.key?{...item,end:event.target.value}:item))}/></td>
                      <td className="admin-table__actions"><Button className="admin-action--compact" variant="danger" type="button" onClick={()=>setWindows(rows=>rows.filter(item=>item.key!==row.key))}>Verwijderen</Button></td>
                    </tr>)}
                  </tbody>
                </table>
              </div>}
        </div>

        <div className="admin-rules__subsection">
          <div className="admin-rules__subsection-head">
            <h3>Kalenderuitzonderingen</h3>
            <Button className="admin-action--compact" variant="secondary" onClick={()=>setExceptions(rows=>[...rows,{key:nextKey++,date:"",isPaid:false}])}>Uitzondering toevoegen</Button>
          </div>
          {exceptions.length===0
            ? <p className="admin-rules__empty">Geen expliciete kalenderuitzonderingen in deze versie.</p>
            : <div className="admin-table-wrap">
                <table className="admin-table admin-table--fixed admin-rules__exception-table">
                  <thead><tr><th>Datum</th><th>Gedrag</th><th aria-label="Acties"/></tr></thead>
                  <tbody>
                    {exceptions.map(row=><tr key={row.key}>
                      <td><input className="admin-table__control" aria-label="Datum" type="date" value={row.date} onChange={event=>setExceptions(rows=>rows.map(item=>item.key===row.key?{...item,date:event.target.value}:item))}/></td>
                      <td><select className="admin-table__control" aria-label="Gedrag" value={row.isPaid?"paid":"free"} onChange={event=>setExceptions(rows=>rows.map(item=>item.key===row.key?{...item,isPaid:event.target.value==="paid"}:item))}>
                        <option value="free">Gratis</option>
                        <option value="paid">Betaald</option>
                      </select></td>
                      <td className="admin-table__actions"><Button className="admin-action--compact" variant="danger" type="button" onClick={()=>setExceptions(rows=>rows.filter(item=>item.key!==row.key))}>Verwijderen</Button></td>
                    </tr>)}
                  </tbody>
                </table>
              </div>}
        </div>

        <div className="admin-rules__actions">
          <Button onClick={()=>void save()} disabled={saving||!products.find(product=>product.id===selectedProductId)?.isAvailable}>{saving?"Opslaan…":"Nieuwe versie plannen"}</Button>
          <Button variant="secondary" onClick={()=>applyLatest(versions)} disabled={saving}>Terug naar laatste versie</Button>
        </div>
      </div>
    </section>

    <section className="admin-rules__versions">
      {versions.map(version=>{
        const state=versionState(version);
        return <article className="admin-rules__version" key={version.id}>
          <div className="admin-rules__version-head">
            <div className="admin-rules__version-copy">
              <h3>{formatDateTime(version.validFrom)} → {formatDateTime(version.validUntil)}</h3>
              <small>{version.id}</small>
            </div>
            <span className={"admin-rules__badge "+(state==="Actief"?"admin-rules__badge--active":"")}>{state}</span>
          </div>

          <div className="admin-rules__facts">
            <div className="admin-rules__fact"><span>Provideractie</span><strong>{formatDuration(version.maxProviderActionDurationMinutes)}</strong></div>
            <div className="admin-rules__fact"><span>Continuation</span><strong>{version.continuation==="StartNewAction"?"Nieuwe actie":"Verlengen"}</strong></div>
            <div className="admin-rules__fact"><span>Feestdagen</span><strong>{version.publicHolidaysAreFree?"Gratis":"Volgens weekvensters"}</strong></div>
          </div>

          <div className="admin-rules__windows">
            {version.paidWindows.length===0
              ? <span className="admin-rules__window">Geen betaalvensters</span>
              : version.paidWindows.map(window=><span className="admin-rules__window" key={window.id}>{dayLabels[window.day]} {window.start.slice(0,5)}–{window.end.slice(0,5)}</span>)}
          </div>

          {version.calendarExceptions.length>0&&<ul className="admin-rules__exceptions">
            {version.calendarExceptions.map(item=><li key={item.id}>{new Date(item.date+"T00:00:00").toLocaleDateString("nl-NL",{dateStyle:"medium"})}: {item.isPaid?"betaald":"gratis"}</li>)}
          </ul>}
        </article>;
      })}
    </section>
  </div>;
}
