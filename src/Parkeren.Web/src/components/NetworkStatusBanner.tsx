import { useEffect,useState } from "react";

export function NetworkStatusBanner(){
  const[online,setOnline]=useState(()=>navigator.onLine);

  useEffect(()=>{
    const update=()=>setOnline(navigator.onLine);
    window.addEventListener("online",update);
    window.addEventListener("offline",update);
    return()=>{
      window.removeEventListener("online",update);
      window.removeEventListener("offline",update);
    };
  },[]);

  if(online)return null;
  return <div className="network-status" role="status" aria-live="polite">Offline. Getoonde parkeerinformatie kan verouderd zijn; starten, stoppen en verlengen zijn niet beschikbaar.</div>;
}