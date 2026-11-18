import React, { useState, useEffect, useRef, useMemo } from "react";
import { createRoot } from "react-dom/client";
import { invoke, isTauri } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import {
  Terminal,
  ShieldCheck,
  Monitor,
  Square,
  Minus,
  X,
  Check,
  Copy,
  Code2,
  Zap,
  Files,
  Info,
  Clock3,
  Search,
  History,
  ScrollText,
  ListTree,
  FileDiff,
  RotateCcw,
  Unplug,
  TriangleAlert,
  SlidersHorizontal,
  ChevronDown,
  ChevronRight,
  Expand,
  KeyRound,
  Activity,
  Folder,
  ArrowDownToLine,
  Octagon,
  Plus,
} from "lucide-react";
import { State, Entry, Pending, Summary, time, label, duration } from "./model";
import { demo, fixtures } from "./fixture";
import "./style.css";

const native = isTauri(),
  approval = new URLSearchParams(location.search).get("window") === "approval";
const blank: State = {
  fixture: false,
  connection: "Connecting",
  session: "",
  target: "",
  identity: "",
  privilege: "",
  rows: [],
};
function send(type: string, data: Record<string, unknown> = {}) {
  return native
    ? invoke("dispatch", { message: { type, ...data } })
    : Promise.resolve();
}
function Chrome({ close }: { close: () => void }) {
  return (
    <header
      className="chrome"
      onMouseDown={(e) => {
        if (
          native &&
          e.button === 0 &&
          !(e.target as HTMLElement).closest("button")
        )
          void getCurrentWindow().startDragging();
      }}
    >
      <span className="logo">
        <Terminal size={17} />
      </span>
      PowerShell Bridge
      <span className="grow" />
      {!approval && (
        <>
          <button
            aria-label="Minimize"
            onClick={() => native && getCurrentWindow().minimize()}
          >
            <Minus />
          </button>
          <button
            aria-label="Maximize or restore"
            onClick={() => native && getCurrentWindow().toggleMaximize()}
          >
            <Square />
          </button>
        </>
      )}
      <button aria-label="Close" onClick={close}>
        <X />
      </button>
    </header>
  );
}
function Clip({ text }: { text: string }) {
  const [copied, set] = useState(false);
  return (
    <button
      className="icon"
      aria-label="Copy exact text"
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(text);
          set(true);
          setTimeout(() => set(false), 1000);
        } catch {
          set(false);
        }
      }}
    >
      {copied ? <Check /> : <Copy />}
    </button>
  );
}
function Code({ value }: { value: string }) {
  return (
    <pre>
      {value
        .split(/('[^'\n]*'|"[^"\n]*"|\$[\w:]+|\b[A-Za-z]+-[A-Za-z]+\b)/g)
        .map((p, i) => (
          <span
            key={i}
            className={
              /^['"]/.test(p)
                ? "string"
                : p.startsWith("$")
                  ? "variable"
                  : /^[a-z]+-[a-z]+$/i.test(p)
                    ? "cmd"
                    : ""
            }
          >
            {p}
          </span>
        ))}
    </pre>
  );
}
function Countdown({ deadline }: { deadline: number }) {
  const [ms, set] = useState(Math.max(0, deadline - Date.now()));
  useEffect(() => {
    const tick = () => set(Math.max(0, deadline - Date.now()));
    tick();
    const timer = setInterval(tick, 200);
    return () => clearInterval(timer);
  }, [deadline]);
  const sec = Math.ceil(ms / 1000);
  return (
    <div className={"count " + (sec <= 10 ? "urgent" : "")}>
      <small>Approval expires in</small>
      <div>
        <svg viewBox="0 0 80 80">
          <circle cx="40" cy="40" r="35" />
          <circle
            cx="40"
            cy="40"
            r="35"
            pathLength="100"
            strokeDasharray={Math.min(100, ms / 600) + " 100"}
          />
        </svg>
        <b>{sec === 60 ? "01:00" : "00:" + String(sec).padStart(2, "0")}</b>
      </div>
    </div>
  );
}
function Approval({
  p,
  fixture,
  done,
}: {
  p: Pending;
  fixture: boolean;
  done: () => void;
}) {
  const o = p.op,
    [reason, reasonSet] = useState(""),
    [more, setMore] = useState(false),
    [big, setBig] = useState(false),
    [diff, setDiff] = useState(false),
    [error, setError] = useState("");
  const busy = useRef(false);
  async function decide(choice: string) {
    if (busy.current) return;
    busy.current = true;
    try {
      await send("decide", {
        id: o.id,
        ticket: p.ticket,
        choice,
        reason,
        captureDiff: diff,
      });
      if (fixture) done();
    } catch {
      setError("Decision could not be delivered.");
      busy.current = false;
    }
  }
  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if (e.repeat) return;
      if (e.key === "Escape" || (e.ctrlKey && e.key.toLowerCase() === "r")) {
        e.preventDefault();
        void decide("reject");
      } else if (e.ctrlKey && e.key === "Enter") {
        e.preventDefault();
        void decide("approve");
      }
    };
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  });
  return (
    <>
      <Chrome close={() => void decide("reject")} />
      <main className={"approval " + (o.readOnly ? "read" : "write")}>
        <div className="approvalhead">
          <div className="shield">
            <ShieldCheck />
          </div>
          <h1>Approval required</h1>
          <div className="request">
            {fixture ? "Design fixture" : "Request"}{" "}
            <strong>{o.id.slice(-6)}</strong>
            <small>Task {o.taskId.slice(0, 8)}</small>
          </div>
        </div>
        <section className="hero">
          <div>
            <div className="badges">
              <b className="severity">{o.readOnly ? "READ" : "WRITE"}</b>
              <span className="priv">
                <ShieldCheck />
                {o.privilege}
              </span>
            </div>
            <h2>{o.description}</h2>
            <p>
              <span className="sub">Model prediction</span> {o.effects}
            </p>
            <div className="target">
              <Monitor /> Target{" "}
              <b>
                {o.connection} · {o.target}
              </b>
            </div>
          </div>
          <Countdown deadline={p.deadline} />
        </section>
        <section className={"panel exact " + (big ? "expanded" : "")}>
          <div className="sectiontitle">
            <Code2 />
            Exact command
            <span className="grow" />
            <Clip text={o.command} />
            <button
              className="icon"
              aria-label="Expand or restore command"
              onClick={() => setBig(!big)}
            >
              <Expand />
            </button>
          </div>
          <Code value={o.command} />
        </section>
        <div className="impact">
          <div>
            <Zap className="amber" />
            <b>Expected effects</b>
            <span>
              {o.effects} <em>Model-supplied</em>
            </span>
          </div>
          <div>
            <Files />
            <b>Affected resources</b>
            <span>{o.resources}</span>
          </div>
          <div>
            <TriangleAlert className="amber" />
            <b>Unverified effects</b>
            <span>{o.risks}</span>
          </div>
        </div>
        <button className="disclose" onClick={() => setMore(!more)}>
          {more ? <ChevronDown /> : <ChevronRight />}Technical details
        </button>
        {more && (
          <pre className="meta">
            {[
              o.id,
              o.taskId,
              p.sessionId,
              o.directory,
              o.identity,
              "Execution timeout " + o.seconds + " s",
            ].join("\n")}
          </pre>
        )}
        <section className="panel reason">
          <label htmlFor="reason">
            Reason for rejection <em>optional</em>
          </label>
          <textarea
            id="reason"
            rows={2}
            maxLength={2000}
            value={reason}
            placeholder="Explain what ChatGPT should reconsider…"
            onChange={(e) => {
              reasonSet(e.target.value);
              void send("draft", {
                id: o.id,
                ticket: p.ticket,
                reason: e.target.value,
              }).catch(() => setError("Feedback could not be delivered."));
            }}
          />
        </section>
        {o.canCaptureDiff && (
          <label>
            <input
              type="checkbox"
              checked={diff}
              onChange={(e) => setDiff(e.target.checked)}
            />{" "}
            Capture local text diff (8 KiB maximum)
          </label>
        )}
        {error && <p role="alert">{error}</p>}
        <div className="decisions">
          <button className="danger" onClick={() => void decide("stop")}>
            <Square />
            Stop task
          </button>
          <span className="grow" />
          <button onClick={() => void decide("reject")}>
            <X />
            Reject
          </button>
          <button className="approve" onClick={() => void decide("approve")}>
            <Check />
            Approve once
          </button>
        </div>
      </main>
      <footer>
        <Info />
        Approval required for every action.{fixture && " · Fixture mode"}
      </footer>
    </>
  );
}
function Diff({ text, path }: { text: string; path: string }) {
  let a = 0,
    b = 0;
  return (
    <section className="panel diff">
      <div className="sectiontitle">
        <FileDiff />
        File diff <small>{path}</small>
      </div>
      <div className="diffbody">
        {text.split(/\r?\n/).map((l, i) => {
          const m = /^@@ -(\d+)(?:,\d+)? \+(\d+)/.exec(l);
          if (m) {
            a = +m[1];
            b = +m[2];
          }
          const sign = l[0],
            content = !m && [" ", "+", "-"].includes(sign);
          return (
            <div
              key={i}
              className={
                sign === "+" ? "add" : sign === "-" ? "del" : "neutral"
              }
            >
              <i>{content && sign !== "+" ? a++ : ""}</i>
              <i>{content && sign !== "-" ? b++ : ""}</i>
              <code>{l}</code>
            </div>
          );
        })}
      </div>
    </section>
  );
}
function Data({ title, value }: { title: string; value: string }) {
  return (
    <section className="panel data">
      <div className="sectiontitle">
        {title}
        <span className="grow" />
        <Clip text={value} />
      </div>
      <pre>{value || "—"}</pre>
    </section>
  );
}
function Details({ e, tab }: { e: Entry; tab: string }) {
  const o = e.op,
    r = e.result,
    changes = (
      <>
        <div className="impact">
          <div>
            <ShieldCheck className={r?.fileDiff ? "green" : ""} />
            <b>Change evidence</b>
            <span>{e.evidence}</span>
          </div>
          <div>
            <Zap className="amber" />
            <b>Expected effects</b>
            <span>
              {o.effects} <em>Model-supplied</em>
            </span>
          </div>
          <div>
            <TriangleAlert className="amber" />
            <b>Unverified effects</b>
            <span>{o.risks}</span>
          </div>
        </div>
      </>
    );
  return (
    <>
      {tab === "Summary" && (
        <>
          {changes}
          <Data title="Exact operation" value={o.command} />
          {e.reason && <Data title="Decision feedback" value={e.reason} />}
        </>
      )}
      {tab === "Changes" && (
        <>
          {changes}
          <Data title="Known resources" value={o.resources} />
        </>
      )}
      {(tab === "Summary" || tab === "Changes") && r?.fileDiff && (
        <Diff text={r.fileDiff} path={o.resources.split("\n")[0]} />
      )}
      {tab === "Output" && (
        <>
          <div className="metastrip">
            {label(e.status)} · {duration(e.durationMs)} · Exit{" "}
            {r?.exitCode ?? "unavailable"}
            {r?.truncated && " · Output truncated"}
          </div>
          <Data title="Exact command" value={o.command} />
          <Data
            title="Standard output"
            value={r?.output ?? "Awaiting result"}
          />
          <Data title="Standard error and diagnostics" value={r?.error ?? ""} />
        </>
      )}
      {tab === "Details" && (
        <>
          <dl>
            {Object.entries({
              "Request ID": o.id,
              "Task ID": e.taskId,
              "Session ID": e.sessionId,
              Target: o.target,
              Identity: o.identity,
              Privilege: o.privilege,
              Classification: o.readOnly ? "READ" : "WRITE",
              "Working directory": o.directory,
              "Execution limit": o.seconds + " s",
              Approval: e.approval,
              Requested: e.requested,
              Decision: e.decided,
              Started: e.started,
              Ended: e.ended,
            }).map(([k, v]) => (
              <React.Fragment key={k}>
                <dt>{k}</dt>
                <dd>{v || "—"}</dd>
              </React.Fragment>
            ))}
          </dl>
          <Data title="Exact operation" value={o.command} />
        </>
      )}
    </>
  );
}

