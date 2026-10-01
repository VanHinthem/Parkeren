import type { ActiveVisit } from "../api/client";

export function activeVisitPresentation(health:ActiveVisit["health"]){
  switch(health){
    case "AttentionRequired":
      return {label:"Parkeren vraagt aandacht",detail:"Providerstatus wijkt af",attention:true};
    case "Reconciling":
      return {label:"Providerstatus controleren",detail:"Reconciliatie bezig",attention:true};
    case "StopFailed":
      return {label:"Stoppen vraagt aandacht",detail:"Providerstop niet bevestigd",attention:true};
    default:
      return {label:"Actief parkeren",detail:"Actieve parkeeractie",attention:false};
  }
}

export function activeVisitHealthMessage(health:ActiveVisit["health"]){
  switch(health){
    case "AttentionRequired":
      return "De parkeerprovider meldt een afwijking. Controleer je melding; verlengen is geblokkeerd totdat de situatie is afgehandeld.";
    case "Reconciling":
      return "De providerstatus wordt gecontroleerd. Acties zijn tijdelijk beperkt.";
    case "StopFailed":
      return "Stoppen is niet bevestigd door de parkeerprovider. Controleer je melding of probeer later opnieuw.";
    default:
      return null;
  }
}

export function canStopActiveVisit(visit:ActiveVisit){
  return (visit.status==="Active"&&visit.health!=="Reconciling")||
    (visit.status==="Stopping"&&visit.health==="Reconciling");
}

export function canExtendActiveVisit(visit:ActiveVisit){
  return visit.status==="Active"&&visit.health==="Healthy";
}
