export type PendingOperationType = "start" | "stop" | "end-time";

type PendingOperation = {
  type: PendingOperationType;
  operationId: string;
  logicalKey: string;
};

const storageKey = "parkeren.pending-operations.v1";

function read(): PendingOperation[] {
  try {
    const value = localStorage.getItem(storageKey);
    if (!value) return [];
    const parsed = JSON.parse(value);
    return Array.isArray(parsed) ? parsed.filter(isPendingOperation) : [];
  } catch {
    return [];
  }
}

function isPendingOperation(value: unknown): value is PendingOperation {
  if (!value || typeof value !== "object") return false;
  const item = value as Partial<PendingOperation>;
  return (item.type === "start" || item.type === "stop" || item.type === "end-time")
    && typeof item.operationId === "string"
    && typeof item.logicalKey === "string";
}

function write(items: PendingOperation[]) {
  if (items.length === 0) localStorage.removeItem(storageKey);
  else localStorage.setItem(storageKey, JSON.stringify(items));
}

export function getOrCreatePendingOperation(type: PendingOperationType, logicalKey: string): string {
  const items = read();
  const existing = items.find(item => item.type === type && item.logicalKey === logicalKey);
  if (existing) return existing.operationId;

  const operationId = crypto.randomUUID();
  write([...items, { type, operationId, logicalKey }]);
  return operationId;
}

export function clearPendingOperation(type: PendingOperationType, logicalKey: string) {
  write(read().filter(item => item.type !== type || item.logicalKey !== logicalKey));
}

export type ResolvedVisit = {
  id: string;
  startOperationId: string;
  desiredEndAt: string | null;
};

function sameEndAt(requested: string, actual: string | null): boolean {
  if (actual === null) return requested === "null";
  const requestedTime = Date.parse(requested);
  const actualTime = Date.parse(actual);
  return Number.isFinite(requestedTime) && requestedTime === actualTime;
}

export function clearResolvedVisitOperations(activeVisit: ResolvedVisit | null) {
  if (activeVisit === null) {
    write([]);
    return;
  }

  write(read().filter(item => {
    if (item.type === "start") return item.operationId !== activeVisit.startOperationId;
    if (item.type === "stop") return item.logicalKey === activeVisit.id;
    if (!item.logicalKey.startsWith(`${activeVisit.id}:`)) return false;
    return !sameEndAt(item.logicalKey.slice(activeVisit.id.length + 1), activeVisit.desiredEndAt);
  }));
}