function Credentials({ msg }: { msg: any }) {
  const [username, name] = useState(""),
    [secret, password] = useState(""),
    [path, key] = useState(""),
    [method, mode] = useState("password");
  const reply = (accepted: boolean) =>
    send("credentialReply", {
      id: msg.id,
      ticket: msg.ticket,
      accepted,
      username,
      secret: accepted ? secret : "",
      path,
      method,
    });
  return (
    <>
      <Chrome close={() => void reply(false)} />
      <main className="approval">
        <h1>Local SSH credentials</h1>
        <p>
          {msg.payload.target} ·{" "}
          {msg.payload.sudo ? "sudo authentication" : "SSH authentication"}
        </p>
        {!msg.payload.sudo && (
          <>
            <label>
              Username
              <input value={username} onChange={(e) => name(e.target.value)} />
            </label>
            <label>
              Authentication
              <select value={method} onChange={(e) => mode(e.target.value)}>
                <option>password</option>
                <option>private-key</option>
              </select>
            </label>
            {method === "private-key" && (
              <label>
                Private key path
                <input value={path} onChange={(e) => key(e.target.value)} />
              </label>
            )}
          </>
        )}
        <label>
          Password or key passphrase
          <input
            type="password"
            autoComplete="off"
            value={secret}
            onChange={(e) => password(e.target.value)}
          />
        </label>
        <p>Kept in memory for this connection.</p>
        <div className="decisions">
          <button onClick={() => void reply(false)}>Cancel</button>
          <button onClick={() => void reply(true)}>Use credential</button>
        </div>
      </main>
    </>
  );
}
function Manager({
  state,
  entry,
  select,
}: {
  state: State;
  entry?: Entry;
  select: (id: string) => void;
}) {
  const [tab, setTab] = useState("Summary"),
    [query, filter] = useState(""),
    [offset, scroll] = useState(0);
  const rows = useMemo(
    () =>
      state.rows
        .filter((r) =>
          (r.title + " " + r.target + " " + r.id + " " + r.status)
            .toLowerCase()
            .includes(query.toLowerCase()),
        )
        .sort((a, b) => b.requested.localeCompare(a.requested)),
    [state.rows, query],
  );
  const from = Math.max(
    0,
    Math.min(rows.length - 1, Math.floor(offset / 80) - 3),
  );
  return (
    <>
      <Chrome close={() => void send("disconnect")} />
      <div className="statusbar">
        <b className="pill">{state.connection}</b>
        <div>
          <small>Execution identity</small>
          {state.identity} · {state.privilege}
        </div>
        <div>
          <small>Target machine</small>
          {state.target}
        </div>
        <button className="danger" onClick={() => void send("stopTask")}>
          <Square /> Stop task
        </button>
      </div>
      <main className="manager">
        <aside>
          <div className="task">
            <h3>
              <Zap />
              Current task
            </h3>
            <p>{state.session || "Waiting for session"}</p>
          </div>
          <h3>
            <History />
            Operation history
          </h3>
          <div className="search">
            <Search />
            <input
              value={query}
              aria-label="Search operations"
              placeholder="Search operations…"
              onChange={(e) => filter(e.target.value)}
            />
          </div>
          <div
            className="history"
            onScroll={(e) => scroll(e.currentTarget.scrollTop)}
          >
            <div style={{ height: rows.length * 80, position: "relative" }}>
              {rows.slice(from, from + 25).map((r, i) => (
                <button
                  key={r.id}
                  className={
                    "historyrow " + (entry?.op.id === r.id ? "selected" : "")
                  }
                  style={{ top: (from + i) * 80 }}
                  onClick={() => select(r.id)}
                >
                  <span>
                    <strong>{r.title}</strong>
                    <small>{time(r.requested)}</small>
                  </span>
                  <span>
                    {r.target} · {r.readOnly ? "READ" : "WRITE"}
                  </span>
                  <span>
                    {label(r.status)} · {duration(r.durationMs)}
                  </span>
                </button>
              ))}
            </div>
          </div>
        </aside>
        <article>
          {entry ? (
            <>
              <div className="detailhead">
                <span className="bigicon">
                  <Code2 />
                </span>
                <div>
                  <h2>{entry.op.description}</h2>
                  <small>
                    {entry.op.connection} · {entry.op.target}
                  </small>
                </div>
                <b className="pill">{label(entry.status)}</b>
              </div>
              <nav>
                {["Summary", "Changes", "Output", "Details"].map((t, i) => {
                  const Icon = [ScrollText, FileDiff, Terminal, ListTree][i];
                  return (
                    <button
                      key={t}
                      className={tab === t ? "active" : ""}
                      onClick={() => setTab(t)}
                    >
                      <Icon />
                      {t}
                    </button>
                  );
                })}
              </nav>
              <div className="details">
                <Details tab={tab} e={entry} />
              </div>
            </>
          ) : (
            <p>Select an operation to inspect its approval and execution.</p>
          )}
        </article>
      </main>
      <footer>
        <Info /> {state.fixture ? "Fixture data" : "Local execution supervisor"}
        <span className="grow" />
        <button onClick={() => void send("newTask")}>
          <Plus />
          New task
        </button>
        <button onClick={() => void send("reset")}>
          <RotateCcw />
          Reset session
        </button>
        <button className="danger" onClick={() => void send("disconnect")}>
          <Unplug />
          Disconnect
        </button>
      </footer>
    </>
  );
}
function App() {
  const example =
    !native && new URLSearchParams(location.search).has("fixture");
  const [state, set] = useState<State>(() => (example ? demo() : blank)),
    [entry, show] = useState<Entry | undefined>(
      example ? fixtures[1] : undefined,
    ),
    [credential, auth] = useState<any>(),
    [notice, note] = useState("");
  const current = useRef(entry?.op.id);
  function select(id: string) {
    current.current = id;
    if (example || state.fixture) show(fixtures.find((e) => e.op.id === id));
    else void send("detail", { id });
  }
  useEffect(() => {
    if (!native) return;
    const off = listen<any>("bridge", ({ payload: m }) => {
      switch (m.type) {
        case "fixture":
          set(demo());
          current.current = fixtures[1].op.id;
          show(fixtures[1]);
          break;
        case "init":
          set({ ...blank, ...m, connection: "Local bridge ready" });
          if (m.rows.length) select(m.rows[m.rows.length - 1].id);
          break;
        case "session":
          set((s) => ({ ...s, session: m.text }));
          break;
        case "row":
          set((s) => ({
            ...s,
            rows: [m.row, ...s.rows.filter((r) => r.id !== m.row.id)],
          }));
          if (!current.current || current.current === m.row.id)
            select(m.row.id);
          break;
        case "entry":
          if (current.current === m.entry.op.id) show(m.entry);
          break;
        case "approval":
          auth(undefined);
          set((s) => ({ ...s, pending: { ...m.payload, ticket: m.ticket } }));
          break;
        case "credential":
          auth(m);
          break;
        case "dismiss":
          auth(undefined);
          set((s) => ({ ...s, pending: undefined }));
          break;
        case "notice":
          note(m.text);
          break;
      }
    });
    void off.then(() => send("ready"));
    return () => {
      void off.then((f) => f());
    };
  }, []);
  if (approval)
    return credential ? (
      <Credentials key={credential.ticket} msg={credential} />
    ) : state.pending ? (
      <Approval
        key={state.pending.ticket}
        p={state.pending}
        fixture={state.fixture}
        done={() => set((s) => ({ ...s, pending: undefined }))}
      />
    ) : (
      <p>No pending approval.</p>
    );
  return (
    <>
      {notice && (
        <div role="status" onClick={() => note("")}>
          {notice}
        </div>
      )}
      <Manager state={state} entry={entry} select={select} />
    </>
  );
}
createRoot(document.getElementById("root")!).render(<App />);
