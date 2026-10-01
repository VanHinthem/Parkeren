export type AuthenticatedUser = { id: string; username: string; role: "Visitor" | "Admin" };
let csrfToken: string | null = null;
async function getCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken;
  const response = await fetch("/api/auth/csrf", { credentials: "same-origin" });
  if (!response.ok) throw new Error("CSRF-token kon niet worden opgehaald.");
  csrfToken = ((await response.json()) as { token: string }).token;
  return csrfToken;
}
export async function apiFetch(input: string, init: RequestInit = {}): Promise<Response> {
  const method = (init.method ?? "GET").toUpperCase();
  const headers = new Headers(init.headers);
  if (!["GET","HEAD","OPTIONS","TRACE"].includes(method)) headers.set("X-CSRF-TOKEN", await getCsrfToken());
  if (init.body && !headers.has("Content-Type")) headers.set("Content-Type","application/json");
  return fetch(input,{...init,headers,credentials:"same-origin"});
}
export async function login(username:string,pin:string):Promise<AuthenticatedUser>{
 const response=await fetch("/api/auth/login",{method:"POST",headers:{"Content-Type":"application/json"},credentials:"same-origin",body:JSON.stringify({username,pin})});
 if(!response.ok) throw new Error("Gebruikersnaam of PIN is onjuist.");
 csrfToken=null; return response.json() as Promise<AuthenticatedUser>;
}
export async function getCurrentUser():Promise<AuthenticatedUser|null>{
 const response=await fetch("/api/auth/me",{credentials:"same-origin"});
 if(response.status===401)return null;
 if(!response.ok)throw new Error("Sessie kon niet worden gecontroleerd.");
 return response.json() as Promise<AuthenticatedUser>;
}

export type ActiveVisit={
  id:string;
  startOperationId:string;
  userId:string;
  vehicleId:string;
  startAt:string;
  desiredEndAt:string|null;
  actualEndAt:string|null;
  status:"Starting"|"Active"|"Stopping"|"Completed"|"Cancelled";
  health:"Healthy"|"Unknown"|"Reconciling";
};
export async function getActiveVisit():Promise<ActiveVisit|null>{
  const response=await apiFetch("/api/visits/active");
  if(response.status===404)return null;
  return json<ActiveVisit>(response);
}

export type ParkingPolicy={maxPaidParkingDurationMinutes:number;maxVisitElapsedDurationMinutes:number|null;allowAutoExtension:boolean;allowManualStop:boolean};
export async function getParkingPolicy():Promise<ParkingPolicy>{
  return json<ParkingPolicy>(await apiFetch("/api/visits/policy"));
}
export type VisitCapacity={used:number;total:number};
export async function getVisitCapacity():Promise<VisitCapacity>{
  return json<VisitCapacity>(await apiFetch("/api/visits/capacity"));
}

export async function getRecentVisits():Promise<ActiveVisit[]>{
  return json<ActiveVisit[]>(await apiFetch("/api/visits/recent"));
}
export async function getVisitHistory():Promise<ActiveVisit[]>{
  return json<ActiveVisit[]>(await apiFetch("/api/visits/history"));
}
export async function getVisit(visitId:string):Promise<ActiveVisit|null>{
  const response=await apiFetch(`/api/visits/${visitId}`);
  if(response.status===404)return null;
  if(response.status===403)throw new Error("Je hebt geen toegang tot deze parkeeractie.");
  return json<ActiveVisit>(response);
}

async function visitError(response:Response,fallback:string):Promise<Error>{
  try{const body=await response.json() as {error?:string;detail?:string};const message=body.error??body.detail;if(message)return new Error(message);}catch{}
  return new Error(`${fallback} (HTTP ${response.status}).`);
}

