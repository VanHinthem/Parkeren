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

export type UserSummary={id:string;username:string;role:"Visitor"|"Admin";isActive:boolean};
export type VehicleSummary={id:string;licensePlate:string;displayName:string|null;isActive:boolean};
async function json<T>(response:Response):Promise<T>{if(!response.ok)throw new Error("De bewerking is mislukt.");return response.json() as Promise<T>;}
export async function getUsers(){return json<UserSummary[]>(await apiFetch("/api/admin/users"));}
export async function createUser(username:string,pin:string){return json<UserSummary>(await apiFetch("/api/admin/users",{method:"POST",body:JSON.stringify({username,pin,role:"Visitor"})}));}
export async function setUserActive(id:string,isActive:boolean){const r=await apiFetch(`/api/admin/users/${id}/active`,{method:"PUT",body:JSON.stringify({isActive})});if(!r.ok)throw new Error("Gebruiker kon niet worden gewijzigd.");}
export async function getVehicles(){return json<VehicleSummary[]>(await apiFetch("/api/admin/vehicles"));}
export async function createVehicle(licensePlate:string,displayName?:string){return json<VehicleSummary>(await apiFetch("/api/admin/vehicles",{method:"POST",body:JSON.stringify({licensePlate,displayName:displayName||null})}));}
export async function setVehicleActive(id:string,isActive:boolean){const r=await apiFetch(`/api/admin/vehicles/${id}/active`,{method:"PUT",body:JSON.stringify({isActive})});if(!r.ok)throw new Error("Voertuig kon niet worden gewijzigd.");}
export async function assignVehicle(userId:string,vehicleId:string){const r=await apiFetch(`/api/admin/users/${userId}/vehicles/${vehicleId}`,{method:"PUT"});if(!r.ok)throw new Error("Voertuig kon niet worden toegewezen.");}

export async function getAuthorizedVehicles(userId?:string){return json<VehicleSummary[]>(await apiFetch(userId?`/api/admin/users/${userId}/vehicles`:"/api/vehicles"));}
export async function unassignVehicle(userId:string,vehicleId:string){const r=await apiFetch(`/api/admin/users/${userId}/vehicles/${vehicleId}`,{method:"DELETE"});if(!r.ok)throw new Error("Toewijzing kon niet worden verwijderd.");}
