import { Entry, State, summarize } from "./model";
const host = "BUILD-STATION",
  task = "9bbe1cb0-4689-4d7f-a28b-01902c962331",
  session = "ec87764c-b894-4c22-a959-0605ba77af0a";
const ops = [
  "Inspect build script",
  "Download module manifest",
  "Verify module directory",
  "Check current location",
];
export const fixtures: Entry[] = ops.map((title, i) => {
  const id = "c4d7c32e-a67f-492f-9371-d3aaf6fd000" + i,
    requested =
      "2026-11-18T10:" + ["24:31", "25:32", "26:05", "26:49"][i] + "Z";
  return {
    op: {
      id,
      taskId: task,
      target: i === 1 ? "build-host:22" : host,
      connection: i === 1 ? "SSH" : "Local PowerShell",
      identity: "operator",
      privilege: "ELEVATED — ADMINISTRATOR",
      command: [
        "Get-Content -LiteralPath .\\Build.ps1",
        '{"action":"download","local_path":"C:\\\\Bridge\\\\modules.txt","remote_path":"/release/modules.txt"}',
        "Test-Path .\\app\\Modules",
        "Get-Location",
      ][i],
      directory: "C:\\Bridge",
      seconds: 30,
      description: title,
      effects: "Inspect the published module list for the current build.",
      risks: "Additional effects are unknown.",
      readOnly: false,
      resources:
        i === 1
          ? "Local: C:\\Bridge\\modules.txt\r\nRemote: /release/modules.txt"
          : "Additional resources unknown",
      canCaptureDiff: false,
    },
    taskId: task,
    sessionId: session,
    requested,
    status: i === 3 ? "rejected" : "completed",
    approval: i === 3 ? "rejected" : "approved",
    reason: i === 3 ? "Use the existing directory." : "",
    durationMs: [312, 842, 148, 0][i],
    evidence:
      i === 1
        ? "Created the new local file during the completed SFTP download."
        : "Changes not independently verified.",
    result: {
      status: "completed",
      output: i === 1 ? "Transfer completed (no overwrite)." : "",
      error: "",
      truncated: false,
      directory: "C:\\Bridge",
      fileDiff:
        i === 1
          ? "@@ -0,0 +1,3 @@\n+Microsoft.PowerShell.Utility\n+Microsoft.PowerShell.Management\n+Microsoft.PowerShell.Security"
          : undefined,
    },
  };
});
export function demo(): State {
  return {
    fixture: true,
    connection: "Fixture mode",
    identity: "operator",
    target: host,
    privilege: "Administrator",
    session: "Task " + task,
    rows: fixtures.map(summarize),
    pending: {
      ticket: "fixture",
      deadline: Date.now() + 60000,
      sessionId: session,
      op: {
        ...fixtures[0].op,
        description: "Modify application configuration",
        command:
          "Set-Content -LiteralPath '.\\config.json' -Value $updatedContent",
        effects: "Update config.json to correct a connection setting.",
        resources: "Local host verified · additional resources unknown",
        risks:
          "The script’s filesystem effects have not been independently verified.",
      },
    },
  };
}
