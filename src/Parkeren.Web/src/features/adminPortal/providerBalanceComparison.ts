export type ProviderBalanceComparisonInput={
  providerUnit:string|undefined;
  providerRemainingBalance:number|undefined;
  providerBalanceIsStale:boolean;
  localRemainingPaidDurationMinutes:number|null;
  localUsageIsComplete:boolean;
  periodValidFrom:string|undefined;
  periodValidUntil:string|undefined;
  nowMs?:number;
};

export function providerBalanceDiscrepancyMinutes(input:ProviderBalanceComparisonInput){
  if(input.providerUnit!=="Minute"||
     input.providerRemainingBalance===undefined||
     input.providerBalanceIsStale||
     input.localRemainingPaidDurationMinutes===null||
     !input.localUsageIsComplete||
     !input.periodValidFrom||
     !input.periodValidUntil)
    return null;

  const from=new Date(input.periodValidFrom).getTime();
  const until=new Date(input.periodValidUntil).getTime();
  const now=input.nowMs??Date.now();
  if(!Number.isFinite(from)||!Number.isFinite(until)||now<from||now>=until)
    return null;

  return input.providerRemainingBalance-input.localRemainingPaidDurationMinutes;
}
