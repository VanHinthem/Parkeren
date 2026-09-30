/// <reference lib="webworker" />

declare const self: ServiceWorkerGlobalScope;

type PushPayload = {
  title?: string;
  body?: string;
  url?: string;
};

self.addEventListener("push",event=>{
  let payload:PushPayload={};
  try{payload=event.data?.json() as PushPayload??{};}catch{payload={};}

  event.waitUntil(self.registration.showNotification(payload.title??"Parkeren",{
    body:payload.body??"Je hebt een nieuwe parkeermelding.",
    data:{url:payload.url??"/meldingen"}
  }));
});

self.addEventListener("notificationclick",event=>{
  event.notification.close();
  const url=new URL((event.notification.data as {url?:string}|undefined)?.url??"/meldingen",self.location.origin).href;

  event.waitUntil((async()=>{
    const clients=await self.clients.matchAll({type:"window",includeUncontrolled:true});
    for(const client of clients){
      if("focus" in client){
        await client.focus();
        client.postMessage({type:"navigate",url});
        return;
      }
    }
    await self.clients.openWindow(url);
  })());
});
