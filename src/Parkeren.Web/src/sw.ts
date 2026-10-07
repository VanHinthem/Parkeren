/// <reference lib="webworker" />

export {};

import { cleanupOutdatedCaches, precacheAndRoute } from "workbox-precaching";

type PushPayload = {
  title?: string;
  body?: string;
  url?: string;
  notificationId?: string;
};

const serviceWorker = globalThis as unknown as ServiceWorkerGlobalScope;

declare const self: ServiceWorkerGlobalScope & { __WB_MANIFEST: Array<{ url: string; revision?: string | null }> };
declare const __PARKEREN_NOTIFICATION_ICON__: string;

precacheAndRoute(self.__WB_MANIFEST,{cleanURLs:false});
cleanupOutdatedCaches();

serviceWorker.addEventListener("install",()=>serviceWorker.skipWaiting());
serviceWorker.addEventListener("activate",event=>event.waitUntil(serviceWorker.clients.claim()));

serviceWorker.addEventListener("push",(event:PushEvent)=>{
  let payload:PushPayload={};
  try{payload=event.data?.json() as PushPayload??{};}catch{payload={};}

  event.waitUntil(serviceWorker.registration.showNotification(payload.title??"Parkeren",{
    body:payload.body??"Je hebt een nieuwe parkeermelding.",
    icon:__PARKEREN_NOTIFICATION_ICON__,
    data:{
      url:payload.url??"/meldingen",
      notificationId:payload.notificationId
    }
  }));
});

serviceWorker.addEventListener("notificationclick",(event:NotificationEvent)=>{
  event.notification.close();
  const data=event.notification.data as {url?:string;notificationId?:string}|undefined;
  const target=new URL(data?.url??"/meldingen",serviceWorker.location.origin);
  const notificationId=data?.notificationId;

  event.waitUntil((async()=>{
    const clients=await serviceWorker.clients.matchAll({type:"window",includeUncontrolled:true});
    for(const client of clients){
      await client.focus();
      client.postMessage({type:"navigate",url:target.href,notificationId});
      return;
    }

    if(notificationId)target.searchParams.set("notificationId",notificationId);
    await serviceWorker.clients.openWindow(target.href);
  })());
});
