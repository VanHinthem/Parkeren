import { ActiveVisitCard } from "./components/ActiveVisitCard";
import { CapacityIndicator } from "./components/CapacityIndicator";
import { ThemeSelector } from "./components/ThemeSelector";
import { AppShell } from "./design/layout/AppShell";
import { Page } from "./design/layout/Page";
import { Card } from "./design/primitives/Card";

export default function App() {
  return (
    <AppShell>
      <Page title="Goedemiddag" subtitle="Hier is je parkeeroverzicht.">
        <ActiveVisitCard vehicle="K-123-AB" startedAt="14:32" endsAt="18:32" elapsed="2:18:24" />
        <Card><CapacityIndicator used={3} total={5} /></Card>
        <Card><ThemeSelector /></Card>
      </Page>
    </AppShell>
  );
}
