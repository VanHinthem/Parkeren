import { AdminProviderSubnav } from "./AdminProviderSubnav";
import "./AdminProvider.css";

export function AdminProviderHistoryPage() {
  return (
    <div className="admin-provider">
      <AdminProviderSubnav current="history" />
      <section className="admin-provider__panel">
        <div className="admin-provider__toolbar">
          <div>
            <h2>Provideractiehistorie</h2>
            <p>
              Overzicht van beheerde, geïmporteerde en extern waargenomen
              provideracties. Zoeken, filters, details en gebruikerstoewijzing
              worden hier toegevoegd.
            </p>
          </div>
        </div>
        <p className="admin-provider__empty">
          Het provideractie-overzicht wordt in de volgende stap aangesloten.
        </p>
      </section>
    </div>
  );
}
