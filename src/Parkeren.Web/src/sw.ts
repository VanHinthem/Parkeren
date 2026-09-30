/// <reference lib="webworker" />

export {};

import { cleanupOutdatedCaches, precacheAndRoute } from "workbox-precaching";

type PushPayload = {
  title?: string;
  body?: string;
  url?: string;
};

const serviceWorker = globalThis as unknown as ServiceWorkerGlobalScope;

precacheAndRoute((serviceWorker as ServiceWorkerGlobalScope & { __WB_MANIFEST: Array<{ url: string; revision?: string | null }> }).__WB_MANIFEST);
cleanupOutdatedCaches();

serviceWorker.addEventListener("push",(event:PushEvent)=>{
  let payload:PushPayload={};
  try{payload=event.data?.json() as PushPayload??{};}catch{payload={};}

  event.waitUntil(serviceWorker.registration.showNotification(payload.title??"Parkeren",{
    body:payload.body??"Je hebt een nieuwe parkeermelding.",
    data:{url:payload.url??"/meldingen"}
  }));
});

serviceWorker.addEventListener("notificationclick",(event:NotificationEvent)=>{
  event.notification.close();
  const url=new URL(
    (event.notification.data as {url?:string}|undefined)?.url??"/meldingen",
    serviceWorker.location.origin
  ).href;

  event.waitUntil((async()=>{
    const clients=await serviceWorker.clients.matchAll({type:"window",includeUncontrolled:true});
    for(const client of clients){
      await client.focus();
      client.postMessage({type:"navigate",url});
      return;
    }
    await serviceWorker.clients.openWindow(url);
  })());
});
