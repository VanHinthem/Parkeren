import { ActiveVisitCard } from "./components/ActiveVisitCard";
import { CapacityIndicator } from "./components/CapacityIndicator";
import { ThemeSelector } from "./components/ThemeSelector";
import { AppShell } from "./design/layout/AppShell";
import { Card } from "./design/primitives/Card";

export default function App() {
  return (
    <AppShell>
      <div style={{ display: "grid", gap: "1rem", maxWidth: "44rem" }}>
        <div><h1 style={{ marginBottom: ".25rem" }}>Goedemiddag</h1><span style={{ color: "var(--color-text-muted)" }}>Hier is je parkeeroverzicht.</span></div>
        <ActiveVisitCard vehicle="K-123-AB" startedAt="14:32" endsAt="18:32" elapsed="2:18:24" />
        <Card><CapacityIndicator used={3} total={5} /></Card>
        <Card><ThemeSelector /></Card>
      </div>
    </AppShell>
  );
}
