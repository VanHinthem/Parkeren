import type { MouseEvent } from "react";
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
            <div className="admin-portal__foundation">
              <section className="admin-portal__panel">
                <h2>Beheeromgeving</h2>
                <p>
                  De desktop beheer-shell, routing en autorisatiebasis zijn actief. Operationele
                  dashboarddata wordt in de volgende verticale slice toegevoegd.
                </p>
              </section>
              <section className="admin-portal__panel">
                <h2>Snelbeheer blijft apart</h2>
                <p>
                  Mobiele operationele beheeracties blijven beschikbaar via Snelbeheer in de PWA.
                  Uitgebreid beheer wordt vanaf hier opgebouwd.
                </p>
              </section>
            </div>
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
