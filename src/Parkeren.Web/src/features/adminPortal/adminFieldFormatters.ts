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
