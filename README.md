# Specters of Skyrim Randomizer

Specters of Skyrim Randomizer is a Synthesis patcher for
`SpectersOfSkyrim.esp`. Its goal is to select a small, reproducible subset of
the mod's hand-placed specter encounters before starting a playthrough.

## Milestone 1: dry-run planning

The current implementation is deliberately read-only. It discovers and
validates the source encounters, calculates which encounters would be selected,
and prints their source FormKeys. It creates no ACHR overrides and changes no
Skyrim records.

`DryRun` must remain enabled. Setting it to `false` stops with a clear error
because mutation has not been implemented yet.

## Requirements and settings

- Skyrim Special Edition and Synthesis
- Specters of Skyrim, with `SpectersOfSkyrim.esp` active
- `Probability` defaults to `5.0` and accepts finite values from 0 through 100
- `Seed` defaults to `38174`; every 32-bit signed integer is valid
- `DryRun` defaults to `true`

## Manual standalone CLI validation

Build the executable, then invoke its existing Synthesis patcher command with
the paths for the load order you want to inspect:

```powershell
dotnet build -c Release
& ".\SpectersOfSkyrimRandomizer\bin\Release\net10.0\SpectersOfSkyrimRandomizer.exe" `
  run-patcher `
  --GameRelease SkyrimSE `
  --DataFolderPath "<path-to-the-complete-Data-folder>" `
  --LoadOrderFilePath "<path-to-plugins.txt>" `
  --ModKey "SpectersOfSkyrimRandomizer.esp" `
  --OutputPath "<path-to-SpectersOfSkyrimRandomizer.esp>" `
  --ExtraDataFolder "<path-to-patcher-settings-folder>"
```

The data folder must expose the complete load order named by `plugins.txt`,
including an active `SpectersOfSkyrim.esp`. The extra-data folder should contain
`settings.json`; for example:

```json
{
  "Probability": 5.0,
  "Seed": 38174,
  "DryRun": true
}
```

`OutputPath` is required by the Synthesis `run-patcher` protocol even in dry-run
mode, and its filename should match `ModKey`. Synthesis may write an empty output
plugin, but this patcher adds no Skyrim records or overrides to it. The console
prints the complete dry-run summary and the selected source FormKeys in ascending
local FormID order. Setting `DryRun` to `false` exits with an error stating that
mutation is not implemented.

Probability is an independent chance for each encounter, not a requested exact
count. Five percent of the currently audited 93 encounters is approximately
five encounters, but a particular seed may select more or fewer. Zero percent
selects none and 100 percent selects all eligible encounters.

The same source record, seed, probability, and algorithm version always produce
the same decision. Changing the seed rerolls the planned locations. Candidate
enumeration order and unrelated records do not affect existing decisions.

## Deterministic algorithm contract

Algorithm `v1` uses SHA-256. Its input is this exact byte sequence:

1. Unsigned 32-bit little-endian byte length of the UTF-8 domain
   `SpectersOfSkyrimRandomizer/v1`
2. Domain UTF-8 bytes
3. Signed 32-bit little-endian seed in two's-complement representation
4. Unsigned 32-bit little-endian byte length of the normalized plugin filename
5. Lowercase-invariant UTF-8 origin plugin filename bytes
6. Unsigned 32-bit little-endian local FormID

The score is the first eight SHA-256 bytes interpreted as an unsigned
little-endian 64-bit integer. For probabilities strictly between 0 and 100, the
upper 53 score bits are converted to a value in `[0, 1)` and compared with
`Probability / 100`. The 0 and 100 endpoints are handled explicitly.

Changing this serialization or scoring rule requires a new algorithm version.
Golden-vector tests protect the `v1` output.

## Discovery and validation

The source plugin itself defines the authoritative candidate identities. The
patcher resolves the exact `LvlSpecterOfSkyrim` and
`TreasSpecterOfSkyrimSkeletonRigid` NPC Editor IDs inside that plugin, then
discovers only source-defined ACHRs using those bases. A different mod placing
the same base NPC does not become a candidate.

The currently supported schema is 93 specters and 93 skeletons. Every encounter
must form one unambiguous pair using the containing source cell FormKey plus the
exact source XYZ position. FormID proximity, enumeration order, and nearest
distance are never used as fallbacks. Unexpected source schemas stop the run.

The dry run also validates that every source specter still contains
`AutomaticLightSwitchScript` with `LightsOnTime = 22` and
`LightsOffTime = 5`. It never edits or reinterprets that VMAD data; nighttime
behavior remains wholly owned by Specters of Skyrim.

## Intended use and limitations

This project is intended as a pre-playthrough/new-game randomization tool, not a
runtime reroll system. It contains no Papyrus or SKSE component.

Current limitations:

- Analysis only; no encounters or skeletons are disabled yet
- Strictly supports the audited 93 + 93 source layout
- Winning records changed, deleted, or missing in later plugins are reported,
  but never modified or resurrected
- No compatibility promise is made for a materially changed future source
  plugin until its schema has been audited
