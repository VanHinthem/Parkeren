import { useEffect,useState } from "react";
import {
  getAdminSystemSettings,
  setAdminDefaultPolicy,
  setAdminGlobalMaxConcurrentVisits,
  setAdminWarningSettings,
  type AdminDefaultPolicyField,
  type AdminSystemSettings
} from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import { formatAdminDateTime } from "./adminFieldFormatters";
import "./adminFieldPresentation.css";
import "./AdminSystem.css";

function hours(minutes:number|null,fallback:number){
  return String((minutes??fallback)/60);
}

function minutes(value:string){
  const numeric=Number(value);
  if(!Number.isFinite(numeric)||numeric<=0)return null;
  return Math.round(numeric*60);
}

const fieldLabels:Record<AdminDefaultPolicyField,string>={
  MaxPaidParkingDuration:"Max. betaalde parkeertijd",
  MaxVisitElapsedDuration:"Max. totale Visitduur",
  AllowVisitExtension:"Visit verlengen",
  AllowOpenEndedVisits:"Open einde toestaan",
  MaxConcurrentVisits:"Max. gelijktijdige Visits"
};

export function AdminSystemPage(){
  const[settings,setSettings]=useState<AdminSystemSettings>();
  const[error,setError]=useState<string>();
  const[message,setMessage]=useState<string>();
  const[conflict,setConflict]=useState<{count:number;fields:AdminDefaultPolicyField[]}>();
  const[savingPolicy,setSavingPolicy]=useState(false);
  const[savingCapacity,setSavingCapacity]=useState(false);
  const[savingWarnings,setSavingWarnings]=useState(false);

  const[paidUnlimited,setPaidUnlimited]=useState(false);
  const[paidHours,setPaidHours]=useState("4");
  const[elapsedUnlimited,setElapsedUnlimited]=useState(false);
  const[elapsedHours,setElapsedHours]=useState("8");
  const[allowExtension,setAllowExtension]=useState(true);
  const[allowOpenEnded,setAllowOpenEnded]=useState(false);
  const[defaultConcurrency,setDefaultConcurrency]=useState("1");
  const[globalConcurrency,setGlobalConcurrency]=useState("5");

  const[longWarningEnabled,setLongWarningEnabled]=useState(false);
  const[longWarningHours,setLongWarningHours]=useState("6");
  const[notifyAdmin,setNotifyAdmin]=useState(true);
  const[reminderEnabled,setReminderEnabled]=useState(false);
  const[reminderHours,setReminderHours]=useState("2");
  const[thresholds,setThresholds]=useState("80, 90, 100");

  function apply(value:AdminSystemSettings){
    setSettings(value);
    setPaidUnlimited(value.defaultPolicy.maxPaidParkingDurationMinutes===null);
    setPaidHours(hours(value.defaultPolicy.maxPaidParkingDurationMinutes,240));
    setElapsedUnlimited(value.defaultPolicy.maxVisitElapsedDurationMinutes===null);
    setElapsedHours(hours(value.defaultPolicy.maxVisitElapsedDurationMinutes,480));
    setAllowExtension(value.defaultPolicy.allowVisitExtension);
    setAllowOpenEnded(value.defaultPolicy.allowOpenEndedVisits);
    setDefaultConcurrency(String(value.defaultPolicy.maxConcurrentVisits));
    setGlobalConcurrency(String(value.globalMaxConcurrentVisits));
    setLongWarningEnabled(value.longVisitWarningAfterMinutes!==null);
    setLongWarningHours(hours(value.longVisitWarningAfterMinutes,360));
    setNotifyAdmin(value.notifyAdminOnLongVisit);
    setReminderEnabled(value.longVisitReminderIntervalMinutes!==null);
    setReminderHours(hours(value.longVisitReminderIntervalMinutes,120));
    setThresholds(value.budgetWarningThresholdPercentages.join(", "));
  }

  async function load(){
    setError(undefined);
    try{
      apply(await getAdminSystemSettings());
    }catch(e){
      setError(e instanceof Error?e.message:"Systeeminstellingen konden niet worden geladen.");
    }
  }

  useEffect(()=>{void load();},[]);

  async function savePolicy(){
    if(!settings)return;
    const paid=paidUnlimited?null:minutes(paidHours);
    const elapsed=elapsedUnlimited?null:minutes(elapsedHours);
    const concurrent=Number(defaultConcurrency);

    if((!paidUnlimited&&paid===null)||
       (!elapsedUnlimited&&elapsed===null)||
       !Number.isInteger(concurrent)||
       concurrent<=0||
       concurrent>settings.globalMaxConcurrentVisits){
      setError("Controleer de waarden van het standaardbeleid.");
      return;
    }

    setSavingPolicy(true);
    setError(undefined);
    setMessage(undefined);
    setConflict(undefined);
    try{
      const result=await setAdminDefaultPolicy({
        maxPaidParkingDurationMinutes:paid,
        maxVisitElapsedDurationMinutes:elapsed,
        allowVisitExtension:allowExtension,
        allowOpenEndedVisits:allowOpenEnded,
        maxConcurrentVisits:concurrent
      });
      if(result.outcome==="ActiveVisitConflict"){
        setConflict({count:result.affectedActiveVisitCount,fields:result.blockedFields});
        return;
      }
      if(result.outcome!=="Updated"){
        setError("Het standaardbeleid bevat ongeldige waarden.");
        return;
      }
      setMessage("Standaardbeleid is opgeslagen.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Standaardbeleid kon niet worden opgeslagen.");
    }finally{
      setSavingPolicy(false);
    }
  }

  async function saveCapacity(){
    const value=Number(globalConcurrency);
    if(!Number.isInteger(value)||value<=0){
      setError("De globale capaciteit moet minimaal 1 zijn.");
      return;
    }

    setSavingCapacity(true);
    setError(undefined);
    setMessage(undefined);
    try{
      await setAdminGlobalMaxConcurrentVisits(value);
      setMessage("Globale capaciteit is opgeslagen.");
      await load();
    }catch(e){
      setError(e instanceof Error?e.message:"Globale capaciteit kon niet worden opgeslagen.");
    }finally{
      setSavingCapacity(false);
    }
  }

  async function saveWarnings(){
    const warningMinutes=longWarningEnabled?minutes(longWarningHours):null;
    const reminderMinutes=reminderEnabled?minutes(reminderHours):null;
    const thresholdParts=thresholds.split(",").map(value=>value.trim());
    const values=thresholdParts.map(value=>Number(value));

    if((longWarningEnabled&&warningMinutes===null)||
       (reminderEnabled&&reminderMinutes===null)||
       thresholdParts.length===0||
       thresholdParts.some(value=>value.length===0)||
       values.some(value=>!Number.isFinite(value)||!Number.isInteger(value)||value<=0||value>100)||
       new Set(values).size!==values.length){
      setError("Controleer de waarschuwingsinstellingen en percentages.");
      return;
    }

    setSavingWarnings(true);
    setError(undefined);
    setMessage(undefined);
    try{
      const result=await setAdminWarningSettings({
        longVisitWarningAfterMinutes:warningMinutes,
        notifyAdminOnLongVisit:notifyAdmin,
        longVisitReminderIntervalMinutes:reminderMinutes,
        budgetWarningThresholdPercentages:values
      });
      apply(result.settings);
      setMessage("Waarschuwingsinstellingen zijn opgeslagen.");
    }catch(e){
      setError(e instanceof Error?e.message:"Waarschuwingsinstellingen konden niet worden opgeslagen.");
    }finally{
      setSavingWarnings(false);
    }
  }

  if(!settings&&!error)return <Loading label="Systeeminstellingen laden"/>;
  if(!settings)return <div className="admin-system">
    <Alert tone="danger">{error??"Systeeminstellingen konden niet worden geladen."}</Alert>
    <div className="admin-action-group admin-action-group--start">
      <Button className="admin-action--compact" onClick={()=>void load()}>Opnieuw proberen</Button>
    </div>
  </div>;

  return <div className="admin-system">
    <nav className="admin-subnav" aria-label="Systeem">
      <a className="admin-subnav__link active" href="/beheer/systeem">Instellingen</a>
      <a className="admin-subnav__link" href="/beheer/systeem/audit">Audit</a>
      <a className="admin-subnav__link" href="/beheer/systeem/diagnostiek">Diagnostiek</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}
    {message&&<Alert>{message}</Alert>}
    {conflict&&<Alert tone="warning">
      Deze wijziging raakt {conflict.count} actieve Visit(s) die één of meer gewijzigde defaultvelden erven.
      <ul className="admin-system__conflict-list">
        {conflict.fields.map(field=><li key={field}>{fieldLabels[field]}</li>)}
      </ul>
      Beëindig de relevante Visits of laat die gebruikers voor deze velden een eigen override gebruiken.
    </Alert>}

    <div className="admin-system__columns">
      <div className="admin-system__column">
        <section className="admin-system__panel">
          <h2>Standaard parkeerbeleid</h2>
          <p>Basisbeleid voor gebruikers zonder individuele override. Wijzigingen werken direct door naar gebruikers die het betreffende veld erven.</p>
          <div className="admin-settings-form">
            <div className="admin-setting-row">
              <div className="admin-setting-copy"><strong>Max. betaalde parkeertijd</strong><small>Totale betaalde tijd binnen één Visit.</small></div>
              <div className="admin-setting-controls">
                <label className="admin-field"><span>Uren</span><input className="admin-field__control--number" type="number" min=".25" step=".25" value={paidHours} onChange={event=>setPaidHours(event.target.value)} disabled={paidUnlimited}/></label>
                <label className="admin-check"><input type="checkbox" checked={paidUnlimited} onChange={event=>setPaidUnlimited(event.target.checked)}/> Onbeperkt</label>
              </div>
            </div>

            <div className="admin-setting-row">
              <div className="admin-setting-copy"><strong>Max. totale Visitduur</strong><small>Verstreken tijd vanaf de oorspronkelijke start.</small></div>
              <div className="admin-setting-controls">
                <label className="admin-field"><span>Uren</span><input className="admin-field__control--number" type="number" min=".25" step=".25" value={elapsedHours} onChange={event=>setElapsedHours(event.target.value)} disabled={elapsedUnlimited}/></label>
                <label className="admin-check"><input type="checkbox" checked={elapsedUnlimited} onChange={event=>setElapsedUnlimited(event.target.checked)}/> Onbeperkt</label>
              </div>
            </div>

            <div className="admin-setting-row">
              <div className="admin-setting-copy"><strong>Visit verlengen</strong><small>Mag een gebruiker de eindtijd van een actieve Visit naar later wijzigen?</small></div>
              <label className="admin-check"><input type="checkbox" checked={allowExtension} onChange={event=>setAllowExtension(event.target.checked)}/> Toestaan</label>
            </div>

            <div className="admin-setting-row">
              <div className="admin-setting-copy"><strong>Open einde toestaan</strong><small>Mag een Visit zonder vooraf gekozen eindtijd worden gestart?</small></div>
              <label className="admin-check"><input type="checkbox" checked={allowOpenEnded} onChange={event=>setAllowOpenEnded(event.target.checked)}/> Toestaan</label>
            </div>

            <div className="admin-setting-row">
              <div className="admin-setting-copy"><strong>Max. Visits per gebruiker</strong><small>Default voor gebruikers zonder concurrency-override; maximaal de globale capaciteit.</small></div>
              <label className="admin-field"><span>Aantal</span><input className="admin-field__control--number" type="number" min="1" max={settings.globalMaxConcurrentVisits} step="1" value={defaultConcurrency} onChange={event=>setDefaultConcurrency(event.target.value)}/></label>
            </div>

            <div className="admin-action-group admin-action-group--start">
              <Button className="admin-action--compact" onClick={()=>void savePolicy()} disabled={savingPolicy}>{savingPolicy?"Opslaan…":"Standaardbeleid opslaan"}</Button>
              <Button className="admin-action--compact" variant="secondary" onClick={()=>apply(settings)} disabled={savingPolicy}>Wijzigingen terugzetten</Button>
            </div>
          </div>
          <div className="admin-meta admin-system__meta">Laatst gewijzigd: {formatAdminDateTime(settings.defaultPolicyUpdatedAt)}</div>
        </section>

        <section className="admin-system__panel">
          <h2>Globale capaciteit</h2>
          <p>Maximaal aantal gelijktijdige Visits in de applicatie/providercontext. Wijzigen is alleen toegestaan zonder actieve Visits.</p>
          <div className="admin-settings-form">
            <label className="admin-field"><span>Max. gelijktijdige Visits</span><input className="admin-field__control--number" type="number" min="1" step="1" value={globalConcurrency} onChange={event=>setGlobalConcurrency(event.target.value)}/></label>
            <div className="admin-action-group admin-action-group--start"><Button className="admin-action--compact" onClick={()=>void saveCapacity()} disabled={savingCapacity}>{savingCapacity?"Opslaan…":"Capaciteit opslaan"}</Button></div>
          </div>
          <div className="admin-meta admin-system__meta">Verlagen klemt hogere user-overrides én de default concurrency automatisch naar het nieuwe globale maximum.</div>
        </section>
      </div>

      <div className="admin-system__column">
        <section className="admin-system__panel">
          <h2>Long Visit waarschuwingen</h2>
          <p>Configureer wanneer een langdurige Visit een melding veroorzaakt en of beheerders die melding ook ontvangen.</p>
          <div className="admin-settings-form">
            <div className="admin-setting-row">
              <div className="admin-setting-copy"><strong>Waarschuwing</strong><small>Stuur een melding zodra een Visit langer duurt dan de ingestelde grens.</small></div>
              <div className="admin-setting-controls">
                <label className="admin-check"><input type="checkbox" checked={longWarningEnabled} onChange={event=>setLongWarningEnabled(event.target.checked)}/> Inschakelen</label>
                <label className="admin-field"><span>Na aantal uren</span><input className="admin-field__control--number" type="number" min=".25" step=".25" value={longWarningHours} onChange={event=>setLongWarningHours(event.target.value)} disabled={!longWarningEnabled}/></label>
              </div>
            </div>
            <div className="admin-setting-row">
              <div className="admin-setting-copy"><strong>Beheerder notificeren</strong><small>Naast de bezoeker krijgen actieve beheerders de long-Visit melding.</small></div>
              <label className="admin-check"><input type="checkbox" checked={notifyAdmin} onChange={event=>setNotifyAdmin(event.target.checked)}/> Beheerders informeren</label>
            </div>
            <div className="admin-setting-row">
              <div className="admin-setting-copy"><strong>Herinnering</strong><small>Herhaal de melding zolang de Visit actief blijft.</small></div>
              <div className="admin-setting-controls">
                <label className="admin-check"><input type="checkbox" checked={reminderEnabled} onChange={event=>setReminderEnabled(event.target.checked)}/> Inschakelen</label>
                <label className="admin-field"><span>Iedere aantal uren</span><input className="admin-field__control--number" type="number" min=".25" step=".25" value={reminderHours} onChange={event=>setReminderHours(event.target.value)} disabled={!reminderEnabled}/></label>
              </div>
            </div>
            <div className="admin-action-group admin-action-group--start">
              <Button className="admin-action--compact" onClick={()=>void saveWarnings()} disabled={savingWarnings}>{savingWarnings?"Opslaan…":"Waarschuwingen opslaan"}</Button>
            </div>
          </div>
        </section>

        <section className="admin-system__panel">
          <h2>Budgetwaarschuwingen</h2>
          <p>Drempelpercentages waarop een budgetwaarschuwing éénmalig per budgetperiode wordt uitgegeven.</p>
          <div className="admin-settings-form">
            <label className="admin-field"><span>Percentages, komma-gescheiden</span><input value={thresholds} onChange={event=>setThresholds(event.target.value)} placeholder="80, 90, 100"/></label>
            <div className="admin-system__thresholds">{settings.budgetWarningThresholdPercentages.map(value=><span className="admin-tag" key={value}>{value}%</span>)}</div>
            <div className="admin-action-group admin-action-group--start"><Button className="admin-action--compact" onClick={()=>void saveWarnings()} disabled={savingWarnings}>{savingWarnings?"Opslaan…":"Waarschuwingen opslaan"}</Button></div>
          </div>
          <div className="admin-meta admin-system__meta">Systeeminstellingen laatst gewijzigd: {formatAdminDateTime(settings.systemSettingsUpdatedAt)}</div>
        </section>
      </div>
    </div>
  </div>;
}
