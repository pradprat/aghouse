# Security

## Reporting a vulnerability

Please report security problems privately through GitHub:
**Security → Report a vulnerability** on this repository
(<https://github.com/pradprat/aghouse/security/advisories/new>).
Don't open a public issue for them.

Include what you found, how to reproduce it and which version you used (`aghouse ping`
prints it). You should get a reply within a week. Fixes go into the next release, and the
advisory is published once that release is out.

## Supported versions

Only the latest release gets security fixes.

## What aghouse trusts

- **Your Windows account.** aghouse's named pipe (`aghouse-<user>`) only accepts
  connections from processes running as the same user. Any such process can drive aghouse
  through the CLI, including reading pane output. That's the same reach any program running
  as you already has.
- **Not terminal output.** Escape sequences that programs print (titles, notifications,
  working-folder reports, bracketed-paste mode) are treated as untrusted. Titles are
  cleaned before they reach an agent's context, notifications say which pane raised them,
  and sizes are capped.
- **Not the clipboard.** Pasted text loses its control characters, is bracketed when
  the shell asks for that, and a paste that would run commands asks first (Settings ›
  General).
- **Agents, only partly.** A pane can type into panes that don't run an agent (for
  example a shell it opened). Typing into a pane that runs an agent is refused, so one
  agent can't answer another agent's permission prompt. Set `"allowInputToAgentPanes": true`
  in `settings.json` to allow it. The CLI can't change that setting. This is defence in
  depth, not a boundary: a process running as you can still get around it.

## Verifying a download

Releases aren't code-signed yet. Each release lists SHA-256 checksums in
`SHA256SUMS.txt`. Check a download with:

```powershell
Get-FileHash .\aghouse-<version>-setup-x64.exe -Algorithm SHA256
```
