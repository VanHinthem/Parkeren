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
