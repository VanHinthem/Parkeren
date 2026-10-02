import { apiFetch } from "./client";

export type AdminAuditEvent={
  id:string;
  actorUserId:string;
  actorUsername:string;
  action:string;
  targetType:string;
  targetId:string|null;
  createdAt:string;
  contextJson:string|null;
};

export type AdminAuditQuery={
  actorUserId?:string;
  action?:string;
  targetType?:string;
  targetId?:string;
  from?:string;
  to?:string;
  limit?:number;
};

export async function getAdminAuditEvents(query:AdminAuditQuery={}){
  const parameters=new URLSearchParams();
  if(query.actorUserId?.trim())parameters.set("actorUserId",query.actorUserId.trim());
  if(query.action?.trim())parameters.set("action",query.action.trim());
  if(query.targetType?.trim())parameters.set("targetType",query.targetType.trim());
  if(query.targetId?.trim())parameters.set("targetId",query.targetId.trim());
  if(query.from)parameters.set("from",query.from);
  if(query.to)parameters.set("to",query.to);
  if(query.limit)parameters.set("limit",String(query.limit));

  const suffix=parameters.size?`?${parameters.toString()}`:"";
  const response=await apiFetch(`/api/admin/system/audit${suffix}`);
  if(!response.ok)throw new Error("Auditlog kon niet worden geladen.");
  return response.json() as Promise<AdminAuditEvent[]>;
}
