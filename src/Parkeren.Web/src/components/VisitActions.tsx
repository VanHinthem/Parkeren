import { useMemo, useState } from "react";
import { Icon } from "../design/icons/Icon";
import "./VisitActions.css";

type Props={
  onStop?:()=>void;
  stopping?:boolean;
  onExtend?:(hours:number)=>void;
  extending?:boolean;
  error?:string|null;
  maxExtensionMinutes?:number|null;
  allowManualStop?:boolean;
  manualStopDisabledMessage?:string;
  allowExtension?:boolean;
  extensionDisabledMessage?:string;
};

export function VisitActions({
  onStop,
  stopping=false,
  onExtend,
  extending=false,
  error,
  maxExtensionMinutes=240,
  allowManualStop=true,
  manualStopDisabledMessage,
  allowExtension=true,
  extensionDisabledMessage
}:Props){
  const[confirmStop,setConfirmStop]=useState(false);
  const[extendHours,setExtendHours]=useState("1");
  const[showExtend,setShowExtend]=useState(false);

  const maxExtensionHours=maxExtensionMinutes===null
    ? null
    : Math.max(1,Math.floor(maxExtensionMinutes/60));
  const extensionOptions=useMemo(
    ()=>maxExtensionHours===null?[]:Array.from({length:maxExtensionHours},(_,index)=>index+1),
    [maxExtensionHours]
  );

  const extensionControl=maxExtensionHours===null
    ? <input
        type="number"
        min="1"
        step="1"
        value={extendHours}
        onChange={event=>setExtendHours(event.target.value)}
        disabled={extending||stopping||!allowExtension}
      />
    : <select
        value={extendHours}
        onChange={event=>setExtendHours(event.target.value)}
        disabled={extending||stopping||!allowExtension}
      >
        {extensionOptions.map(hours=><option key={hours} value={hours}>{hours} uur</option>)}
      </select>;

  return <>
    <div className="visit-actions">
      <button className="visit-action visit-action--stop" onClick={()=>setConfirmStop(true)} disabled={stopping||extending||!allowManualStop}>
        <span className="stop-icon">■</span>
        <strong>{stopping?"Stoppen…":"Stoppen"}</strong>
      </button>
      <button className="visit-action visit-action--extend" onClick={()=>setShowExtend(true)} disabled={extending||stopping||!allowExtension}>
        <Icon name="clock"/>
        <span><strong>{extending?"Verlengen…":"Verlengen"}</strong><small>Eindtijd wijzigen</small></span>
      </button>
    </div>

    {!allowManualStop&&manualStopDisabledMessage?<p className="visit-actions__error" role="status">{manualStopDisabledMessage}</p>:null}
    {!allowExtension&&extensionDisabledMessage?<p className="visit-actions__error" role="status">{extensionDisabledMessage}</p>:null}
    {error?<p className="visit-actions__error" role="alert">{error}</p>:null}

    {showExtend?<div className="visit-confirm" role="dialog" aria-modal="true" aria-label="Parkeeractie verlengen">
      <strong>Parkeeractie verlengen</strong>
      <label className="visit-confirm__field">
        <span>Extra tijd{maxExtensionHours===null?" (uren)":""}</span>
        {extensionControl}
      </label>
      <div className="visit-confirm__actions">
        <button type="button" onClick={()=>setShowExtend(false)} disabled={extending}>Annuleren</button>
        <button
          type="button"
          onClick={()=>{
            const hours=Number(extendHours);
            if(!Number.isInteger(hours)||hours<1||(maxExtensionHours!==null&&hours>maxExtensionHours))
              return;
            setShowExtend(false);
            onExtend?.(hours);
          }}
          disabled={extending||stopping||!allowExtension}
        >
          Verlengen
        </button>
      </div>
    </div>:null}

    {confirmStop?<div className="visit-confirm" role="dialog" aria-modal="true" aria-label="Parkeeractie stoppen">
      <strong>Parkeeractie stoppen?</strong>
      <p>Weet je zeker dat je deze parkeeractie wilt stoppen?</p>
      <div className="visit-confirm__actions">
        <button type="button" onClick={()=>setConfirmStop(false)} disabled={stopping}>Annuleren</button>
        <button type="button" className="visit-confirm__stop" onClick={()=>{setConfirmStop(false);onStop?.();}} disabled={stopping||extending||!allowManualStop}>Stop parkeren</button>
      </div>
    </div>:null}
  </>;
}
