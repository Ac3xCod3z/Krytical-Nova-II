using System.Runtime.CompilerServices;

// The music clock moved here from Audio (15 Nov 2026: the server judges Cadence beats with it, and Audio, which only plays,
// stays off the server); Audio's combat player still feeds CombatBeat.
[assembly: InternalsVisibleTo("KryticalNova.Audio")]
[assembly: InternalsVisibleTo("KryticalNova.Audio.Tests.Editor")]
