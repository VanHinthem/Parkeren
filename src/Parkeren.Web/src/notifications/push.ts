import { getPushPublicKey, registerPushSubscription, unregisterPushSubscription } from "../api/client";

function base64UrlToUint8Array(value:string):Uint8Array<ArrayBuffer>{
  const padding="=".repeat((4-value.length%4)%4);
  const base64=(value+padding).replace(/-/g,"+").replace(/_/g,"/");
  const raw=atob(base64);
  return Uint8Array.from(raw,char=>char.charCodeAt(0));
}

export function isPushSupported():boolean{
  return "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;
}

export async function enablePushNotifications():Promise<void>{
  if(!isPushSupported())throw new Error("Pushmeldingen worden niet ondersteund op dit apparaat.");

  const permission=await Notification.requestPermission();
  if(permission!=="granted")throw new Error("Toestemming voor pushmeldingen is niet gegeven.");

  const registration=await navigator.serviceWorker.ready;
  let subscription=await registration.pushManager.getSubscription();

  if(!subscription){
    const {publicKey}=await getPushPublicKey();
    subscription=await registration.pushManager.subscribe({
      userVisibleOnly:true,
      applicationServerKey:base64UrlToUint8Array(publicKey)
    });
  }

  await registerPushSubscription(subscription);
}

export async function disablePushNotifications():Promise<void>{
  if(!isPushSupported())return;
  const registration=await navigator.serviceWorker.ready;
  const subscription=await registration.pushManager.getSubscription();
  if(!subscription)return;

  await unregisterPushSubscription(subscription.endpoint);
  await subscription.unsubscribe();
}

export async function hasPushSubscription():Promise<boolean>{
  if(!isPushSupported()||Notification.permission!=="granted")return false;
  const registration=await navigator.serviceWorker.ready;
  return (await registration.pushManager.getSubscription())!==null;
}
