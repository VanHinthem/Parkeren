import type { MouseEvent } from "react";
import { AdminDashboard } from "./AdminDashboard";
import { AdminVisitsPage,AdminVisitDetailPage } from "./AdminVisitsPage";
import { AdminProviderPage } from "./AdminProviderPage";
import { AdminUsersPage,AdminUserDetailPage } from "./AdminUsersPage";
import { AdminSystemPage } from "./AdminSystemPage";
import { AdminParkingRulesPage } from "./AdminParkingRulesPage";
import { AdminFinanceConfigPage } from "./AdminFinanceConfigPage";
import { AdminUsagePage } from "./AdminUsagePage";
import "./AdminPortal.css";

type Props = {
  currentPath: string;
  username: string;
  onNavigate: (path: string) => void;
};

type AdminSection = {
  path: string;
  label: string;
  title: string;
  subtitle: string;
};

const sections: AdminSection[] = [
  {
    path: "/beheer",
    label: "Overzicht",
    title: "Beheer",
    subtitle: "Operationeel overzicht en aandachtspunten voor Parkeren."
  },
  {
    path: "/beheer/bezoeken",
    label: "Bezoeken",
    title: "Bezoeken",
    subtitle: "Actieve bezoeken, historie, details, verbruik en kosten."
  },
  {
    path: "/beheer/gebruikers",
    label: "Gebruikers & voertuigen",
    title: "Gebruikers & voertuigen",
    subtitle: "Accounts, voertuigen, toewijzingen en gebruikersbeleid."
  },
  {
    path: "/beheer/configuratie",
    label: "Parkeerconfiguratie",
    title: "Parkeerconfiguratie",
    subtitle: "Zones, parkeerregels, betaalvensters, uitzonderingen, tarieven en budgetten."
  },
  {
    path: "/beheer/provider",
    label: "Provider & reconciliatie",
    title: "Provider & reconciliatie",
    subtitle: "2Park-status, provideracties, operations, discrepancies en herstelcontext."
  },
  {
    path: "/beheer/systeem",
    label: "Systeem",
    title: "Systeem",
    subtitle: "Algemene instellingen, notificaties, diagnostiek, scheduler en audit."
  }
];

function resolveSection(path: string): AdminSection {
  if (path === "/beheer" || path === "/beheer/") return sections[0];
  if (path === "/beheer/verbruik" || path === "/beheer/historie") return sections[1];
  if (path === "/beheer/voertuigen" || path.startsWith("/beheer/voertuigen/")) return sections[2];
  return sections.find(section => section.path !== "/beheer" && path.startsWith(section.path)) ?? sections[0];
}

export function AdminPortal({ currentPath, username, onNavigate }: Props) {
  const section = resolveSection(currentPath);

  function handleNavigation(event: MouseEvent<HTMLElement>) {
    const anchor = (event.target as HTMLElement).closest("a");
    if (!anchor) return;

    const url = new URL(anchor.href, window.location.origin);
    if (url.origin !== window.location.origin) return;

    event.preventDefault();
    if (url.pathname === currentPath) return;

    history.pushState({}, "", url.pathname);
    onNavigate(url.pathname);
  }

  return (
    <div className="admin-portal" onClick={handleNavigation}>
      <aside className="admin-portal__sidebar">
        <a className="admin-portal__brand" href="/beheer">
          <img src="/pwa-192x192.png" alt="" />
          <span className="admin-portal__brand-copy">
            <strong>Parkeren</strong>
            <small>Beheerportaal</small>
          </span>
        </a>

        <nav className="admin-portal__nav" aria-label="Beheernavigatie">
          {sections.map(item => (
            <a
              key={item.path}
              href={item.path}
              className={section.path === item.path ? "active" : ""}
            >
              {item.label}
            </a>
          ))}
        </nav>

        <footer className="admin-portal__sidebar-footer">
          <div className="admin-portal__user">
            <strong>{username}</strong>
            <small>Beheerder</small>
          </div>
          <div className="admin-portal__quick-links">
            <a href="/">PWA openen</a>
            <a href="/snelbeheer">Snelbeheer</a>
          </div>
        </footer>
      </aside>

      <main className="admin-portal__main">
        <div className="admin-portal__content">
          <header className="admin-portal__heading">
            <h1>{section.title}</h1>
            <p>{section.subtitle}</p>
          </header>

          {section.path === "/beheer" ? (
            <AdminDashboard />
          ) : section.path === "/beheer/bezoeken" ? (
            currentPath === "/beheer/verbruik" ? (
              <AdminUsagePage />
            ) : currentPath.startsWith("/beheer/bezoeken/") ? (
              <AdminVisitDetailPage visitId={currentPath.slice("/beheer/bezoeken/".length)} />
            ) : (
              <AdminVisitsPage />
            )
          ) : section.path === "/beheer/gebruikers" ? (
            currentPath === "/beheer/voertuigen" ? (
              <AdminUsersPage mode="vehicles" />
            ) : currentPath.startsWith("/beheer/gebruikers/") ? (
              <AdminUserDetailPage userId={currentPath.slice("/beheer/gebruikers/".length)} />
            ) : (
              <AdminUsersPage mode="users" />
            )
          ) : section.path === "/beheer/configuratie" ? (
            currentPath === "/beheer/configuratie" || currentPath === "/beheer/configuratie/parkeerregels" ? (
              <AdminParkingRulesPage />
            ) : currentPath === "/beheer/configuratie/tarieven" ? (
              <AdminFinanceConfigPage mode="tariffs" />
            ) : currentPath === "/beheer/configuratie/budgetten" ? (
              <AdminFinanceConfigPage mode="budgets" />
            ) : (
              <section className="admin-portal__panel admin-portal__placeholder">
                <h2>{section.title}</h2>
                <p>Deze configuratiefunctie wordt in een volgende verticale slice toegevoegd.</p>
              </section>
            )
          ) : section.path === "/beheer/provider" ? (
            currentPath === "/beheer/provider/afwijkingen" ? (
              <section className="admin-portal__panel admin-portal__placeholder">
                <h2>Afwijkingen & reconciliatie</h2>
                <p>Persistente discrepancies en recovery-acties worden in verticale slice 8.11 toegevoegd.</p>
              </section>
            ) : (
              <AdminProviderPage />
            )
          ) : section.path === "/beheer/systeem" ? (
            currentPath === "/beheer/systeem" ? (
              <AdminSystemPage />
            ) : (
              <section className="admin-portal__panel admin-portal__placeholder">
                <h2>{section.title}</h2>
                <p>Deze systeemfunctie wordt in een volgende verticale slice toegevoegd.</p>
              </section>
            )
          ) : (
            <section className="admin-portal__panel admin-portal__placeholder">
              <h2>{section.title}</h2>
              <p>Deze beheerfunctie wordt in een volgende verticale slice toegevoegd.</p>
            </section>
          )}
        </div>
      </main>
    </div>
  );
}
