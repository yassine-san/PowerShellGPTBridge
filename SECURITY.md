## Reporting
Use GitHub private vulnerability reporting if enabled, or contact the maintainer privately. Include the version and a sanitized description. Keep keys, transcripts and host inventories out of issue reports.

## Trust boundary
An approved command inherits the user's access. The persistent worker is not a sandbox. Read the exact command and target before each decision. Verify the SSH host fingerprint with the server administrator through a separate channel before first use. Stop on an unexpected change. Disconnect ends new requests. Review interrupted remote operations on the remote machine before retrying.

Run as an ordinary desktop user. Protect the user account, bridge binaries, state directory and tunnel key. Other processes controlling the same desktop or account are inside the local trust boundary. Avoid putting secrets in command text or output. If a key leaks, revoke it at its issuer, replace it locally, and coordinate removal from public history, forks, caches and artifacts.
