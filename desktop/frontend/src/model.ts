export type Operation = {
  id: string;
  target: string;
  connection: string;
  identity: string;
  privilege: string;
  command: string;
  directory: string;
  seconds: number;
  description: string;
  effects: string;
  risks: string;
  readOnly: boolean;
  resources: string;
  taskId: string;
  canCaptureDiff: boolean;
};
export type Summary = {
  id: string;
  title: string;
  target: string;
  status: string;
  approval: string;
  requested: string;
  readOnly: boolean;
  durationMs: number;
  taskId: string;
  sessionId: string;
  started?: string;
  seconds: number;
};
export type Entry = {
  op: Operation;
  taskId: string;
  sessionId: string;
  requested: string;
  status: string;
  approval: string;
  reason: string;
  decided?: string;
  started?: string;
  ended?: string;
  durationMs: number;
  evidence: string;
  result?: {
    status: string;
    output: string;
    error: string;
    exitCode?: number;
    truncated: boolean;
    fileDiff?: string;
    directory: string;
  };
};
export type Pending = {
  op: Operation;
  ticket: string;
  deadline: number;
  sessionId: string;
};
export type State = {
  fixture: boolean;
  connection: string;
  session: string;
  identity: string;
  target: string;
  privilege: string;
  rows: Summary[];
  pending?: Pending;
};
export const summarize = (e: Entry): Summary => ({
  id: e.op.id,
  title: e.op.description,
  target: e.op.target,
  status: e.status,
  approval: e.approval,
  requested: e.requested,
  readOnly: e.op.readOnly,
  durationMs: e.durationMs,
  sessionId: e.sessionId,
  taskId: e.taskId,
  started: e.started,
  seconds: e.op.seconds,
});
export const time = (s?: string) =>
  s
    ? new Date(s).toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
        hour12: false,
      })
    : "—";
export const duration = (ms: number) =>
  ms < 1000 ? Math.round(ms) + " ms" : (ms / 1000).toFixed(1) + " s";
export const label = (s: string) =>
  s.replaceAll("_", " ").replace(/^./, (v) => v.toUpperCase());
