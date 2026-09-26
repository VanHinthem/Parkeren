import { Badge } from "../design/primitives/Badge";
import { Button } from "../design/primitives/Button";
import "./ActiveVisitCard.css";

type Props = {
  vehicle: string;
  startedAt: string;
  endsAt: string;
  elapsed: string;
  onStop?: () => void;
  onExtend?: () => void;
};

export function ActiveVisitCard({ vehicle, startedAt, endsAt, elapsed, onStop, onExtend }: Props) {
  return (
    <section className="visit-card">
      <div className="visit-card__top"><Badge>● Actief</Badge><strong>{vehicle}</strong></div>
      <div className="visit-card__time" aria-label={`Parkeerduur ${elapsed}`}>{elapsed}</div>
      <div className="visit-card__meta"><span>Gestart {startedAt}</span><span>Eindigt {endsAt}</span></div>
      <div className="visit-card__actions">
        <Button variant="danger" onClick={onStop}>Stoppen</Button>
        <Button onClick={onExtend}>Verlengen</Button>
      </div>
    </section>
  );
}
