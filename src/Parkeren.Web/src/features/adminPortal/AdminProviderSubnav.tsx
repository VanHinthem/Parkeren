type ProviderSubpage = "provider" | "reconciliation" | "history";

const links: { path: string; label: string; page: ProviderSubpage }[] = [
  { path: "/beheer/provider", label: "Provider", page: "provider" },
  { path: "/beheer/provider/afwijkingen", label: "Reconciliatie", page: "reconciliation" },
  { path: "/beheer/provider/historie", label: "Historie", page: "history" }
];

export function AdminProviderSubnav({ current }: { current: ProviderSubpage }) {
  return (
    <nav className="admin-subnav" aria-label="Provider & reconciliatie">
      {links.map(link => (
        <a key={link.path}
          className={`admin-subnav__link${current === link.page ? " active" : ""}`}
          href={link.path}
          aria-current={current === link.page ? "page" : undefined}>
          {link.label}
        </a>
      ))}
    </nav>
  );
}
