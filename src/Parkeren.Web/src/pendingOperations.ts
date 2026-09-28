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

export function clearResolvedVisitOperations(activeVisitId: string | null) {
  write(read().filter(item => {
    if (item.type === "start") return activeVisitId !== null;
    if (item.type === "stop" || item.type === "end-time")
      return activeVisitId !== null && item.logicalKey.startsWith(`${activeVisitId}:`) || item.logicalKey === activeVisitId;
    return false;
  }));
}
