import { useEffect, useState } from "react";
import { getAdminProviderActionHistory, type AdminProviderActionHistoryPage } from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Loading } from "../../design/primitives/Loading";
import { Button } from "../../design/primitives/Button";
import { formatAdminDateTime, formatAdminProviderActionStatus } from "./adminFieldFormatters";
import { AdminProviderSubnav } from "./AdminProviderSubnav";
import "./AdminProvider.css";

export function AdminProviderHistoryPage() {
  const [data, setData] = useState<AdminProviderActionHistoryPage>();
  const [error, setError] = useState("");
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  useEffect(() => {
    let active = true;
    getAdminProviderActionHistory({page, pageSize: 25, search: appliedSearch})
      .then(result => { if (active) setData(result); })
      .catch(() => { if (active) setError("Historie kon niet worden geladen."); });
    return () => { active = false; };
  }, [page, appliedSearch]);
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
        <Button type="submit" variant="secondary">Zoeken</Button>
      </form>
      {error && <Alert tone="danger">{error}</Alert>}
      {!data && !error && <Loading label="Historie laden" />}
      {data && <div className="admin-table-wrap">
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
