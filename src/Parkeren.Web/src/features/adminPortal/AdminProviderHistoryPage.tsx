import { useEffect, useState } from "react";
import { getAdminProviderActionHistory, type AdminProviderActionHistoryPage } from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Loading } from "../../design/primitives/Loading";
import { formatAdminDateTime, formatAdminProviderActionStatus } from "./adminFieldFormatters";
import { AdminProviderSubnav } from "./AdminProviderSubnav";
import "./AdminProvider.css";

export function AdminProviderHistoryPage() {
  const [data, setData] = useState<AdminProviderActionHistoryPage>();
  const [error, setError] = useState("");
  useEffect(() => {
    let active = true;
    getAdminProviderActionHistory({page: 1, pageSize: 25})
      .then(result => { if (active) setData(result); })
      .catch(() => { if (active) setError("Historie kon niet worden geladen."); });
    return () => { active = false; };
  }, []);
  return <div className="admin-provider">
    <AdminProviderSubnav current="history" />
    <section className="admin-provider__panel">
      <h2>Provideractiehistorie</h2>
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
        <p>{data.totalCount} provideracties gevonden.</p>
      </div>}
    </section>
  </div>;
}
