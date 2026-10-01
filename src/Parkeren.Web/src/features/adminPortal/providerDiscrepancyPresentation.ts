import type { AdminProviderDiscrepancy } from "../../api/client";

export function providerDiscrepancyTypeLabel(type:AdminProviderDiscrepancy["type"]){
  switch(type){
    case "MissingProviderAction": return "Actie ontbreekt bij 2Park";
    case "ProviderActionStatusMismatch": return "Providerstatus wijkt af";
    case "ProviderActionEndMismatch": return "Eindtijd provideractie wijkt af";
    case "ExternalProviderAction": return "Externe 2Park-actie";
    case "BalanceMismatch": return "Saldo-afwijking";
  }
}

export function providerDiscrepancyContextLabel(item:AdminProviderDiscrepancy){
  if(item.localProviderActionState&&item.providerStatus)
    return `Lokaal: ${item.localProviderActionState} · 2Park: ${item.providerStatus}`;
  if(item.localProviderActionState)
    return `Lokaal: ${item.localProviderActionState}`;
  if(item.providerStatus)
    return `2Park: ${item.providerStatus}`;
  return "Geen aanvullende statuscontext";
}
