import type { ActiveVisit } from "../api/client";
import { LicensePlate } from "./LicensePlate";
import { activeVisitPresentation } from "./activeVisitPresentation";
import "./ActiveVisitCard.css";

type Props={
  vehicle:string;
  elapsed:string;
  startTime:string;
  endTime?:string|null;
  health:ActiveVisit["health"];
};

function time(value:string){
  return new Date(value).toLocaleTimeString("nl-NL",{hour:"2-digit",minute:"2-digit"});
}

function date(value:string){
  return new Date(value).toLocaleDateString("nl-NL",{day:"numeric",month:"short",year:"numeric"});
}

export function ActiveVisitCard({vehicle,elapsed,startTime,endTime,health}:Props){
  const state=activeVisitPresentation(health);
  return <section className={`visit-card${state.attention?" visit-card--attention":""}`}>
    <div className="visit-card__status"><span>P</span><strong>{state.label}</strong></div>
    <div className="visit-card__time">{elapsed}</div>
    <div className="visit-card__schedule">
      <span>
        <strong>Gestart {time(startTime)}</strong>
        <small>{date(startTime)}</small>
      </span>
      <span>
        {endTime
          ? <>
              <strong>Eindtijd {time(endTime)}</strong>
              <small>{date(endTime)}</small>
            </>
          : <strong>Tot handmatig stoppen</strong>}
      </span>
    </div>
    <div className="visit-card__bottom">
      <span className="visit-card__vehicle">
        <LicensePlate value={vehicle}/>
        <small>{state.detail}</small>
      </span>
    </div>
  </section>;
}
