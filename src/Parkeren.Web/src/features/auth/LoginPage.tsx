import { useState,type FormEvent } from "react";
import { login,type AuthenticatedUser } from "../../api/client";
import { Alert } from "../../design/primitives/Alert"; import { Button } from "../../design/primitives/Button"; import { Card } from "../../design/primitives/Card"; import { Input } from "../../design/primitives/Input"; import "./LoginPage.css";
export function LoginPage({onLoggedIn}:{onLoggedIn:(user:AuthenticatedUser)=>void}){
 const [username,setUsername]=useState("");const [pin,setPin]=useState("");const [error,setError]=useState<string>();const [busy,setBusy]=useState(false);
 async function submit(e:FormEvent){e.preventDefault();setError(undefined);setBusy(true);try{onLoggedIn(await login(username,pin));}catch(x){setError(x instanceof Error?x.message:"Inloggen is mislukt.");}finally{setBusy(false);}}
 return <main className="login-page"><div className="login-page__brand"><strong>Parkeren</strong></div><Card><form className="login-form" onSubmit={submit}><div><h1>Welkom</h1><p>Log in om je bezoekersparkeren te beheren.</p></div>{error&&<Alert variant="error">{error}</Alert>}<Input label="Gebruikersnaam" name="username" autoComplete="username" value={username} onChange={e=>setUsername(e.target.value)} required/><Input label="PIN" name="pin" type="password" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} autoComplete="current-password" value={pin} onChange={e=>setPin(e.target.value.replace(/\D/g,"").slice(0,6))} required/><Button type="submit" disabled={busy||pin.length!==6}>{busy?"Inloggen…":"Inloggen"}</Button></form></Card></main>;
}
