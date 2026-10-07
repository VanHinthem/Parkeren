import type { AdminActiveVisitSummary } from "../api/client";
import { Icon } from "../design/icons/Icon";
import { LicensePlate } from "./LicensePlate";
import "./RecentVisits.css";
import "./OtherActiveVisits.css";

function time(value:string){
  return new Date(value).toLocaleTimeString("nl-NL",{hour:"2-digit",minute:"2-digit"});
}

function elapsed(startAt:string,now:number){
  const seconds=Math.max(0,Math.floor((now-new Date(startAt).getTime())/1000));
  const hours=Math.floor(seconds/3600);
  const minutes=Math.floor((seconds%3600)/60);
  const rest=seconds%60;
  return [hours,minutes,rest].map(value=>value.toString().padStart(2,"0")).join(":");
}

export function OtherActiveVisits({visits,now}:{visits:AdminActiveVisitSummary[];now:number}){
  return <div className="recent other-active-visits">
    {visits.map(visit=><div className="recent__row" key={visit.id}>
      <span className="recent__icon recent__icon--active"><Icon name="car"/></span>
      <span className="recent__details">
        <span className="other-active-visits__identity">
          <strong>{visit.username}</strong>
          <LicensePlate value={visit.licensePlate}/>
        </span>
        <small>
          Gestart {time(visit.startAt)}
          {" · "}
          {visit.desiredEndAt?`tot ${time(visit.desiredEndAt)}`:"Open einde"}
        </small>
      </span>
      <span className="recent__meta"><strong>{elapsed(visit.startAt,now)}</strong></span>
    </div>)}
  </div>;
}
