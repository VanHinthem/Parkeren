import { Fragment, useEffect, useState } from "react";
import { assignAdminProviderActionUser, getAdminProviderHistorySyncStatus, startAdminProviderHistorySync, cancelAdminProviderHistorySync, getAdminProviderAssignmentHistory, getAdminProviderActionHistory, getAdminProviderProducts, getUsers, type AdminProviderAssignmentAuditEntry, type UserSummary, type AdminProviderProduct, type AdminProviderActionHistoryPage, type AdminProviderHistorySyncStatus } from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { LicensePlate } from "../../components/LicensePlate";
import { Loading } from "../../design/primitives/Loading";
import { Button } from "../../design/primitives/Button";
import { formatAdminDateTime, formatAdminProviderActionStatus } from "./adminFieldFormatters";
import { AdminProviderSubnav } from "./AdminProviderSubnav";
import "./adminFieldPresentation.css";
import "./AdminProvider.css";

function syncRunStatusLabel(status:string) {
  switch (status) {
    case "Running": return "Bezig";
    case "Succeeded": return "Voltooid";
    case "Failed": return "Mislukt";
    case "Cancelled": return "Geannuleerd";
    default: return status;
  }
}

function assignmentChange(contextJson:string|null, users:UserSummary[]) {
  if (!contextJson) return "Toewijzing gewijzigd";
  try {
    const change = JSON.parse(contextJson) as {PreviousUserId?:string|null;NewUserId?:string|null};
    const name = (id:string|null|undefined) => id ? users.find(user => user.id === id)?.username ?? id : "Niet toegewezen";
    return `${name(change.PreviousUserId)} → ${name(change.NewUserId)}`;
  } catch { return "Toewijzing gewijzigd"; }
}

function dateBoundary(value: string, nextDay: boolean) {
  if (!value) return undefined;
  const boundary = new Date(value + "T00:00:00");
  if (nextDay) boundary.setDate(boundary.getDate() + 1);
  return boundary.toISOString();
}

