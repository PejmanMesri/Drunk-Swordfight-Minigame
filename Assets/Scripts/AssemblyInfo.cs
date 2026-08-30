using System.Runtime.CompilerServices;

// PlayMode tests live in their own assembly and drive the clash/blocking
// paths through these internals.
[assembly: InternalsVisibleTo("DrunkSwordfightTests")]
