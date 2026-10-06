import type { MouseEvent } from "react";
import { AdminDashboard } from "./AdminDashboard";
import { AdminVisitsPage,AdminVisitDetailPage } from "./AdminVisitsPage";
import { AdminProviderPage } from "./AdminProviderPage";
import { AdminProviderDiscrepanciesPage } from "./AdminProviderDiscrepanciesPage";
import { AdminUserDetailPage } from "./AdminUserDetailPage";
import { AdminUsersOverviewPage } from "./AdminUsersOverviewPage";
import { AdminSystemPage } from "./AdminSystemPage";
import { AdminSystemDiagnosticsPage } from "./AdminSystemDiagnosticsPage";
import { AdminSystemAuditPage } from "./AdminSystemAuditPage";
import { AdminParkingRulesPage } from "./AdminParkingRulesPage";
import { AdminFinanceConfigPage } from "./AdminFinanceConfigPage";
import { AdminUsagePage } from "./AdminUsagePage";
import { AdminAnalysisPage } from "./AdminAnalysisPage";
import "./adminTablePresentation.css";
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
    subtitle: "Actieve en afgeronde Visits, planning en operationele details."
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
    subtitle: "Productgebonden parkeerregels, betaalvensters, uitzonderingen, tarieven en budgetten."
  },
  {
    path: "/beheer/provider",
    label: "Provider & reconciliatie",
    title: "Provider & reconciliatie",
    subtitle: "2Park-producten, provideracties, afwijkingen en recoverycontext."
  },
  {
    path: "/beheer/systeem",
    label: "Systeem",
    title: "Systeem",
    subtitle: "Diagnostiek, audit en technische beheerinformatie."
  }
];

function sectionForPath(path:string){
  if(path.startsWith("/beheer/gebruikers/")||path==="/beheer/voertuigen")return sections.find(section=>section.path==="/beheer/gebruikers")!;
  if(path.startsWith("/beheer/bezoeken/"))return sections.find(section=>section.path==="/beheer/bezoeken")!;
  if(path.startsWith("/beheer/configuratie"))return sections.find(section=>section.path==="/beheer/configuratie")!;
  if(path.startsWith("/beheer/provider"))return sections.find(section=>section.path==="/beheer/provider")!;
  if(path.startsWith("/beheer/systeem"))return sections.find(section=>section.path==="/beheer/systeem")!;
  return sections.find(section=>section.path===path)??sections[0];
}

export function AdminPortal({currentPath,username,onNavigate}:Props){
  const section=sectionForPath(currentPath);

  function interceptNavigation(event:MouseEvent<HTMLElement>){
    const target=event.target as HTMLElement;
    const anchor=target.closest("a");
    if(!anchor)return;
    const href=anchor.getAttribute("href");
    if(!href||!href.startsWith("/"))return;
    event.preventDefault();
    onNavigate(href);
  }

  let content;
  if(currentPath==="/beheer")content=<AdminDashboard/>;
  else if(currentPath==="/beheer/bezoeken")content=<AdminVisitsPage/>;
  else if(currentPath.startsWith("/beheer/bezoeken/"))content=<AdminVisitDetailPage visitId={decodeURIComponent(currentPath.slice("/beheer/bezoeken/".length))}/>;
  else if(currentPath==="/beheer/gebruikers")content=<AdminUsersOverviewPage mode="users"/>;
  else if(currentPath==="/beheer/voertuigen")content=<AdminUsersOverviewPage mode="vehicles"/>;
  else if(currentPath.startsWith("/beheer/gebruikers/"))content=<AdminUserDetailPage userId={decodeURIComponent(currentPath.slice("/beheer/gebruikers/".length))}/>;
  else if(currentPath==="/beheer/provider")content=<AdminProviderPage/>;
  else if(currentPath==="/beheer/provider/reconciliatie")content=<AdminProviderDiscrepanciesPage/>;
  else if(currentPath==="/beheer/configuratie"||currentPath==="/beheer/configuratie/parkeerregels")content=<AdminParkingRulesPage/>;
  else if(currentPath==="/beheer/configuratie/tarieven")content=<AdminFinanceConfigPage mode="tariffs"/>;
  else if(currentPath==="/beheer/configuratie/budgetten")content=<AdminFinanceConfigPage mode="budgets"/>;
  else if(currentPath==="/beheer/verbruik")content=<AdminUsagePage/>;
  else if(currentPath==="/beheer/analyse")content=<AdminAnalysisPage/>;
  else if(currentPath==="/beheer/systeem")content=<AdminSystemPage/>;
  else if(currentPath==="/beheer/systeem/diagnostiek")content=<AdminSystemDiagnosticsPage/>;
  else if(currentPath==="/beheer/systeem/audit")content=<AdminSystemAuditPage/>;
  else content=<p>Deze beheerpagina is nog niet beschikbaar.</p>;

  return <div className="admin-portal" onClick={interceptNavigation}>
    <aside className="admin-portal__sidebar">
      <a className="admin-portal__brand" href="/beheer">
        <img src="/icons/icon-192.png" alt=""/>
        <span className="admin-portal__brand-copy"><strong>Parkeren</strong><small>Beheerportal</small></span>
      </a>
      <nav className="admin-portal__nav" aria-label="Beheer">
        {sections.map(item=><a className={section.path===item.path?"active":""} href={item.path} key={item.path}>{item.label}</a>)}
      </nav>
      <div className="admin-portal__sidebar-footer">
        <div className="admin-portal__user"><strong>{username}</strong><small>Beheerder</small></div>
        <div className="admin-portal__quick-links"><a href="/">PWA openen</a><a href="/beheer/snelbeheer">Snelbeheer</a></div>
      </div>
    </aside>

    <main className="admin-portal__main">
      <div className="admin-portal__content">
        <header className="admin-portal__heading">
          <h1>{section.title}</h1>
          <p>{section.subtitle}</p>
        </header>
        {content}
      </div>
    </main>
  </div>;
}