export function AdminProviderHistoryPage() {
  const [data, setData] = useState<AdminProviderActionHistoryPage>();
  const [syncStatus, setSyncStatus] = useState<AdminProviderHistorySyncStatus>();
  const [syncProductId, setSyncProductId] = useState("");
  const [syncBusy, setSyncBusy] = useState(false);
  const [cancellingRunId, setCancellingRunId] = useState<string>();
  const [syncError, setSyncError] = useState("");
  const [expandedId, setExpandedId] = useState<string>();
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [selectedUsers, setSelectedUsers] = useState<Record<string,string>>({});
  const [savingId, setSavingId] = useState<string>();
  const [audit, setAudit] = useState<Record<string,AdminProviderAssignmentAuditEntry[]>>({});
  const [auditErrors, setAuditErrors] = useState<Record<string,boolean>>({});
  const [reload, setReload] = useState(0);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [products, setProducts] = useState<AdminProviderProduct[]>([]);
  const [productId, setProductId] = useState("");
  const [state, setState] = useState("");
  const [origin, setOrigin] = useState("");
  const [assignedUserId, setAssignedUserId] = useState("");
  const [oldestFirst, setOldestFirst] = useState(false);
  const [discrepancyFilter, setDiscrepancyFilter] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  useEffect(() => {
    void getAdminProviderProducts().then(setProducts).catch(() => setProducts([]));
    void getUsers().then(setUsers).catch(() => setError("Gebruikers konden niet worden geladen."));
  }, []);
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    getAdminProviderActionHistory({page, pageSize: 25, search: appliedSearch, providerProductId: productId, state, origin,
      from: dateBoundary(fromDate, false), until: dateBoundary(toDate, true), assignedUserId, oldestFirst, hasOpenDiscrepancy: discrepancyFilter === "" ? undefined : discrepancyFilter === "open"})
      .then(result => { if (active) setData(result); })
      .catch((reason: unknown) => { if (active) { setData(undefined); setError(reason instanceof Error ? `Historie kon niet worden geladen: ${reason.message}` : "Historie kon niet worden geladen."); } })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [page, appliedSearch, productId, state, origin, fromDate, toDate, assignedUserId, oldestFirst, discrepancyFilter, reload]);
  useEffect(() => {
    let active = true;
    let timer: ReturnType<typeof setTimeout> | undefined;
    async function refresh() {
      try {
        const status = await getAdminProviderHistorySyncStatus();
        if (active) {
          setSyncStatus(status);
          setSyncError("");
          if (status.runs.some(run => run.status === "Running"))
            timer = setTimeout(() => { void refresh(); }, 5000);
        }
      } catch {
        if (active) setSyncError("Synchronisatiestatus kon niet worden geladen.");
      }
    }
    void refresh();
    return () => { active = false; if (timer) clearTimeout(timer); };
  }, [reload]);
  async function startSync() {
    if (!syncProductId || syncBusy) return;
    setSyncBusy(true);
    setSyncError("");
    try {
      await startAdminProviderHistorySync(syncProductId);
      setSyncStatus(await getAdminProviderHistorySyncStatus());
      setReload(value => value + 1);
    } catch (reason) {
      setSyncError(reason instanceof Error ? reason.message : "Synchronisatie kon niet worden gestart.");
    } finally {
      setSyncBusy(false);
    }
  }
  async function cancelSync(runId:string) {
    setCancellingRunId(runId);
    setSyncError("");
    try {
      await cancelAdminProviderHistorySync(runId);
      setSyncStatus(await getAdminProviderHistorySyncStatus());
    } catch (reason) {
      setSyncError(reason instanceof Error ? reason.message : "Synchronisatie annuleren is mislukt.");
    } finally {
      setCancellingRunId(undefined);
    }
  }
  async function toggleDetails(actionId:string) {
    if (expandedId === actionId) { setExpandedId(undefined); return; }
    setExpandedId(actionId);
    setAuditErrors(current => ({...current,[actionId]:false}));
    try {
      const entries = await getAdminProviderAssignmentHistory(actionId);
      setAudit(current => ({...current,[actionId]:entries}));
    } catch {
      setAuditErrors(current => ({...current,[actionId]:true}));
    }
  }
  async function saveAssignment(actionId:string, userId:string|null) {
    setSavingId(actionId);
    setError("");
    try {
      await assignAdminProviderActionUser(actionId, userId);
      setReload(value => value + 1);
      const entries = await getAdminProviderAssignmentHistory(actionId);
      setAudit(current => ({...current,[actionId]:entries}));
      setAuditErrors(current => ({...current,[actionId]:false}));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Toewijzing kon niet worden opgeslagen.");
    } finally {
      setSavingId(undefined);
    }
  }
  return <div className="admin-provider">
    <AdminProviderSubnav current="history" />
    <section className="admin-provider__panel">
      <h2>Provideractiehistorie</h2>
      <div className="admin-provider__panel">
        <h3>Historie synchroniseren</h3>
        <div className="admin-provider__toolbar">
          <label>Providerproduct
            <select className="admin-table__control" aria-label="Product voor synchronisatie"
              value={syncProductId} onChange={event => setSyncProductId(event.target.value)}>
              <option value="">Kies een product</option>
              {products.map(product => <option key={product.id} value={product.providerProductId}>{product.name}</option>)}
            </select>
          </label>
          <Button variant="secondary" disabled={!syncProductId || syncBusy ||
            syncStatus?.runs.some(run => run.providerProductId === syncProductId && run.status === "Running")}
            onClick={() => void startSync()}>
            {syncBusy ? "Starten…" : "Synchroniseren"}
          </Button>
        </div>
        {syncError && <Alert tone="danger">{syncError}</Alert>}
        {syncStatus && <div>
          <p>Laatste synchronisatieruns</p>
          {syncStatus.runs.length === 0 ? <p>Nog geen synchronisaties uitgevoerd.</p> :
            <div className="admin-table-wrap"><table className="admin-table">
              <thead><tr><th>Product</th><th>Gestart</th><th>Status</th><th>Gelezen</th><th>Toegevoegd</th><th>Bijgewerkt</th><th>Overgeslagen</th><th>Afgerond</th><th>Foutmelding</th><th>Acties</th></tr></thead>
              <tbody>{syncStatus.runs.slice(0, 5).map(run => <tr key={run.id}>
                <td>{products.find(product => product.providerProductId === run.providerProductId)?.name ?? run.providerProductId}</td>
                <td>{formatAdminDateTime(run.startedAt)}</td>
                <td>{syncRunStatusLabel(run.status)}</td>
                <td>{run.readCount}</td><td>{run.insertedCount}</td><td>{run.refreshedCount}</td>
                <td>{run.skippedCount}</td><td>{run.finishedAt ? formatAdminDateTime(run.finishedAt) : "—"}</td>
                <td>{run.error || "—"}</td>
                <td>{run.status === "Running" && <Button variant="secondary"
                  disabled={cancellingRunId === run.id}
                  onClick={() => void cancelSync(run.id)}>
                  {cancellingRunId === run.id ? "Annuleren…" : "Annuleren"}
                </Button>}</td>
              </tr>)}</tbody>
            </table></div>}
        </div>}
      </div>
      <form className="admin-provider__toolbar admin-provider__history-filters" onSubmit={event => {
        event.preventDefault();
        setPage(1);
        setAppliedSearch(search.trim());
      }}>
        <label>Zoeken
          <input className="admin-table__control" aria-label="Kenteken of provideractie-ID" placeholder="Kenteken of provideractie-ID" value={search} onChange={event => setSearch(event.target.value)} />
        </label>
        <label>Product
          <select className="admin-table__control" aria-label="Providerproduct" value={productId} onChange={event => { setPage(1); setProductId(event.target.value); }}>
          <option value="">Alle producten</option>
          {products.map(product => <option key={product.id} value={product.providerProductId}>{product.name}</option>)}
          </select>
        </label>
        <label>Status
          <select className="admin-table__control" aria-label="Status" value={state} onChange={event => { setPage(1); setState(event.target.value); }}>
          <option value="">Alle statussen</option>
          {["Planned", "Starting", "Scheduled", "Active", "Stopping", "Stopped", "Completed", "Failed"].map(value =>
            <option key={value} value={value}>{formatAdminProviderActionStatus(value)}</option>)}
          </select>
        </label>
        <label>Herkomst
          <select className="admin-table__control" aria-label="Herkomst" value={origin} onChange={event => { setPage(1); setOrigin(event.target.value); }}>
          <option value="">Alle herkomsten</option>
          <option value="Managed">Beheerd</option>
          <option value="Imported">Geïmporteerd</option>
          <option value="External">Extern</option>
          </select>
        </label>
        <label>Gebruiker
          <select className="admin-table__control" aria-label="Gebruiker" value={assignedUserId}
            onChange={event => { setPage(1); setAssignedUserId(event.target.value); }}>
            <option value="">Alle gebruikers</option>
            {users.map(user => <option key={user.id} value={user.id}>{user.username}</option>)}
          </select>
        </label>
        <label>Sortering
          <select className="admin-table__control" aria-label="Sortering" value={oldestFirst ? "asc" : "desc"}
            onChange={event => { setPage(1); setOldestFirst(event.target.value === "asc"); }}>
            <option value="desc">Nieuwste eerst</option>
            <option value="asc">Oudste eerst</option>
          </select>
        </label>
        <label>Afwijking
          <select className="admin-table__control" aria-label="Afwijking" value={discrepancyFilter}
            onChange={event => { setPage(1); setDiscrepancyFilter(event.target.value); }}>
            <option value="">Alle acties</option>
            <option value="open">Met open afwijking</option>
            <option value="none">Zonder open afwijking</option>
          </select>
        </label>
        <label>Van
          <input className="admin-table__control" type="date" value={fromDate}
            max={toDate || undefined} onChange={event => { setPage(1); setFromDate(event.target.value); }} />
        </label>
        <label>Tot en met
          <input className="admin-table__control" type="date" value={toDate}
            min={fromDate || undefined} onChange={event => { setPage(1); setToDate(event.target.value); }} />
        </label>
        <Button type="submit" variant="secondary">Zoeken</Button>
      </form>
      {error && <Alert tone="danger">{error}</Alert>}
      {loading && <Loading label="Historie laden" />}
      {!loading && data && <div className="admin-table-wrap">
        <table className="admin-table">
          <thead><tr><th>Kenteken</th><th>Start</th><th>Product</th><th>Status</th><th>Herkomst</th><th aria-label="Acties" /></tr></thead>
          <tbody>{data.items.map(item => <Fragment key={item.id}><tr>
            <td>{item.licensePlate ? <LicensePlate value={item.licensePlate} /> : "—"}</td>
            <td>{formatAdminDateTime(item.actualStartAt)}</td>
            <td>{products.find(product => product.providerProductId === item.providerProductId)?.name ?? item.providerProductId ?? "—"}</td>
            <td>{formatAdminProviderActionStatus(item.state)}</td>
            <td>{item.origin === "Managed" ? "Beheerd" : item.origin === "Imported" ? "Geïmporteerd" : item.origin === "External" ? "Extern" : item.origin}</td>
            <td className="admin-table__actions">
              <button type="button" className="admin-action-link admin-action-link--muted"
                aria-expanded={expandedId === item.id}
                onClick={() => void toggleDetails(item.id)}>
                {expandedId === item.id ? "Verbergen" : "Details"}
              </button>
            </td>
          </tr>
          {expandedId === item.id && <tr className="admin-table__detail-row">
            <td colSpan={6}><div className="admin-table__detail-panel">
              <dl className="admin-facts admin-facts--grid">
                <div className="admin-fact"><dt>Provideractie-ID</dt><dd className="admin-code">{item.providerActionId ?? "—"}</dd></div>
                <div className="admin-fact"><dt>Lokale actie-ID</dt><dd className="admin-code">{item.id}</dd></div>
                <div className="admin-fact"><dt>Product-ID</dt><dd className="admin-code">{item.providerProductId ?? "—"}</dd></div>
                <div className="admin-fact"><dt>Werkelijke start</dt><dd>{formatAdminDateTime(item.actualStartAt)}</dd></div>
                <div className="admin-fact"><dt>Werkelijk einde</dt><dd>{formatAdminDateTime(item.actualEndAt)}</dd></div>
                <div className="admin-fact"><dt>Historie-status</dt><dd>{item.historyStatus}</dd></div>
                <div className="admin-fact"><dt>Toewijzingsbron</dt><dd>{item.origin === "Managed" ? "Via Visit" : item.assignmentSource === "Unassigned" ? "Niet toegewezen" : item.assignmentSource === "Inferred" ? "Afgeleid" : item.assignmentSource === "ManuallyAssigned" ? "Handmatig" : "Bevestigd"}</dd></div>
                <div className="admin-fact"><dt>Gebruiker</dt><dd>{item.username ?? "Niet toegewezen"}</dd></div>
                <div className="admin-fact"><dt>Providerkosten</dt><dd>{item.providerCostAmount === null ? "—" : item.providerCostAmount.toLocaleString("nl-NL", { style: "currency", currency: "EUR" })}</dd></div>
              </dl>
              {item.origin !== "Managed" && <div>
                <h3>Toewijzingshistorie</h3>
                {auditErrors[item.id] ? <p>Toewijzingshistorie kon niet worden geladen. Sluit en open de details opnieuw om het nogmaals te proberen.</p> :
                  audit[item.id] === undefined ? <p>Historie laden…</p> :
                  audit[item.id].length === 0 ? <p>Geen handmatige wijzigingen.</p> :
                  <ul>{audit[item.id].map(entry => <li key={entry.id}>
                    {formatAdminDateTime(entry.createdAt)} · {entry.actorUsername ?? entry.actorUserId} · {assignmentChange(entry.contextJson, users)}
                  </li>)}</ul>}
              </div>}
              {item.origin !== "Managed" && <div className="admin-provider__toolbar">
                <label>Gebruiker toewijzen
                  <select className="admin-table__control"
                    value={selectedUsers[item.id] ?? item.assignedUserId ?? ""}
                    onChange={event => setSelectedUsers(values => ({...values, [item.id]: event.target.value}))}>
                    <option value="">Niet toegewezen</option>
                    {users.filter(user => user.role === "Visitor" || user.id === item.assignedUserId)
                      .map(user => <option key={user.id} value={user.id}>{user.username}</option>)}
                  </select>
                </label>
                <Button variant="secondary" disabled={savingId === item.id}
                  onClick={() => void saveAssignment(item.id, (selectedUsers[item.id] ?? item.assignedUserId) || null)}>
                  {savingId === item.id ? "Opslaan…" : "Toewijzing opslaan"}
                </Button>
              </div>}
              {item.visitId && <a className="admin-action-link" href={`/beheer/bezoeken/${item.visitId}`}>Visit openen</a>}
            </div></td>
          </tr>}</Fragment>)}</tbody>
        </table>
        <div className="admin-provider__toolbar">
          <p>{data.totalCount} provideracties · pagina {page} van {Math.max(1, Math.ceil(data.totalCount / data.pageSize))}</p>
          <div>
            <Button variant="secondary" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Vorige</Button>{" "}
            <Button variant="secondary" disabled={page * data.pageSize >= data.totalCount} onClick={() => setPage(value => value + 1)}>Volgende</Button>
          </div>
        </div>
      </div>}
    </section>
  </div>;
}
