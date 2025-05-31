// Put this in *one* of the shared projects (the one that both DLLs
// already reference), e.g. MyScript.Shared or MyScript.Debugger.Common.
// It is a plain container for the two dummy objects created in the
// monitor process and reused by the IDE-side stack filter.

using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Script;

namespace HelloWorld
{
    /// <summary>
    ///  Per-process cache that holds the synthetic runtime & module so
    ///  every synthetic stack frame can reference the *same* instances
    ///  without trying to create new ones (which would violate location
    ///  constraints and crash).
    /// </summary>
    internal sealed class DummyModuleCache : DkmDataItem
    {
        internal readonly DkmScriptRuntimeInstance Runtime;
        internal readonly DkmCustomModuleInstance Module;

        internal DummyModuleCache(DkmScriptRuntimeInstance runtime,
                            DkmCustomModuleInstance module)
        {
            Runtime = runtime;
            Module = module;
        }
    }
}
