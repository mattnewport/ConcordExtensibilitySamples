// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using EnvDTE;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Breakpoints;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Native;
using Microsoft.VisualStudio.Debugger.Script;
using Microsoft.VisualStudio.Debugger.Symbols;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace HelloWorld
{
    internal class SquirrelLocalProcessData : DkmDataItem
    {
        public DkmRuntimeInstance runtimeInstance = null;
        public DkmCustomModuleInstance moduleInstance = null;
    }

    /// <summary>
    /// Note that the list of interfaces implemented is defined here, and in 
    /// HelloWorld.vsdconfigxml. Both lists need to be the same.
    /// </summary>
    public class HelloWorldService : IDkmCallStackFilter, IDkmModuleInstanceLoadNotification, IDkmSymbolQuery,
        IDkmSymbolDocumentCollectionQuery
    {
        #region IDkmCallStackFilter Members

        string GetVmAddress(DkmRuntimeInstance runtimeInstance, DkmThread dkmThread, DkmStackWalkFrame input)
        {
            var expr = "this";

            var language = DkmLanguage.Create("C++",
                new DkmCompilerId(DkmVendorId.Microsoft, DkmLanguageId.Cpp));
            var langExpr = DkmLanguageExpression.Create(language, DkmEvaluationFlags.None, expr, null);

            var session = DkmInspectionSession.Create(dkmThread.Process, null);

            var inspectionContext = DkmInspectionContext.Create(
                session,
                runtimeInstance,
                dkmThread,
                5000, // 5s timeout
                DkmEvaluationFlags.IncreaseMaxStringSize,
                DkmFuncEvalFlags.None,
                10, language, null);

            var res = "";

            var workList = DkmWorkList.Create(null);

            inspectionContext.EvaluateExpression(
                workList, langExpr, input,
                result =>
                {
                    if (result.ErrorCode == 0 &&
                        result.ResultObject is DkmSuccessEvaluationResult success &&
                        success.Value is string value)
                    {
                        res = value;
                    }

                    result.ResultObject?.Close();
                });

            workList.Execute();
            return res;
        }

        DkmStackWalkFrame[] IDkmCallStackFilter.FilterNextFrame(DkmStackContext stackContext, DkmStackWalkFrame input)
        {
            // The HelloWorld sample is a very simple debugger component which modified the call stack
            // so that there is a '[Hello World]' frame at the top of the call stack. All the frames
            // below this are left the same.

            if (input == null) // null input frame indicates the end of the call stack. This sample does nothing on end-of-stack.
                return null;

            // Get the HelloWorldDataItem which is associated with this stack walk. This
            // lets us keep data associated with this stack walk.
            var dataItem = HelloWordDataItem.GetInstance(stackContext);
            DkmStackWalkFrame[] frames = null;

            // Now use this data item to see if we are looking at the first (top-most) frame
            if (dataItem.State == State.Initial)
            {
                // Get the function name for this frame
                var functionName = input.BasicSymbolInfo?.MethodName ?? "";

                if (functionName.Contains("SQVM::Execute"))
                {
                    var process = stackContext.InspectionSession.Process;

                    var processData = DebugHelpers.GetOrCreateDataItem<SquirrelLocalProcessData>(process);

                    if (processData.runtimeInstance == null)
                    {
                        processData.runtimeInstance = process.GetNativeRuntimeInstance();
                        //processData.runtimeInstance = process.GetRuntimeInstances().OfType<DkmCustomRuntimeInstance>().FirstOrDefault(el => el.Id.RuntimeType == Guids.squirrelRuntimeGuid);

                        if (processData.runtimeInstance == null)
                            return new DkmStackWalkFrame[1] { input };

                        processData.moduleInstance = processData.runtimeInstance.GetModuleInstances()
                            .OfType<DkmCustomModuleInstance>().FirstOrDefault(el =>
                                el.Module != null && el.Module.CompilerId.VendorId == Guids.squirrelCompilerGuid);

                        if (processData.moduleInstance == null)
                            return new DkmStackWalkFrame[1] { input };
                    }

                    var vmAddress = GetVmAddress(input.RuntimeInstance, stackContext.Thread, input);
                    if (!dataItem.VmCallStacks.TryGetValue(vmAddress, out var callstack))
                    {
                        var expr = "Squirrel_GetStackInfo(this)";

                        var language = DkmLanguage.Create("C++",
                            new DkmCompilerId(DkmVendorId.Microsoft, DkmLanguageId.Cpp));
                        var langExpr = DkmLanguageExpression.Create(language, DkmEvaluationFlags.None, expr, null);

                        var session = DkmInspectionSession.Create(stackContext.Thread.Process, null);
                        var thread = stackContext.Thread;

                        var inspectionContext = DkmInspectionContext.Create(
                            session,
                            input.RuntimeInstance,
                            thread,
                            5000, // 5s timeout
                            DkmEvaluationFlags.IncreaseMaxStringSize,
                            DkmFuncEvalFlags.None,
                            10, language, null);

                        var workList = DkmWorkList.Create(null);

                        inspectionContext.EvaluateExpression(
                            workList, langExpr, input,
                            result =>
                            {
                                if (result.ErrorCode == 0 &&
                                    result.ResultObject is DkmSuccessEvaluationResult success &&
                                    success.Value is string value)
                                {
                                    // Extract content inside quotes
                                    var match = Regex.Match(value, "\"([^\"]*)\"");
                                    if (match.Success)
                                    {
                                        var insideQuotes = match.Groups[1].Value;
                                        // Split on literal \n
                                        var scriptFunctionNames = insideQuotes.Split(new[] { "\\n" },
                                            StringSplitOptions.RemoveEmptyEntries);
                                        callstack = scriptFunctionNames.ToList();
                                        dataItem.VmCallStacks.Add(vmAddress, callstack);
                                    }
                                }

                                result.ResultObject?.Close();
                            });

                        workList.Execute();
                    }

                    var isTopFrame = (input.Flags & DkmStackWalkFrameFlags.TopFrame) != 0;

                    bool first = true;

                    var framesList = callstack
                        .TakeWhile(s =>
                        {
                            if (first)
                            {
                                first = false;
                                return true; // Always include the first frame
                            }
                            return !s.Contains("NATIVE");
                        })
                        .Select(scriptFunctionName =>
                    {
                        var frameMatch = Regex.Match(scriptFunctionName,
                            @"\]\s+([^\s]+)\s+line\s+\[(\d+)\]$");
                        var path = "";
                        var lineNumber = 0;
                        if (frameMatch.Success)
                        {
                            // Join the path with C:\depot\r5dev\game\r2\scripts\vscripts
                            path = frameMatch.Groups[1].Value;
                            if (!Path.IsPathRooted(path))
                            {
                                path = Path.Combine(@"C:\depot\r5dev\game\r2\scripts\vscripts",
                                    path);
                            }

                            lineNumber = int.Parse(frameMatch.Groups[2].Value);
                        }

                        var additionalData =
                            new ReadOnlyCollection<byte>(
                                Encoding.UTF8.GetBytes($"{path},{lineNumber}"));

                        var instructionAddress = DkmCustomInstructionAddress.Create(
                            processData.runtimeInstance,
                            processData.moduleInstance, // Use the script module instance
                            null,
                            0,
                            additionalData,
                            null
                        );

                        return DkmStackWalkFrame.Create(
                            stackContext.Thread,
                            instructionAddress,
                            input.FrameBase, // Use the same frame base as the input frame
                            0, // annotated frame uses zero bytes
                            DkmStackWalkFrameFlags.None,
                            scriptFunctionName
                                .Trim(), // Description of the frame which will appear in the call stack
                            null, // Annotated frame, so no registers
                            null
                        );
                    }
                    ).ToList();

                    dataItem.VmCallStacks[vmAddress] = callstack.SkipWhile(s => !s.Contains("NATIVE")).ToList();

                    if (!isTopFrame)
                    {
                        // If this is not the top frame, we add a transition frame to indicate that we are transitioning to Squirrel code
                        framesList.Insert(0,
                            DkmStackWalkFrame.Create(stackContext.Thread, null, input.FrameBase,
                                input.FrameSize, DkmStackWalkFrameFlags.FakeFrame,
                                "[Transition to Native]", input.Registers, input.Annotations));
                    }

                    framesList.Add(DkmStackWalkFrame.Create(stackContext.Thread, null, input.FrameBase,
                        input.FrameSize, DkmStackWalkFrameFlags.FakeFrame, "[Transition to Squirrel]",
                        input.Registers, input.Annotations));
                    framesList.Add(input); // Add the original input frame at the end
                    frames = framesList.ToArray();
                }

                if (frames is null)
                {
                    frames = new DkmStackWalkFrame[1] { input };
                }

                // Update our state so that on the next frame we know not to add '[Hello World]' again.
                //dataItem.State = State.HelloWorldFrameAdded;
            }
            else
            {
                // We have already added '[Hello World]' to this call stack, so just return
                // the input frame.

                frames = new DkmStackWalkFrame[1] { input };
            }

            return frames;
        }

        public void OnModuleInstanceLoad(DkmModuleInstance moduleInstance, DkmWorkList workList,
            DkmEventDescriptorS eventDescriptor)
        {
            if (moduleInstance is DkmNativeModuleInstance nativeModuleInstance)
            {
                var process = moduleInstance.Process;

                var processData = DebugHelpers.GetOrCreateDataItem<SquirrelLocalProcessData>(process);

                var moduleName = nativeModuleInstance.FullName;

                if (moduleName != null && moduleName.EndsWith(".exe"))
                {
                    // Request the RemoteComponent to create the runtime and a module
                    DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid,
                        MessageToRemote.createRuntime, null, null).SendLower();
                }
            }
        }

        object IDkmSymbolQuery.GetSymbolInterface(DkmModule module, Guid interfaceID)
        {
            return module.GetSymbolInterface(interfaceID);
        }

        DkmSourcePosition IDkmSymbolQuery.GetSourcePosition(DkmInstructionSymbol instruction,
            DkmSourcePositionFlags flags, DkmInspectionSession inspectionSession, out bool startOfLine)
        {
            var process = inspectionSession?.Process;

            if (process == null)
            {
                var moduleInstance = instruction.Module.GetModuleInstances().OfType<DkmCustomModuleInstance>()
                    .FirstOrDefault(el => el.Module.CompilerId.VendorId == Guids.squirrelCompilerGuid);

                if (moduleInstance == null)
                    return instruction.GetSourcePosition(flags, inspectionSession, out startOfLine);

                process = moduleInstance.Process;
            }

            var processData = DebugHelpers.GetOrCreateDataItem<SquirrelLocalProcessData>(process);

            var instructionSymbol = instruction as DkmCustomInstructionSymbol;

            Debug.Assert(instructionSymbol != null);

            if (instructionSymbol.AdditionalData != null)
            {
                var additionalData = Encoding.UTF8.GetString(instructionSymbol.AdditionalData.ToArray()).TrimEnd('\0');
                var match = Regex.Match(additionalData, @"^(.*?),(.*)$");
                if (!match.Success)
                {
                    return instruction.GetSourcePosition(flags, inspectionSession, out startOfLine);
                }

                var filePath = match.Groups[1].Value.Trim();
                var lineNumber = int.Parse(match.Groups[2].Value.Trim());

                startOfLine = true;
                return DkmSourcePosition.Create(DkmSourceFileId.Create(filePath, null, null, null),
                    new DkmTextSpan(lineNumber, lineNumber, 0, 0));
            }

            return instruction.GetSourcePosition(flags, inspectionSession, out startOfLine);
        }

        DkmResolvedDocument[] IDkmSymbolDocumentCollectionQuery.FindDocuments(DkmModule module,
            DkmSourceFileId sourceFileId)
        {
            return null;
        }

        #endregion
    }
}
