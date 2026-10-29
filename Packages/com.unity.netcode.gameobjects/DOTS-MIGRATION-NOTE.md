# Embedded copy of Netcode for GameObjects 2.13.3 (DOTS migration branch only)

The registry package cannot sit beside Netcode for Entities (com.unity.netcode 6.6.0): both define the Editor assemblies
`Unity.Netcode.Editor` (case-insensitive clash with `Unity.NetCode.Editor`) and `Unity.Netcode.Editor.Tests`, and Unity stops compiling.
This copy lets the unported NGO code keep compiling during the migration. Changes against the registry package:
- `Editor/Unity.Netcode.Editor.asmdef` renamed to `Unity.Netcode.GameObjects.Editor`; `Runtime/AssemblyInfo.cs` gets a matching InternalsVisibleTo.
- `Tests/`, `Samples~/`, `Documentation~/` removed (the test assemblies clash too).
Removed together with the NGO package at cutover (plan section 9).
