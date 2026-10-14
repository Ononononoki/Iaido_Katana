using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("Iaido_Katana")]
[assembly: AssemblyDescription("A two-handed katana for Valheim that strikes back on a perfect parry")]
[assembly: AssemblyVersion("0.2.1.0")]
[assembly: AssemblyFileVersion("0.2.1.0")]

// lets the offline check in test\KatanaCheck call the internal helpers
[assembly: InternalsVisibleTo("KatanaCheck")]
// and the stance preview in test\StancePreview runs the stance solver on the ripped skeleton
[assembly: InternalsVisibleTo("StancePreview")]
