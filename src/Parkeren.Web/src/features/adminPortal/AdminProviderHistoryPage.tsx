import { useEffect, useState } from "react";
import { getAdminProviderActionHistory, getAdminProviderProducts, type AdminProviderProduct, type AdminProviderActionHistoryPage } from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Loading } from "../../design/primitives/Loading";
import { Button } from "../../design/primitives/Button";
import { formatAdminDateTime, formatAdminProviderActionStatus } from "./adminFieldFormatters";
import { AdminProviderSubnav } from "./AdminProviderSubnav";
import "./AdminProvider.css";

function dateBoundary(value: string, nextDay: boolean) {
  if (!value) return undefined;
  const boundary = new Date(value + "T00:00:00");
  if (nextDay) boundary.setDate(boundary.getDate() + 1);
  return boundary.toISOString();
}

export function AdminProviderHistoryPage() {
  const [data, setData] = useState<AdminProviderActionHistoryPage>();
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [products, setProducts] = useState<AdminProviderProduct[]>([]);
  const [productId, setProductId] = useState("");
  const [state, setState] = useState("");
  const [origin, setOrigin] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  useEffect(() => { void getAdminProviderProducts().then(setProducts).catch(() => setProducts([])); }, []);
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    getAdminProviderActionHistory({page, pageSize: 25, search: appliedSearch, providerProductId: productId, state, origin,
      from: dateBoundary(fromDate, false), until: dateBoundary(toDate, true)})
      .then(result => { if (active) setData(result); })
      .catch(() => { if (active) { setData(undefined); setError("Historie kon niet worden geladen."); } })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [page, appliedSearch, productId, state, origin, fromDate, toDate]);
  return <div className="admin-provider">
    <AdminProviderSubnav current="history" />
    <section className="admin-provider__panel">
      <h2>Provideractiehistorie</h2>
      <form className="admin-provider__toolbar" onSubmit={event => {
        event.preventDefault();
        setPage(1);
        setAppliedSearch(search.trim());
      }}>
        <input className="admin-table__control" aria-label="Kenteken of provideractie-ID" placeholder="Kenteken of provideractie-ID" value={search} onChange={event => setSearch(event.target.value)} />
        <select className="admin-table__control" aria-label="Providerproduct" value={productId} onChange={event => { setPage(1); setProductId(event.target.value); }}>
          <option value="">Alle producten</option>
          {products.map(product => <option key={product.id} value={product.providerProductId}>{product.name}</option>)}
        </select>
        <select className="admin-table__control" aria-label="Status" value={state} onChange={event => { setPage(1); setState(event.target.value); }}>
          <option value="">Alle statussen</option>
          {["Planned", "Starting", "Scheduled", "Active", "Stopping", "Stopped", "Completed", "Failed"].map(value =>
            <option key={value} value={value}>{formatAdminProviderActionStatus(value)}</option>)}
        </select>
        <select className="admin-table__control" aria-label="Herkomst" value={origin} onChange={event => { setPage(1); setOrigin(event.target.value); }}>
          <option value="">Alle herkomsten</option>
          <option value="Managed">Beheerd</option>
          <option value="Imported">Geïmporteerd</option>
          <option value="External">Extern</option>
        </select>
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
          <thead><tr><th>Kenteken</th><th>Start</th><th>Product</th><th>Status</th><th>Herkomst</th></tr></thead>
          <tbody>{data.items.map(item => <tr key={item.id}>
            <td>{item.licensePlate ?? "—"}</td>
            <td>{formatAdminDateTime(item.actualStartAt)}</td>
            <td>{item.providerProductId ?? "—"}</td>
            <td>{formatAdminProviderActionStatus(item.state)}</td>
            <td>{item.origin}</td>
          </tr>)}</tbody>
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