export type StartVisitResult={visit:ActiveVisit;reconciliationRequired:boolean};
export async function startVisit(vehicleId:string,desiredEndAt:string|null,operationId:string,ownerUserId?:string):Promise<StartVisitResult>{
  const response=await apiFetch("/api/visits/start",{
    method:"POST",
    body:JSON.stringify({operationId,vehicleId,desiredEndAt,ownerUserId:ownerUserId??null})
  });
  if(!response.ok)throw await visitError(response,"Parkeeractie kon niet worden gestart");
  const result=await response.json() as {visit:ActiveVisit};
  return {visit:result.visit,reconciliationRequired:response.status===202};
}

export type StopVisitResult={visit:ActiveVisit;reconciliationRequired:boolean};
export async function stopVisit(visitId:string,operationId:string):Promise<StopVisitResult>{
  const response=await apiFetch(`/api/visits/${visitId}/stop`,{
    method:"POST",
    body:JSON.stringify({operationId})
  });
  if(!response.ok)throw await visitError(response,"Parkeeractie kon niet worden gestopt");
  const result=await response.json() as {visit:ActiveVisit};
  return {visit:result.visit,reconciliationRequired:response.status===202};
}

export type ChangeVisitEndTimeResult={visit:ActiveVisit;reconciliationRequired:boolean};
export async function changeVisitEndTime(
  visitId:string,
  operationId:string,
  desiredEndAt:string|null
):Promise<ChangeVisitEndTimeResult>{
  const response=await apiFetch(`/api/visits/${visitId}/end-time`,{
    method:"PUT",
    body:JSON.stringify({operationId,desiredEndAt})
  });
  if(!response.ok)throw await visitError(response,"Eindtijd kon niet worden gewijzigd");
  const result=await response.json() as {visit:ActiveVisit};
  return {visit:result.visit,reconciliationRequired:response.status===202};
}

export type UserSummary={id:string;username:string;role:"Visitor"|"Admin";isActive:boolean};
export type VehicleSummary={id:string;licensePlate:string;displayName:string|null;isActive:boolean};
export type AdminActiveVisitSummary={
  id:string;
  userId:string;
  username:string;
  vehicleId:string;
  licensePlate:string;
  startAt:string;
  desiredEndAt:string|null;
  status:ActiveVisit["status"];
  health:ActiveVisit["health"];
  paidDurationMinutes:number|null;
};
export type AdminDashboardSummary={used:number;total:number;activeVisits:AdminActiveVisitSummary[]};
export type AdminParkingPolicySummary={
  maxPaidParkingDurationMinutes:number|null;
  maxVisitElapsedDurationMinutes:number|null;
  allowVisitExtension:boolean;
  allowOpenEndedVisits:boolean;
  maxConcurrentVisits:number;
};
export async function getAdminDashboard(){return json<AdminDashboardSummary>(await apiFetch("/api/admin/dashboard"));}
export async function getAdminUserParkingPolicy(userId:string){
  const response=await apiFetch(`/api/admin/users/${userId}/parking-policy`);
  if(response.status===404)return null;
  return json<AdminParkingPolicySummary>(response);
}

