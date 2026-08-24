# Phase 11B private Current Session probe

This is a disposable, Windows Sandbox-only compatibility experiment. It is not part of `MediaLock.sln`, is not a
production dependency and must not be distributed as a supported Media Lock feature.

The probe publishes one ordinary desktop SMTC Session through the documented
`SystemMediaTransportControlsInterop.GetForWindow` boundary. Its private test button then uses NPSMLib 0.9.14 to find
the Session whose PID matches the probe process and calls the reverse-engineered `SetCurrentSession` once.

Guardrails:

- Run only inside Windows Sandbox.
- Do not automate or repeatedly call the private setter.
- A successful HRESULT is not sufficient evidence; compare the public GSMTC Current Session and Windows native UI.
- Close the probe after the observation. Closing disables its SMTC and clears its display metadata.
- Do not add this project or NPSMLib to the production solution.

NPSMLib is MIT licensed and comes with no Windows 11 build 26100 compatibility guarantee:
<https://github.com/ADeltaX/NPSMLib/tree/22616b82f9b6ffd43ecf863f89455766edf63c76>
