import { LicensePlate } from "./LicensePlate";
import "./ActiveVisitCard.css";
type Props={vehicle:string;elapsed:string;startTime:string;endTime?:string|null};
function time(value:string){return new Date(value).toLocaleTimeString("nl-NL",{hour:"2-digit",minute:"2-digit"});}
export function ActiveVisitCard({vehicle,elapsed,startTime,endTime}:Props){return <section className="visit-card"><div className="visit-card__status"><span>P</span><strong>Actief parkeren</strong></div><div className="visit-card__time">{elapsed}</div><div className="visit-card__schedule"><span>Gestart {time(startTime)}</span><span>{endTime?`Eindtijd ${time(endTime)}`:"Tot handmatig stoppen"}</span></div><div className="visit-card__bottom"><span className="visit-card__vehicle"><LicensePlate value={vehicle}/><small>Actieve parkeeractie</small></span></div></section>;}
