using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Native;
using Microsoft.VisualStudio.Debugger.Script;
using Microsoft.VisualStudio.Debugger.Stepping;
using Microsoft.VisualStudio.Debugger.Symbols;
using System;
using System.Collections.Generic;

namespace HelloWorld
{
    internal class SquirrelRemoteProcessData : DkmDataItem
    {
        public DkmLanguage language = null;
        public DkmCompilerId compilerId;

        public DkmRuntimeInstance runtimeInstance = null;
        public DkmModule module = null;
        public DkmCustomModuleInstance moduleInstance = null;
    }

    class ScriptModuleRegistrar : IDkmCustomMessageForwardReceiver
    {
        public DkmCustomMessage SendLower(DkmCustomMessage customMessage)
        {
            var process = customMessage.Process;
            var processData = DebugHelpers.GetOrCreateDataItem<SquirrelRemoteProcessData>(process);

            if (customMessage.MessageCode == MessageToRemote.createRuntime)
            {
                if (processData.language == null)
                {
                    processData.compilerId = new DkmCompilerId(Guids.squirrelCompilerGuid, Guids.squirrelLanguageGuid);

                    processData.language = DkmLanguage.Create("Squirrel", processData.compilerId);
                }

                if (processData.runtimeInstance == null)
                {
                    //DkmRuntimeInstanceId runtimeId = new DkmRuntimeInstanceId(Guids.squirrelRuntimeGuid, 0);

                    //processData.runtimeInstance = DkmCustomRuntimeInstance.Create(process, runtimeId, null);
                    var nativeRuntime = process.GetNativeRuntimeInstance();
                    processData.runtimeInstance = nativeRuntime;
                }

                if (processData.module == null)
                {
                    DkmModuleId moduleId = new DkmModuleId(Guid.NewGuid(), Guids.squirrelSymbolProviderGuid);

                    processData.module = DkmModule.Create(moduleId, "squirrel.vm.code", processData.compilerId, process.Connection, null);
                }

                if (processData.moduleInstance == null)
                {
                    DkmDynamicSymbolFileId symbolFileId = DkmDynamicSymbolFileId.Create(Guids.squirrelSymbolProviderGuid);

                    processData.moduleInstance = DkmCustomModuleInstance.Create("squirrel_vm", "squirrel.vm.code", 0, processData.runtimeInstance, null, symbolFileId, DkmModuleFlags.None, DkmModuleMemoryLayout.Unknown, 0, 1, 0, "Lua vm code", false, null, null, null);

                    processData.moduleInstance.SetModule(processData.module, true);
                }
            }

            return null;
        }
    }
}
