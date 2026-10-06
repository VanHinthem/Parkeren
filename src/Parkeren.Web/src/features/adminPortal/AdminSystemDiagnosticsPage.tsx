import { useEffect,useState } from "react";
import {
  getAdminSystemDiagnostics,
  type AdminSystemDiagnostics
} from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Loading } from "../../design/primitives/Loading";
import {
  adminHealthStatusTone,
  adminStatusClass,
  formatAdminDateTime,
  formatAdminHealthStatus,
  formatAdminNumber,
  type AdminHealthStatus
} from "./adminFieldFormatters";
import "./adminFieldPresentation.css";
import "./AdminSystem.css";

function configuredHealth(configured:boolean):AdminHealthStatus{
  return configured?"Healthy":"Error";
}

function schedulerHealth(value:AdminSystemDiagnostics):AdminHealthStatus{
  if(value.scheduler.overdueCount>0)return "Error";
  if(value.scheduler.claimedCount>0)return "Warning";
  return "Healthy";
}

function pushHealth(value:AdminSystemDiagnostics):AdminHealthStatus{
  if(value.pushDeliveries.failedCount>0)return "Error";
  if(value.pushDeliveries.pendingCount>0)return "Warning";
  return "Healthy";
}

function HealthStatus({status}:{status:AdminHealthStatus}){
  return <span className={adminStatusClass(adminHealthStatusTone(status))}>{formatAdminHealthStatus(status)}</span>;
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

  const subnav=<nav className="admin-subnav" aria-label="Systeem">
    <a className="admin-subnav__link" href="/beheer/systeem">Instellingen</a>
    <a className="admin-subnav__link" href="/beheer/systeem/audit">Audit</a>
    <a className="admin-subnav__link active" href="/beheer/systeem/diagnostiek">Diagnostiek</a>
  </nav>;

  if(!diagnostics&&!error)return <Loading label="Systeemdiagnostiek laden"/>;

  if(!diagnostics)return <div className="admin-system">
    {subnav}
    <Alert tone="danger">{error??"Systeemdiagnostiek kon niet worden geladen."}</Alert>
    <div className="admin-action-group admin-action-group--start">
      <Button className="admin-action--compact" onClick={()=>void load()}>Opnieuw proberen</Button>
    </div>
  </div>;

  const schedulerStatus=schedulerHealth(diagnostics);
  const pushStatus=pushHealth(diagnostics);

  return <div className="admin-system">
    {subnav}
    {error&&<Alert tone="danger">{error}</Alert>}

    <div className="admin-system__diagnostics-toolbar">
      <span className="admin-meta">Geobserveerd: {formatAdminDateTime(diagnostics.observedAt)}</span>
      <Button className="admin-action--compact" variant="secondary" onClick={()=>void load()}>Vernieuwen</Button>
    </div>

    <div className="admin-system__columns">
      <div className="admin-system__column">
        <section className="admin-system__panel">
          <div className="admin-system__panel-heading">
            <h2>Database</h2>
            <HealthStatus status={diagnostics.database.healthy?"Healthy":"Error"}/>
          </div>
          <p>Bereikbaarheid van de primaire applicatiedatabase.</p>
          <dl className="admin-facts admin-system__diagnostic-facts">
            <div className="admin-fact"><dt>Status</dt><dd>{diagnostics.database.status}</dd></div>
          </dl>
        </section>

        <section className="admin-system__panel">
          <div className="admin-system__panel-heading">
            <h2>Web Push</h2>
            <HealthStatus status={configuredHealth(diagnostics.webPush.configured)}/>
          </div>
          <p>Controle of de vereiste Web Push-instellingen aanwezig zijn.</p>
          <dl className="admin-facts admin-system__diagnostic-facts">
            <div className="admin-fact"><dt>Status</dt><dd>{diagnostics.webPush.status}</dd></div>
          </dl>
        </section>

        <section className="admin-system__panel">
          <div className="admin-system__panel-heading">
            <h2>Push delivery</h2>
            <HealthStatus status={pushStatus}/>
          </div>
          <p>Persistente pushwerkvoorraad. De inbox blijft de betrouwbare bron voor meldingen.</p>
          <dl className="admin-facts admin-system__diagnostic-facts">
            <div className="admin-fact"><dt>Pending</dt><dd className="admin-number">{formatAdminNumber(diagnostics.pushDeliveries.pendingCount)}</dd></div>
            <div className="admin-fact"><dt>Failed</dt><dd className="admin-number">{formatAdminNumber(diagnostics.pushDeliveries.failedCount)}</dd></div>
            <div className="admin-fact"><dt>Oudste pending</dt><dd>{formatAdminDateTime(diagnostics.pushDeliveries.oldestPendingCreatedAt)}</dd></div>
            <div className="admin-fact"><dt>Laatste poging</dt><dd>{formatAdminDateTime(diagnostics.pushDeliveries.lastAttemptAt)}</dd></div>
            <div className="admin-fact"><dt>Laatste delivery</dt><dd>{formatAdminDateTime(diagnostics.pushDeliveries.lastDeliveredAt)}</dd></div>
          </dl>
        </section>
      </div>

      <div className="admin-system__column">
        <section className="admin-system__panel">
          <div className="admin-system__panel-heading">
            <h2>Provider</h2>
            <HealthStatus status={configuredHealth(diagnostics.provider.configured)}/>
          </div>
          <p>Veilige configuratiestatus van de parkeerprovider.</p>
          <dl className="admin-facts admin-system__diagnostic-facts">
            <div className="admin-fact"><dt>Configuratie</dt><dd>{diagnostics.provider.status}</dd></div>
          </dl>
        </section>

        <section className="admin-system__panel">
          <div className="admin-system__panel-heading">
            <h2>Scheduler</h2>
            <HealthStatus status={schedulerStatus}/>
          </div>
          <p>Werkvoorraad voor geplande Visit-acties en waarschuwingen.</p>
          <dl className="admin-facts admin-system__diagnostic-facts">
            <div className="admin-fact"><dt>Pending</dt><dd className="admin-number">{formatAdminNumber(diagnostics.scheduler.pendingCount)}</dd></div>
            <div className="admin-fact"><dt>Claimed</dt><dd className="admin-number">{formatAdminNumber(diagnostics.scheduler.claimedCount)}</dd></div>
            <div className="admin-fact"><dt>Overdue</dt><dd className="admin-number">{formatAdminNumber(diagnostics.scheduler.overdueCount)}</dd></div>
            <div className="admin-fact"><dt>Oudste pending</dt><dd>{formatAdminDateTime(diagnostics.scheduler.oldestPendingDueAt)}</dd></div>
            <div className="admin-fact"><dt>Oudste claim</dt><dd>{formatAdminDateTime(diagnostics.scheduler.oldestClaimedAt)}</dd></div>
            <div className="admin-fact"><dt>Laatste completion</dt><dd>{formatAdminDateTime(diagnostics.scheduler.lastCompletedAt)}</dd></div>
          </dl>
        </section>
      </div>
    </div>
  </div>;
}
