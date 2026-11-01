// AssemblyInfo.cs
// GoF2.Net: multiplayer (Netcode for GameObjects, Unity Transport, Relay / Lobby) and the event graphs. Only this assembly
// (and GoF2.App) reference Netcode, so its IL post-processors don't run on the rest.
// Layers, lowest first: GoF2.Core <- GoF2.Game <- GoF2.Space <- GoF2.Net <- GoF2.App (Scripts/Runtime/README.md).

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GoF2.App")]
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
[assembly: InternalsVisibleTo("GoF2.Tests")]
[assembly: InternalsVisibleTo("GoF2.PlayModeTests")]
