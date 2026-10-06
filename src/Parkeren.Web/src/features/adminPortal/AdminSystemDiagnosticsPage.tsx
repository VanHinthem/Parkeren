import { useEffect,useState } from "react";
import {
  getAdminSystemDiagnostics,
  type AdminSystemDiagnostics
} from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import "./AdminSystem.css";

type HealthTone="healthy"|"warning"|"error";

function formatDateTime(value:string|null){
  if(!value)return "—";
  return new Date(value).toLocaleString("nl-NL",{dateStyle:"short",timeStyle:"short"});
}

function toneClass(tone:HealthTone){
  return `admin-system__status admin-system__status--${tone}`;
}

function configuredTone(configured:boolean):HealthTone{
  return configured?"healthy":"error";
}

function schedulerTone(value:AdminSystemDiagnostics):HealthTone{
  if(value.scheduler.overdueCount>0)return "error";
  if(value.scheduler.claimedCount>0)return "warning";
  return "healthy";
}

function pushTone(value:AdminSystemDiagnostics):HealthTone{
  if(value.pushDeliveries.failedCount>0)return "error";
  if(value.pushDeliveries.pendingCount>0)return "warning";
  return "healthy";
}

export function AdminSystemDiagnosticsPage(){
  const[diagnostics,setDiagnostics]=useState<AdminSystemDiagnostics>();
  const[error,setError]=useState<string>();

  async function load(){
    setError(undefined);
    try{
      setDiagnostics(await getAdminSystemDiagnostics());
    }catch(e){
      setError(e instanceof Error?e.message:"Systeemdiagnostiek kon niet worden geladen.");
    }
  }

  useEffect(()=>{void load();},[]);

  if(!diagnostics&&!error)return <Loading label="Systeemdiagnostiek laden"/>;

  if(!diagnostics)return <div className="admin-system">
    <nav className="admin-subnav" aria-label="Systeem">
      <a className="admin-subnav__link" href="/beheer/systeem">Instellingen</a>
      <a className="admin-subnav__link" href="/beheer/systeem/audit">Audit</a>
      <a className="admin-subnav__link active" href="/beheer/systeem/diagnostiek">Diagnostiek</a>
    </nav>
    <Alert tone="danger">{error??"Systeemdiagnostiek kon niet worden geladen."}</Alert>
    <div className="admin-system__actions">
      <Button onClick={()=>void load()}>Opnieuw proberen</Button>
    </div>
  </div>;

  const schedulerHealth=schedulerTone(diagnostics);
  const pushHealth=pushTone(diagnostics);

  return <div className="admin-system">
    <nav className="admin-subnav" aria-label="Systeem">
      <a className="admin-subnav__link" href="/beheer/systeem">Instellingen</a>
      <a className="admin-subnav__link" href="/beheer/systeem/audit">Audit</a>
      <a className="admin-subnav__link active" href="/beheer/systeem/diagnostiek">Diagnostiek</a>
    </nav>

    {error&&<Alert tone="danger">{error}</Alert>}

    <div className="admin-system__actions">
      <Button variant="secondary" onClick={()=>void load()}>Vernieuwen</Button>
    </div>

    <div className="admin-system__grid">
      <section className="admin-system__panel">
        <div className="admin-system__panel-heading">
          <h2>Database</h2>
          <span className={toneClass(diagnostics.database.healthy?"healthy":"error")}>
            {diagnostics.database.healthy?"Healthy":"Error"}
          </span>
        </div>
        <p>Bereikbaarheid van de primaire applicatiedatabase.</p>
        <dl className="admin-system__diagnostics-list">
          <div><dt>Status</dt><dd>{diagnostics.database.status}</dd></div>
        </dl>
      </section>

      <section className="admin-system__panel">
        <div className="admin-system__panel-heading">
          <h2>Provider</h2>
          <span className={toneClass(configuredTone(diagnostics.provider.configured))}>
            {diagnostics.provider.configured?"Healthy":"Error"}
          </span>
        </div>
        <p>Veilige configuratiestatus van de parkeerprovider.</p>
        <dl className="admin-system__diagnostics-list">
          <div><dt>Configuratie</dt><dd>{diagnostics.provider.status}</dd></div>
        </dl>
      </section>

      <section className="admin-system__panel">
        <div className="admin-system__panel-heading">
          <h2>Web Push</h2>
          <span className={toneClass(configuredTone(diagnostics.webPush.configured))}>
            {diagnostics.webPush.configured?"Healthy":"Error"}
          </span>
        </div>
        <p>Controle of de vereiste Web Push-instellingen aanwezig zijn.</p>
        <dl className="admin-system__diagnostics-list">
          <div><dt>Status</dt><dd>{diagnostics.webPush.status}</dd></div>
        </dl>
      </section>

      <section className="admin-system__panel">
        <div className="admin-system__panel-heading">
          <h2>Scheduler</h2>
          <span className={toneClass(schedulerHealth)}>
            {schedulerHealth==="healthy"?"Healthy":schedulerHealth==="warning"?"Warning":"Error"}
          </span>
        </div>
        <p>Werkvoorraad voor geplande Visit-acties en waarschuwingen.</p>
        <dl className="admin-system__diagnostics-list">
          <div><dt>Pending</dt><dd>{diagnostics.scheduler.pendingCount}</dd></div>
          <div><dt>Claimed</dt><dd>{diagnostics.scheduler.claimedCount}</dd></div>
          <div><dt>Overdue</dt><dd>{diagnostics.scheduler.overdueCount}</dd></div>
          <div><dt>Oudste pending</dt><dd>{formatDateTime(diagnostics.scheduler.oldestPendingDueAt)}</dd></div>
          <div><dt>Oudste claim</dt><dd>{formatDateTime(diagnostics.scheduler.oldestClaimedAt)}</dd></div>
          <div><dt>Laatste completion</dt><dd>{formatDateTime(diagnostics.scheduler.lastCompletedAt)}</dd></div>
        </dl>
      </section>

      <section className="admin-system__panel">
        <div className="admin-system__panel-heading">
          <h2>Push delivery</h2>
          <span className={toneClass(pushHealth)}>
            {pushHealth==="healthy"?"Healthy":pushHealth==="warning"?"Warning":"Error"}
          </span>
        </div>
        <p>Persistente pushwerkvoorraad. De inbox blijft de betrouwbare bron voor meldingen.</p>
        <dl className="admin-system__diagnostics-list">
          <div><dt>Pending</dt><dd>{diagnostics.pushDeliveries.pendingCount}</dd></div>
          <div><dt>Failed</dt><dd>{diagnostics.pushDeliveries.failedCount}</dd></div>
          <div><dt>Oudste pending</dt><dd>{formatDateTime(diagnostics.pushDeliveries.oldestPendingCreatedAt)}</dd></div>
          <div><dt>Laatste poging</dt><dd>{formatDateTime(diagnostics.pushDeliveries.lastAttemptAt)}</dd></div>
          <div><dt>Laatste delivery</dt><dd>{formatDateTime(diagnostics.pushDeliveries.lastDeliveredAt)}</dd></div>
        </dl>
      </section>
    </div>

    <div className="admin-system__meta">
      Geobserveerd: {formatDateTime(diagnostics.observedAt)}
    </div>
  </div>;
}
