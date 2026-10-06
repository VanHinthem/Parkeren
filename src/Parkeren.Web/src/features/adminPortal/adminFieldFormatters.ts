export type AdminVisitStatus="Starting"|"Active"|"Stopping"|"Completed"|"Cancelled";
export type AdminRecordStatus="Active"|"Inactive"|"Archived";
export type AdminStatusTone="neutral"|"active"|"warning"|"danger";
export type AdminProviderBalanceUnit="Unknown"|"Euro"|"Minute"|"Times";

export function formatAdminDuration(value:number|null,unavailable="Niet beschikbaar"){
  if(value===null)return unavailable;
  if(value<60)return `${value} min`;
  const hours=Math.floor(value/60);
  const minutes=value%60;
  return minutes===0?`${hours} u`:`${hours} u ${minutes} min`;
}

export function formatAdminDateTime(value:string|null,unavailable="—"){
  return value
    ? new Date(value).toLocaleString("nl-NL",{dateStyle:"short",timeStyle:"short"}).replace(",","")
    : unavailable;
}

export function formatAdminDate(value:string|null,unavailable="—"){
  return value
    ? new Date(value).toLocaleDateString("nl-NL",{dateStyle:"medium"})
    : unavailable;
}

export function formatAdminNumber(value:number){
  return new Intl.NumberFormat("nl-NL").format(value);
}

export function formatAdminCapacity(used:number,total:number){
  return `${formatAdminNumber(used)} van ${formatAdminNumber(total)}`;
}

export function formatAdminMoney(value:number|null,unavailable="Niet beschikbaar"){
  return value===null
    ? unavailable
    : new Intl.NumberFormat("nl-NL",{style:"currency",currency:"EUR"}).format(value);
}

export function formatAdminBoolean(value:boolean){
  return value?"Ja":"Nee";
}

export function formatAdminProviderBalance(value:number|null,unit:AdminProviderBalanceUnit|null,unavailable="Niet beschikbaar"){
  if(value===null||unit===null)return unavailable;
  switch(unit){
    case "Euro": return formatAdminMoney(value,unavailable);
    case "Minute": return formatAdminDuration(value,unavailable);
    case "Times": return `${formatAdminNumber(value)} ${value===1?"keer":"keer"}`;
    case "Unknown": return formatAdminNumber(value);
  }
}

export function formatAdminVisitStatus(status:AdminVisitStatus){
  switch(status){
    case "Starting": return "Wordt gestart";
    case "Active": return "Actief";
    case "Stopping": return "Wordt gestopt";
    case "Completed": return "Afgerond";
    case "Cancelled": return "Geannuleerd";
  }
}

export function formatAdminRecordStatus(status:AdminRecordStatus){
  switch(status){
    case "Active": return "Actief";
    case "Inactive": return "Inactief";
    case "Archived": return "Gearchiveerd";
  }
}

export function formatAdminProviderActionStatus(status:string){
  switch(status.toLowerCase()){
    case "planned": return "Gepland";
    case "scheduled": return "Gepland";
    case "starting": return "Wordt gestart";
    case "active": return "Actief";
    case "stopping": return "Wordt gestopt";
    case "stopped": return "Gestopt";
    case "completed": return "Afgerond";
    case "failed": return "Mislukt";
    case "cancelled": return "Geannuleerd";
    default: return status;
  }
}

export function adminProviderActionStatusTone(status:string):AdminStatusTone{
  switch(status.toLowerCase()){
    case "active": return "active";
    case "starting":
    case "stopping": return "warning";
    case "failed": return "danger";
    default: return "neutral";
  }
}
