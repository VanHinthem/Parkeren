import { useEffect,useState } from "react";
import { deleteNotification,getNotifications,markAllNotificationsRead,markNotificationRead,type InboxNotification } from "../../api/client";
import { Card } from "../../design/primitives/Card";
import { Icon } from "../../design/icons/Icon";
import "./NotificationsPage.css";

const labels:Record<InboxNotification["type"],string>={
  VisitStarted:"Parkeren gestart",
  VisitStopped:"Parkeren gestopt",
  ProviderContinuationSucceeded:"Parkeren voortgezet",
  ProviderContinuationAttentionRequired:"Parkeren vraagt aandacht",
  LongVisitWarning:"Langdurig parkeren",
  BudgetWarning:"Parkeerbudget waarschuwing"
};

type NotificationPayload=Record<string,unknown>;

function payloadValue<T>(payload:NotificationPayload,name:string):T|undefined{
  const key=Object.keys(payload).find(x=>x.toLowerCase()===name.toLowerCase());
  return key?payload[key] as T:undefined;
}

function notificationLabel(item:InboxNotification){
  if(item.type!=="ProviderContinuationAttentionRequired"||!item.payload)
    return labels[item.type];

  try{
    const payload=JSON.parse(item.payload) as NotificationPayload;
    return payloadValue<string>(payload,"Reason")==="ExternalStop"
      ?"Parkeeractie extern gestopt"
      :labels[item.type];
  }catch{
    return labels[item.type];
  }
}

function notificationDetails(item:InboxNotification,isAdmin:boolean){
  if(!item.payload)
    return item.type==="ProviderContinuationAttentionRequired"
      ?"Automatisch voortzetten is niet gelukt."
      :null;

  try{
    const payload=JSON.parse(item.payload) as NotificationPayload;

    if(item.type==="ProviderContinuationAttentionRequired")
      return payloadValue<string>(payload,"Reason")==="ExternalStop"
        ?"2Park heeft deze parkeeractie buiten de app gestopt."
        :"Automatisch voortzetten is niet gelukt.";

    if(item.type==="LongVisitWarning"){
      const visitor=payloadValue<string>(payload,"Visitor");
      const licensePlate=payloadValue<string>(payload,"LicensePlate");
      const startAt=payloadValue<string>(payload,"StartAt");
      const elapsedDuration=payloadValue<string>(payload,"ElapsedDuration");
      if(!licensePlate||!startAt||!elapsedDuration)return null;
      const visitorText=isAdmin&&visitor?`${visitor} · `:"";
      return `${visitorText}${licensePlate} · gestart ${new Date(startAt).toLocaleString("nl-NL",{dateStyle:"medium",timeStyle:"short"})} · ${elapsedDuration}`;
    }

    if(item.type==="BudgetWarning"){
      const threshold=payloadValue<number>(payload,"ThresholdPercentage");
      return typeof threshold==="number"?`${threshold}% van het parkeerbudget is gebruikt.`:null;
    }
  }catch{
    return null;
  }

  return null;
}

function notificationTarget(item:InboxNotification){
  switch(item.type){
    case "VisitStarted":
    case "ProviderContinuationSucceeded":
      return "/";
    case "VisitStopped":
    case "ProviderContinuationAttentionRequired":
    case "LongVisitWarning":
      return item.visitId?"/acties/"+item.visitId:"/meldingen";
    case "BudgetWarning":
    default:
      return "/meldingen";
  }
}

type Props={userRole:"Admin"|"Visitor";onUnreadCountChanged:(count:number)=>void;onNavigate:(path:string)=>void};

export function NotificationsPage({userRole,onUnreadCountChanged,onNavigate}:Props){
  const[items,setItems]=useState<InboxNotification[]>();
  const[error,setError]=useState<string|null>(null);
  const unread=items?.filter(x=>!x.isRead).length??0;

  useEffect(()=>{getNotifications().then(setItems).catch(()=>setError("Meldingen konden niet worden geladen."));},[]);
  useEffect(()=>{if(items)onUnreadCountChanged(unread);},[items,unread,onUnreadCountChanged]);

  async function read(item:InboxNotification){
    if(!item.isRead){
      await markNotificationRead(item.id);
      setItems(current=>current?.map(x=>x.id===item.id?{...x,isRead:true,readAt:new Date().toISOString()}:x));
    }
    onNavigate(notificationTarget(item));
  }

  async function remove(id:string){
    try{await deleteNotification(id);setItems(current=>current?.filter(x=>x.id!==id));}
    catch{setError("Melding kon niet worden verwijderd.");}
  }

  async function readAll(){
    try{await markAllNotificationsRead();const now=new Date().toISOString();setItems(current=>current?.map(x=>({...x,isRead:true,readAt:x.readAt??now})));}
    catch{setError("Meldingen konden niet als gelezen worden gemarkeerd.");}
  }

  if(error&&!items)return <Card><p role="alert" style={{margin:0}}>{error}</p></Card>;
  if(!items)return <Card>Meldingen laden…</Card>;

  return <div className="notification-inbox">
    <div className="notification-inbox__toolbar">
      <span>{unread===0?"Geen ongelezen meldingen":unread===1?"1 ongelezen melding":`${unread} ongelezen meldingen`}</span>
      {unread>0&&<button type="button" onClick={readAll}>Alles gelezen</button>}
    </div>
    {error&&<p role="alert" className="notification-inbox__error">{error}</p>}
    {items.length===0?<Card>Je hebt nog geen meldingen.</Card>:items.map(item=>
      <article key={item.id} className={`notification-item ${item.isRead?"":"notification-item--unread"}`}>
        <button type="button" className="notification-item__body" onClick={()=>read(item)}>
          <span className="notification-item__dot" aria-hidden="true"/>
          <span>
            <strong>{notificationLabel(item)}</strong>
            {notificationDetails(item,userRole==="Admin")&&<small>{notificationDetails(item,userRole==="Admin")}</small>}
            <time dateTime={item.createdAt}>{new Date(item.createdAt).toLocaleString("nl-NL",{dateStyle:"medium",timeStyle:"short"})}</time>
          </span>
        </button>
        <button type="button" className="notification-item__delete" aria-label="Melding verwijderen" onClick={()=>remove(item.id)}><Icon name="trash"/></button>
      </article>)}
  </div>;
}