export type AdminVisitSummary={
  id:string;
  userId:string;
  username:string;
  vehicleId:string;
  licensePlate:string;
  startedByUserId:string;
  startedByUsername:string;
  startAt:string;
  desiredEndAt:string|null;
  actualEndAt:string|null;
  status:ActiveVisit["status"];
  health:ActiveVisit["health"];
  paidDurationMinutes:number|null;
};
export type AdminProviderParkingActionSummary={
  id:string;
  providerActionId:string|null;
  plannedStartAt:string;
  plannedEndAt:string;
  actualStartAt:string|null;
  actualEndAt:string|null;
  providerStatus:string|null;
  state:"Planned"|"Starting"|"Scheduled"|"Active"|"Stopping"|"Stopped"|"Completed"|"Failed";
  health:"Healthy"|"Unknown"|"Reconciling";
};
export type AdminProviderOperationSummary={
  id:string;
  operationId:string;
  providerParkingActionId:string|null;
  parentOperationId:string|null;
  type:"Start"|"ContinueStart"|"Extend"|"Stop";
  status:"Pending"|"InProgress"|"Succeeded"|"Failed"|"Unknown"|"Reconciling";
  attemptCount:number;
  lastErrorCode:string|null;
  createdAt:string;
  attemptStartedAt:string|null;
  requestedEndAt:string|null;
  completedAt:string|null;
};
export type AdminVisitEndTimeChangeSummary={
  id:string;
  operationId:string;
  actorUserId:string;
  actorUsername:string;
  previousDesiredEndAt:string|null;
  requestedDesiredEndAt:string|null;
  createdAt:string;
  result:"Pending"|"Applied"|"Rejected";
};
export type AdminRuleSetSummary={
  id:string;
  validFrom:string;
  validUntil:string|null;
  maxProviderActionDurationMinutes:number;
  continuation:"ExtendAction"|"StartNewAction";
  publicHolidaysAreFree:boolean;
};
export type AdminVisitDetail={
  visit:AdminVisitSummary;
  policySnapshot:{
    maxPaidParkingDurationMinutes:number|null;
    maxVisitElapsedDurationMinutes:number|null;
    allowVisitExtension:boolean;
    allowOpenEndedVisits:boolean;
  };
  providerActions:AdminProviderParkingActionSummary[];
  providerOperations:AdminProviderOperationSummary[];
  endTimeChanges:AdminVisitEndTimeChangeSummary[];
  relevantRuleSets:AdminRuleSetSummary[];
};
export type AdminVisitFilters={
  userId?:string;
  licensePlate?:string;
  from?:string;
  to?:string;
  status?:ActiveVisit["status"];
};
export async function getAdminVisits(filters:AdminVisitFilters={}){
  const params=new URLSearchParams();
  if(filters.userId)params.set("userId",filters.userId);
  if(filters.licensePlate)params.set("licensePlate",filters.licensePlate);
  if(filters.from)params.set("from",filters.from);
  if(filters.to)params.set("to",filters.to);
  if(filters.status)params.set("status",filters.status);
  const query=params.toString();
  return json<AdminVisitSummary[]>(await apiFetch(`/api/admin/visits${query?`?${query}`:""}`));
}
export async function getAdminVisit(visitId:string){
  const response=await apiFetch(`/api/admin/visits/${visitId}`);
  if(response.status===404)return null;
  return json<AdminVisitDetail>(response);
}
async function json<T>(response:Response):Promise<T>{if(!response.ok)throw new Error(`De bewerking is mislukt (HTTP ${response.status}).`);return response.json() as Promise<T>;}
export async function getUsers(){return json<UserSummary[]>(await apiFetch("/api/admin/users"));}
export async function createUser(username:string,pin:string){return json<UserSummary>(await apiFetch("/api/admin/users",{method:"POST",body:JSON.stringify({username,pin,role:"Visitor"})}));}
export async function setUserActive(id:string,isActive:boolean){const r=await apiFetch(`/api/admin/users/${id}/active`,{method:"PUT",body:JSON.stringify({isActive})});if(!r.ok)throw new Error("Gebruiker kon niet worden gewijzigd.");}
export async function getVehicles(){return json<VehicleSummary[]>(await apiFetch("/api/admin/vehicles"));}
export async function createVehicle(licensePlate:string,displayName?:string){return json<VehicleSummary>(await apiFetch("/api/admin/vehicles",{method:"POST",body:JSON.stringify({licensePlate,displayName:displayName||null})}));}
export async function setVehicleActive(id:string,isActive:boolean){const r=await apiFetch(`/api/admin/vehicles/${id}/active`,{method:"PUT",body:JSON.stringify({isActive})});if(!r.ok)throw new Error("Voertuig kon niet worden gewijzigd.");}
export async function assignVehicle(userId:string,vehicleId:string){const r=await apiFetch(`/api/admin/users/${userId}/vehicles/${vehicleId}`,{method:"PUT"});if(!r.ok)throw new Error("Voertuig kon niet worden toegewezen.");}

