import "./CapacityIndicator.css";

type Props = { used: number; total: number };

export function CapacityIndicator({ used, total }: Props) {
  const safeUsed = Math.min(Math.max(used, 0), total);
  return (
    <div className="capacity" aria-label={`${safeUsed} van ${total} parkeerplaatsen in gebruik`}>
      <div><strong>{safeUsed} / {total}</strong><span> in gebruik</span></div>
      <div className="capacity__slots" aria-hidden="true">
        {Array.from({ length: total }, (_, index) => <span key={index} className={index < safeUsed ? "used" : ""} />)}
      </div>
    </div>
  );
}
