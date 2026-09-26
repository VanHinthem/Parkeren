import type { PropsWithChildren } from "react";
import "./AppShell.css";

export function AppShell({ children }: PropsWithChildren) {
  return (
    <div className="app-shell">
      <header className="app-header"><strong>🚙 Parkeren</strong><button className="icon-button" aria-label="Instellingen">⚙</button></header>
      <aside className="side-nav" aria-label="Hoofdnavigatie">
        <strong>🚙 Parkeren</strong>
        <a className="active" href="/">⌂ Home</a><a href="/acties">◷ Acties</a><a href="/autos">▣ Auto's</a><a href="/instellingen">⚙ Instellingen</a>
      </aside>
      <main className="app-content">{children}</main>
      <nav className="bottom-nav" aria-label="Hoofdnavigatie">
        <a className="active" href="/">⌂<span>Home</span></a><a href="/acties">◷<span>Acties</span></a><a href="/autos">▣<span>Auto's</span></a>
      </nav>
    </div>
  );
}
