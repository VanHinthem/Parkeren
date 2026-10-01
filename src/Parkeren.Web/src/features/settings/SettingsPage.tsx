import { useEffect,useState,type FormEvent } from "react";
import { disablePushNotifications,enablePushNotifications,hasPushSubscription,isPushSupported } from "../../notifications/push";
import { changePin,logout,type AuthenticatedUser } from "../../api/client";
import { Alert } from "../../design/primitives/Alert";
import { Button } from "../../design/primitives/Button";
import { Card } from "../../design/primitives/Card";
import { Input } from "../../design/primitives/Input";
import { ThemeSelector } from "../../components/ThemeSelector";
import "./SettingsPage.css";

export function SettingsPage({user,onLoggedOut}:{user:AuthenticatedUser;onLoggedOut:()=>void}){
  const[error,setError]=useState<string>();
  const[message,setMessage]=useState<string>();
  const[currentPin,setCurrentPin]=useState("");
  const[newPin,setNewPin]=useState("");
  const[confirmPin,setConfirmPin]=useState("");
  const[pushBusy,setPushBusy]=useState(false);
  const[pushEnabled,setPushEnabled]=useState<boolean|undefined>(undefined);

  useEffect(()=>{
    let active=true;

    async function refreshPushStatus(){
      try{
        const enabled=await hasPushSubscription();
        if(active)setPushEnabled(enabled);
      }catch{
        if(active)setPushEnabled(false);
      }
    }

    void refreshPushStatus();
    const onVisible=()=>{if(document.visibilityState==="visible")void refreshPushStatus();};
    document.addEventListener("visibilitychange",onVisible);
    return()=>{active=false;document.removeEventListener("visibilitychange",onVisible);};
  },[]);

  async function signOut(){
    setError(undefined);
    try{
      await logout();
      onLoggedOut();
      history.replaceState({},"","/");
    }catch(e){
      setError(e instanceof Error?e.message:"Uitloggen is mislukt.");
    }
  }

  async function enablePush(){
    setError(undefined);
    setMessage(undefined);
    setPushBusy(true);
    try{
      await enablePushNotifications();
      setPushEnabled(await hasPushSubscription());
      setMessage("Pushmeldingen zijn ingeschakeld op dit apparaat.");
    }catch(e){
      setError(e instanceof Error?e.message:"Pushmeldingen konden niet worden ingeschakeld.");
    }finally{
      setPushBusy(false);
    }
  }

  async function disablePush(){
    setError(undefined);
    setMessage(undefined);
    setPushBusy(true);
    try{
      await disablePushNotifications();
      setPushEnabled(false);
      setMessage("Pushmeldingen zijn uitgeschakeld op dit apparaat.");
    }catch(e){
      setError(e instanceof Error?e.message:"Pushmeldingen konden niet worden uitgeschakeld.");
    }finally{
      setPushBusy(false);
    }
  }

  async function submit(e:FormEvent){
    e.preventDefault();
    setError(undefined);
    setMessage(undefined);
    if(newPin!==confirmPin){
      setError("De nieuwe PIN-codes zijn niet gelijk.");
      return;
    }
    try{
      await changePin(currentPin,newPin);
      setCurrentPin("");
      setNewPin("");
      setConfirmPin("");
      setMessage("Je PIN is gewijzigd. Andere actieve sessies zijn ingetrokken.");
    }catch(e){
      setError(e instanceof Error?e.message:"PIN wijzigen is mislukt.");
    }
  }

  const pushSupported=isPushSupported();

  return <div className="settings-grid">
    {error&&<Alert tone="danger">{error}</Alert>}
    {message&&<Alert tone="info">{message}</Alert>}

    <Card className="settings-card">
      <h2>Account</h2>
      <div className="settings-account">
        <div>
          <strong>{user.username}</strong>
          <span>{user.role==="Admin"?"Beheerder":"Bezoeker"}</span>
        </div>
        <Button variant="secondary" onClick={signOut}>Uitloggen</Button>
      </div>
    </Card>

    <Card className="settings-card">
      <h2>Weergave</h2>
      <ThemeSelector/>
    </Card>

    <Card className="settings-card">
      <h2>Meldingen</h2>
      {!pushSupported
        ? <p className="settings-copy">Pushmeldingen worden niet ondersteund op dit apparaat.</p>
        : <>
            <div className="push-status">
              <span className={`push-status__dot ${pushEnabled===true?"push-status__dot--enabled":pushEnabled===false?"push-status__dot--disabled":""}`} aria-hidden="true"/>
              <div>
                <strong>{pushEnabled===undefined?"Status controleren…":pushEnabled?"Pushmeldingen ingeschakeld":"Pushmeldingen uitgeschakeld"}</strong>
                <span>{pushEnabled?"Je ontvangt belangrijke parkeermeldingen ook als de app niet open staat.":"Schakel pushmeldingen in om belangrijke parkeermeldingen te ontvangen."}</span>
              </div>
            </div>
            <Button
              variant="secondary"
              onClick={pushEnabled?disablePush:enablePush}
              disabled={pushBusy||pushEnabled===undefined}
            >
              {pushBusy?"Bezig…":pushEnabled?"Pushmeldingen uitschakelen":"Pushmeldingen inschakelen"}
            </Button>
          </>}
    </Card>

    <Card className="settings-card settings-card--pin">
      <h2>PIN wijzigen</h2>
      <form className="settings-pin-form" onSubmit={submit}>
        <Input label="Huidige PIN" type="password" inputMode="numeric" maxLength={6} autoComplete="current-password" value={currentPin} onChange={e=>setCurrentPin(e.target.value.replace(/\D/g,"").slice(0,6))}/>
        <Input label="Nieuwe PIN" type="password" inputMode="numeric" maxLength={6} autoComplete="new-password" value={newPin} onChange={e=>setNewPin(e.target.value.replace(/\D/g,"").slice(0,6))}/>
        <Input label="Nieuwe PIN herhalen" type="password" inputMode="numeric" maxLength={6} autoComplete="new-password" value={confirmPin} onChange={e=>setConfirmPin(e.target.value.replace(/\D/g,"").slice(0,6))}/>
        <Button disabled={currentPin.length!==6||newPin.length!==6||confirmPin.length!==6}>PIN wijzigen</Button>
      </form>
    </Card>
  </div>;
}