export async function getAssignedVehicles(userId:string){return json<VehicleSummary[]>(await apiFetch(`/api/admin/users/${userId}/vehicles`));}
export async function getAuthorizedVehicles(){return json<VehicleSummary[]>(await apiFetch("/api/vehicles"));}
export async function unassignVehicle(userId:string,vehicleId:string){const r=await apiFetch(`/api/admin/users/${userId}/vehicles/${vehicleId}`,{method:"DELETE"});if(!r.ok)throw new Error("Toewijzing kon niet worden verwijderd.");}

export async function logout(){const response=await apiFetch("/api/auth/logout",{method:"POST"});if(!response.ok)throw new Error("Uitloggen is mislukt.");csrfToken=null;}

export async function changePin(currentPin:string,newPin:string){const r=await apiFetch("/api/auth/change-pin",{method:"POST",body:JSON.stringify({currentPin,newPin})});if(!r.ok)throw new Error("PIN kon niet worden gewijzigd.");}
export async function resetUserPin(userId:string,newPin:string){const r=await apiFetch(`/api/admin/users/${userId}/reset-pin`,{method:"POST",body:JSON.stringify({newPin})});if(!r.ok)throw new Error("PIN kon niet worden gereset.");}
export async function revokeUserSessions(userId:string){const r=await apiFetch(`/api/admin/users/${userId}/revoke-sessions`,{method:"POST"});if(!r.ok)throw new Error("Sessies konden niet worden ingetrokken.");}

export type InboxNotification={
  id:string;
  type:"VisitStarted"|"VisitStopped"|"ProviderContinuationSucceeded"|"ProviderContinuationAttentionRequired"|"LongVisitWarning"|"BudgetWarning";
  visitId:string|null;
  payload:string|null;
  createdAt:string;
  readAt:string|null;
  isRead:boolean;
};
export async function getNotifications(){return json<InboxNotification[]>(await apiFetch("/api/notifications"));}
export async function getNotificationUnreadCount(){return json<{count:number}>(await apiFetch("/api/notifications/unread-count"));}
export async function markNotificationRead(id:string){const r=await apiFetch(`/api/notifications/${id}/read`,{method:"POST"});if(!r.ok)throw new Error("Melding kon niet als gelezen worden gemarkeerd.");}
export async function markAllNotificationsRead(){const r=await apiFetch("/api/notifications/read-all",{method:"POST"});if(!r.ok)throw new Error("Meldingen konden niet als gelezen worden gemarkeerd.");}
export async function deleteNotification(id:string){const r=await apiFetch(`/api/notifications/${id}`,{method:"DELETE"});if(!r.ok)throw new Error("Melding kon niet worden verwijderd.");}

export async function getPushPublicKey(){return json<{publicKey:string}>(await apiFetch("/api/notifications/push-public-key"));}
export async function registerPushSubscription(subscription:PushSubscription){
  const key=subscription.getKey("p256dh"),auth=subscription.getKey("auth");
  if(!key||!auth)throw new Error("Browser heeft geen geldige push-sleutels geleverd.");
  const encode=(value:ArrayBuffer)=>btoa(String.fromCharCode(...new Uint8Array(value)));
  const r=await apiFetch("/api/notifications/push-subscriptions",{method:"POST",body:JSON.stringify({endpoint:subscription.endpoint,p256dh:encode(key),auth:encode(auth)})});
  if(!r.ok)throw new Error("Pushmeldingen konden niet worden geregistreerd.");
}
export async function unregisterPushSubscription(endpoint:string){
  const r=await apiFetch("/api/notifications/push-subscriptions",{method:"DELETE",body:JSON.stringify({endpoint})});
  if(!r.ok)throw new Error("Pushregistratie kon niet worden verwijderd.");
}
