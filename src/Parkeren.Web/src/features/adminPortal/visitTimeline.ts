import type { AdminVisitTimelineEvent } from "../../api/client";

export type AdminVisitTimelineGroup={
  key:string;
  groupKey:string;
  events:AdminVisitTimelineEvent[];
};

export function groupTimelineEvents(events:AdminVisitTimelineEvent[]):AdminVisitTimelineGroup[]{
  const chronological=[...events].sort((left,right)=>
    new Date(left.occurredAt).getTime()-new Date(right.occurredAt).getTime()||
    left.eventOrder-right.eventOrder||
    left.id.localeCompare(right.id)
  );
  const groups:AdminVisitTimelineGroup[]=[];

  for(const event of chronological){
    const current=groups.at(-1);
    if(current?.groupKey===event.groupKey){
      current.events.push(event);
    }else{
      groups.push({key:`${event.groupKey}:${event.id}`,groupKey:event.groupKey,events:[event]});
    }
  }

  return groups;
}

export function timelineSourceHref(event:AdminVisitTimelineEvent){
  if(event.sourceType==="provider_operation")return `#provider-operation-${event.sourceId}`;
  if(event.sourceType==="provider_action")return `#provider-action-${event.sourceId}`;
  if(event.sourceType==="scheduler_work")return `#scheduler-work-${event.sourceId}`;
  return null;
}