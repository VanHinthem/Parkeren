import { useEffect } from "react";
import "./VisitEndTimeField.css";

type Props={
  startAt:Date;
  value:string|null;
  onChange:(value:string|null)=>void;
  allowOpenEnded:boolean;
  maxDurationMinutes:number|null;
  disabled?:boolean;
};

const shortDateFormatter=new Intl.DateTimeFormat("nl-NL",{
  weekday:"short",
  day:"numeric",
  month:"short"
});

const timeFormatter=new Intl.DateTimeFormat("nl-NL",{
  hour:"2-digit",
  minute:"2-digit",
  hourCycle:"h23"
});

function pad(value:number){
  return String(value).padStart(2,"0");
}

function inputDate(value:Date){
  return `${value.getFullYear()}-${pad(value.getMonth()+1)}-${pad(value.getDate())}`;
}

function inputTime(value:Date){
  return `${pad(value.getHours())}:${pad(value.getMinutes())}`;
}

function localDate(date:string,time:string){
  const[year,month,day]=date.split("-").map(Number);
  const[hour,minute]=time.split(":").map(Number);
  return new Date(year,month-1,day,hour,minute,0,0);
}

function minimumEndAt(startAt:Date){
  const value=new Date(startAt);
  value.setSeconds(0,0);
  value.setMinutes(value.getMinutes()+1);
  return value;
}

function maximumEndAt(startAt:Date,maxDurationMinutes:number|null){
  return maxDurationMinutes===null
    ? null
    : new Date(startAt.getTime()+maxDurationMinutes*60_000);
}

export function createDefaultVisitEndAt(startAt:Date,maxDurationMinutes:number|null){
  const preferredMinutes=maxDurationMinutes===null?240:Math.min(240,maxDurationMinutes);
  return new Date(startAt.getTime()+Math.max(1,preferredMinutes)*60_000).toISOString();
}

export function isVisitEndAtAllowed(startAt:Date,value:string|null,allowOpenEnded:boolean,maxDurationMinutes:number|null){
  if(value===null)return allowOpenEnded;
  const endAt=new Date(value);
  if(!Number.isFinite(endAt.getTime())||endAt<=startAt)return false;
  const maximum=maximumEndAt(startAt,maxDurationMinutes);
  return maximum===null||endAt<=maximum;
}

export function VisitEndTimeField({
  startAt,
  value,
  onChange,
  allowOpenEnded,
  maxDurationMinutes,
  disabled=false
}:Props){
  useEffect(()=>{
    if(value===null&&!allowOpenEnded)
      onChange(createDefaultVisitEndAt(startAt,maxDurationMinutes));
  },[allowOpenEnded,maxDurationMinutes,onChange,startAt,value]);

  const endAt=value===null?null:new Date(value);
  const maximum=maximumEndAt(startAt,maxDurationMinutes);
  const selectedDate=endAt??new Date(createDefaultVisitEndAt(startAt,maxDurationMinutes));

  function changePart(datePart:string,timePart:string){
    let next=localDate(datePart,timePart);
    const minimum=minimumEndAt(startAt);
    if(next<minimum)next=minimum;
    if(maximum!==null&&next>maximum)next=maximum;
    onChange(next.toISOString());
  }

  const selectedDatePart=inputDate(selectedDate);
  const minimumDatePart=inputDate(startAt);
  const maximumDatePart=maximum===null?undefined:inputDate(maximum);
  const minimumTimePart=selectedDatePart===minimumDatePart?inputTime(minimumEndAt(startAt)):undefined;
  const maximumTimePart=maximum!==null&&selectedDatePart===inputDate(maximum)?inputTime(maximum):undefined;

  return <div className="visit-end-time">
    <div className="visit-end-time__summary">
      <div className="visit-end-time__moment">
        <span>Start</span>
        <strong>{timeFormatter.format(startAt)}</strong>
        <small>{shortDateFormatter.format(startAt)}</small>
      </div>
      <div className="visit-end-time__moment">
        <span>Einde</span>
        {endAt
          ? <>
              <strong>{timeFormatter.format(endAt)}</strong>
              <small>{shortDateFormatter.format(endAt)}</small>
            </>
          : <>
              <strong>Open einde</strong>
              <small>Tot je zelf stopt</small>
            </>}
      </div>
    </div>

    {allowOpenEnded
      ? <fieldset className="visit-end-time__modes" disabled={disabled}>
          <legend>Eindmoment</legend>
          <label>
            <input
              type="radio"
              name="visit-end-mode"
              checked={value!==null}
              onChange={()=>onChange(createDefaultVisitEndAt(startAt,maxDurationMinutes))}
            />
            <span>Eindtijd</span>
          </label>
          <label>
            <input
              type="radio"
              name="visit-end-mode"
              checked={value===null}
              onChange={()=>onChange(null)}
            />
            <span>Open einde</span>
          </label>
        </fieldset>
      : null}

    {endAt
      ? <div className="visit-end-time__inputs">
          <label>
            <span>Datum</span>
            <input
              type="date"
              value={selectedDatePart}
              min={minimumDatePart}
              max={maximumDatePart}
              onChange={event=>changePart(event.target.value,inputTime(selectedDate))}
              disabled={disabled}
            />
          </label>
          <label>
            <span>Tijd</span>
            <input
              type="time"
              value={inputTime(selectedDate)}
              min={minimumTimePart}
              max={maximumTimePart}
              step="60"
              onChange={event=>changePart(selectedDatePart,event.target.value)}
              disabled={disabled}
            />
          </label>
        </div>
      : <p className="visit-end-time__open-hint">De parkeeractie blijft actief totdat je deze zelf stopt.</p>}
  </div>;
}
