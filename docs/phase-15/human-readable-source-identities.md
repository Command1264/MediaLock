# Phase 15 human-readable source identities

Date: 2026-08-26

## Scope

Replace raw source identifiers on Main Session／target and Settings Priority Rule surfaces with trustworthy Windows
application names while preserving the exact `SourceAppUserModelId` for routing, Recovery and persistence. This phase
does not inspect browser URLs, infer identity from media metadata or migrate saved identities.

## Metadata decision

Microsoft documents `shell:AppsFolder` as the installed-application view that exposes both application names and
AppUserModelIDs. `FOLDERID_AppsFolder` is a Windows virtual Applications folder, and `System.AppUserModel.ID` is the
property Windows uses to associate processes, windows and shortcuts with an application. These are a better match for
classic browsers and installed PWAs than `Windows.ApplicationModel.AppInfo`, which is retained neither as a dependency
nor as a fallback in this phase.[Find installed AUMIDs][find-aumid] [AppsFolder][apps-folder]
[System.AppUserModel.ID][app-id]

The Windows Adapter enumerates AppsFolder once, performs exact ordinal AUMID lookup and returns:

- the Shell item display name;
- a distinct target executable `ProductName` as an optional host qualifier;
- no result when metadata is unavailable, allowing the App presentation Module to use the raw ID.

The host qualifier is omitted when either name already contains the other. A trailing generic `Browser` product suffix
is removed, yielding `YouTube Music — Brave` rather than `YouTube Music — Brave Browser`, without hard-coding `_crx_`
IDs or rewriting other product names.

## Module shape

```text
ISourceApplicationMetadataResolver
└─ WindowsSourceApplicationMetadataResolver
   └─ AppsFolder exact AUMID metadata

SourceApplicationPresentationCatalog
├─ friendly／host composition
├─ collision disambiguation
└─ raw-ID fallback and details
```

The Module has high Leverage: Main and Settings learn one presentation result, while Shell enumeration, qualifier
selection, collisions and fallback remain local. Core snapshots and settings documents are unchanged.

## Automated evidence

- Presentation catalog: trusted display／host names, fallback and deterministic duplicate-name disambiguation.
- Windows Adapter: exact case-sensitive lookup, one cached catalog load and qualifier normalization.
- Main: friendly current target／Session label while App Lock submits the raw identity.
- Settings: friendly available-app／Priority Rule label while Save persists raw identities.
- WPF contract: raw identity remains in tooltip and accessibility help; ComboBox uses raw ID as selected value.

The reviewed implementation passes 383 repository tests, a Release build with zero warnings／errors, formatting,
Markdown relative-link validation, `git diff --check` and the isolated packaging regression suite. The named desktop
smoke below also passed.

## Host metadata observation

On the named development host, the exact Windows Adapter resolved:

| GSMTC source identity | Shell display | Host qualifier | Candidate label |
| --- | --- | --- | --- |
| `Brave._crx_cinhimbnkkghhklpknlkffjgod` | YouTube Music | Brave | YouTube Music — Brave |
| `Brave` | Brave | none | Brave |
| `Chrome` | Google Chrome | none | Google Chrome |
| `MSEdge` | Microsoft Edge | none | Microsoft Edge |

This proves the production Adapter can read the current host's registrations and supplies the metadata exercised by
the named UI／routing smoke below.

## Manual acceptance record

Status: passed on 2026-08-26 on the named development host.

- Main Session and current-target surfaces displayed `YouTube Music — Brave`; Settings Priority Rules displayed the
  same friendly name.
- Main, Settings tooltip／accessibility help and Priority Rules retained
  `Brave._crx_cinhimbnkkghhklpknlkffjgod` as the exact diagnostic and routing identity.
- Session Lock routed both the on-screen Play／Pause control and an ASUS ROG STRIX FLARE physical Play／Pause key to
  the installed Brave YouTube Music PWA. Ordinary Brave YouTube did not change.
- Reloading the PWA produced Recovering and then reacquired the recreated Session as `YouTube Music — Brave` without
  rebinding to ordinary Brave. The raw identity remained unchanged.
- Keep Playing remained selected across the Session Lock recovery and the reacquired YouTube Music target resumed
  playing.
- The shared presentation text is metadata rather than localized prose. Existing automated English／Traditional
  Chinese and Light／Dark ViewModel／WPF contracts therefore cover both language and theme surfaces without creating
  a second resolver path.

App Lock, Priority Rules and Windows Auto continue submitting the same raw identities through automated regression
coverage; this phase's named command-isolation and Recovery gate used Session Lock because it exercises the strictest
single-Session identity continuity.

[find-aumid]: https://learn.microsoft.com/en-us/windows/configuration/store/find-aumid
[apps-folder]: https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid
[app-id]: https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-id
