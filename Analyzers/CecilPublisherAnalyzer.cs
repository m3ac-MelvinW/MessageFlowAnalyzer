using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MessageFlowAnalyzer.Models;

namespace MessageFlowAnalyzer.Analyzers
{
    public class CecilPublisherAnalyzer : BaseAnalyzer
    {
        private readonly List<string> _publisherMethodNames = new()
        {
            "Publish",
            "PublishAsync",
            "Send",
            "SendAsync"
        };

        private readonly List<string> _publisherInterfaceNames = new()
        {
            "IMessagePublisher",
            "IEventBus",
            "IIntegrationEventPublisher",
            "IPublisher"
        };

        public async Task<List<MessagePublisher>> AnalyzeAsync(string assemblyPath, string repoName, bool includeDetails)
        {
            var publishers = new List<MessagePublisher>();

            try
            {
                // Read assembly without loading it into current AppDomain
                using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
                
                Console.WriteLine($"    Analyzing assembly: {Path.GetFileName(assemblyPath)}");
                
                foreach (var module in assembly.Modules)
                {
                    foreach (var type in module.Types.Where(t => !t.IsInterface && !t.IsAbstract))
                    {
                        var typePublishers = AnalyzeType(type, repoName, includeDetails);
                        publishers.AddRange(typePublishers);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Error analyzing assembly {assemblyPath}: {ex.Message}");
            }

            return publishers;
        }

        private List<MessagePublisher> AnalyzeType(TypeDefinition type, string repoName, bool includeDetails)
        {
            var publishers = new List<MessagePublisher>();
            
            // Check if this type might be a Hangfire job
            bool isHangfireJob = IsHangfireJobType(type);
            
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                var methodPublishers = AnalyzeMethod(method, type, repoName, includeDetails, isHangfireJob);
                publishers.AddRange(methodPublishers);
            }

            return publishers;
        }

        private List<MessagePublisher> AnalyzeMethod(MethodDefinition method, TypeDefinition declaringType, 
            string repoName, bool includeDetails, bool isHangfireJob)
        {
            var publishers = new List<MessagePublisher>();

            if (!method.HasBody || method.Body.Instructions == null)
                return publishers;

            for (int i = 0; i < method.Body.Instructions.Count; i++)
            {
                var instruction = method.Body.Instructions[i];
                
                // Look for method call instructions
                if (IsMethodCallInstruction(instruction))
                {
                    var methodRef = instruction.Operand as MethodReference;
                    if (methodRef != null && IsPublishMethodCall(methodRef))
                    {
                        // Found a publish call! Extract the event type
                        var eventInfo = ExtractEventInformation(method.Body.Instructions, i);
                        
                        var publisher = new MessagePublisher
                        {
                            EventName = eventInfo.EventName,
                            Repository = repoName,
                            Project = GetProjectNameFromAssembly(declaringType.Module.Assembly),
                            FilePath = GetSourceFileFromType(declaringType),
                            ClassName = declaringType.Name,
                            MethodName = method.Name,
                            LineNumber = GetLineNumber(instruction, method),
                            CodeContext = includeDetails ? GetCodeContext(method, instruction) : $"Call to {methodRef.Name}",
                            IsInHangfireJob = isHangfireJob,
                            HangfireJobClass = isHangfireJob ? declaringType.Name : null
                        };

                        publishers.Add(publisher);
                        
                        Console.WriteLine($"      Found publisher: {publisher.ClassName}.{publisher.MethodName}() -> {publisher.EventName}");
                    }
                }
            }

            return publishers;
        }

        private bool IsMethodCallInstruction(Instruction instruction)
        {
            return instruction.OpCode == OpCodes.Call || 
                   instruction.OpCode == OpCodes.Callvirt || 
                   instruction.OpCode == OpCodes.Calli;
        }

        private bool IsPublishMethodCall(MethodReference methodRef)
        {
            // Check if method name matches publisher method names
            if (!_publisherMethodNames.Contains(methodRef.Name))
                return false;

            // Check if the declaring type or any of its interfaces match publisher interfaces
            return IsPublisherType(methodRef.DeclaringType);
        }

        private bool IsPublisherType(TypeReference typeRef)
        {
            try
            {
                // Resolve the type to get full information
                var typeDef = typeRef.Resolve();
                if (typeDef == null) return false;

                // Check the type name directly
                if (_publisherInterfaceNames.Any(name => typeDef.Name.Contains(name)))
                    return true;

                // Check interfaces implemented by this type
                foreach (var interfaceImpl in typeDef.Interfaces)
                {
                    var interfaceType = interfaceImpl.InterfaceType;
                    if (_publisherInterfaceNames.Any(name => interfaceType.Name.Contains(name)))
                        return true;
                }

                // Check base types
                var baseType = typeDef.BaseType;
                while (baseType != null)
                {
                    if (_publisherInterfaceNames.Any(name => baseType.Name.Contains(name)))
                        return true;
                    
                    var baseTypeDef = baseType.Resolve();
                    baseType = baseTypeDef?.BaseType;
                }
            }
            catch
            {
                // If we can't resolve the type, make a best guess based on name
                return _publisherInterfaceNames.Any(name => typeRef.Name.Contains(name));
            }

            return false;
        }

        private EventInformation ExtractEventInformation(IList<Instruction> instructions, int publishCallIndex)
        {
            var eventInfo = new EventInformation { EventName = "Unknown" };

            // Look backwards from the publish call to find the event being created
            for (int i = publishCallIndex - 1; i >= Math.Max(0, publishCallIndex - 20); i--)
            {
                var instruction = instructions[i];

                // Look for object creation (newobj)
                if (instruction.OpCode == OpCodes.Newobj && instruction.Operand is MethodReference constructor)
                {
                    var eventType = constructor.DeclaringType;
                    if (IsIntegrationEventType(eventType))
                    {
                        eventInfo.EventName = eventType.Name;
                        eventInfo.FullEventName = eventType.FullName;
                        break;
                    }
                }

                // Look for local variable loads that might contain the event
                if (instruction.OpCode == OpCodes.Ldloc || 
                    instruction.OpCode == OpCodes.Ldloc_0 || 
                    instruction.OpCode == OpCodes.Ldloc_1 || 
                    instruction.OpCode == OpCodes.Ldloc_2 || 
                    instruction.OpCode == OpCodes.Ldloc_3 ||
                    instruction.OpCode == OpCodes.Ldloc_S)
                {
                    // Try to find where this local variable was assigned
                    var eventName = TraceLocalVariableAssignment(instructions, i, publishCallIndex);
                    if (!string.IsNullOrEmpty(eventName))
                    {
                        eventInfo.EventName = eventName;
                        break;
                    }
                }

                // Look for field loads (instance variables like _someEvent)
                if (instruction.OpCode == OpCodes.Ldfld && instruction.Operand is FieldReference field)
                {
                    if (IsIntegrationEventType(field.FieldType))
                    {
                        eventInfo.EventName = field.FieldType.Name;
                        eventInfo.FullEventName = field.FieldType.FullName;
                        break;
                    }
                }
            }

            return eventInfo;
        }

        private string TraceLocalVariableAssignment(IList<Instruction> instructions, int ldlocIndex, int maxIndex)
        {
            // Get the local variable being loaded
            var ldlocInstruction = instructions[ldlocIndex];
            var localVar = GetLocalVariable(ldlocInstruction);
            
            if (localVar == null) return null;

            // Look backwards for where this local variable was stored (stloc)
            for (int i = ldlocIndex - 1; i >= 0; i--)
            {
                var instruction = instructions[i];
                
                if (IsStoreLocalInstruction(instruction))
                {
                    var storedVar = GetLocalVariable(instruction);
                    if (storedVar == localVar)
                    {
                        // Found where the variable was stored, now look at the previous instruction
                        // to see what was stored
                        if (i > 0)
                        {
                            var previousInstruction = instructions[i - 1];
                            if (previousInstruction.OpCode == OpCodes.Newobj && 
                                previousInstruction.Operand is MethodReference constructor)
                            {
                                if (IsIntegrationEventType(constructor.DeclaringType))
                                {
                                    return constructor.DeclaringType.Name;
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        private VariableDefinition GetLocalVariable(Instruction instruction)
        {
            return instruction.OpCode.Code switch
            {
                Code.Ldloc_0 or Code.Stloc_0 => instruction.Operand as VariableDefinition ?? GetVariableByIndex(instruction, 0),
                Code.Ldloc_1 or Code.Stloc_1 => instruction.Operand as VariableDefinition ?? GetVariableByIndex(instruction, 1),
                Code.Ldloc_2 or Code.Stloc_2 => instruction.Operand as VariableDefinition ?? GetVariableByIndex(instruction, 2),
                Code.Ldloc_3 or Code.Stloc_3 => instruction.Operand as VariableDefinition ?? GetVariableByIndex(instruction, 3),
                Code.Ldloc_S or Code.Stloc_S or Code.Ldloc or Code.Stloc => instruction.Operand as VariableDefinition,
                _ => null
            };
        }

        private VariableDefinition GetVariableByIndex(Instruction instruction, int index)
        {
            // This is a helper to get variables by index when not directly available
            // In practice, you'd need access to the method's local variables collection
            return null;
        }

        private bool IsStoreLocalInstruction(Instruction instruction)
        {
            return instruction.OpCode == OpCodes.Stloc ||
                   instruction.OpCode == OpCodes.Stloc_0 ||
                   instruction.OpCode == OpCodes.Stloc_1 ||
                   instruction.OpCode == OpCodes.Stloc_2 ||
                   instruction.OpCode == OpCodes.Stloc_3 ||
                   instruction.OpCode == OpCodes.Stloc_S;
        }

        private bool IsIntegrationEventType(TypeReference typeRef)
        {
            if (typeRef == null) return false;

            // Check if the type name suggests it's an integration event
            if (typeRef.Name.EndsWith("IntegrationEvent") || 
                typeRef.Name.EndsWith("Event") ||
                typeRef.Name.EndsWith("Message"))
                return true;

            try
            {
                // Check inheritance chain
                var typeDef = typeRef.Resolve();
                if (typeDef == null) return false;

                var baseType = typeDef.BaseType;
                while (baseType != null)
                {
                    if (baseType.Name == "IntegrationEvent" || 
                        baseType.Name.EndsWith("IntegrationEvent"))
                        return true;
                    
                    var baseTypeDef = baseType.Resolve();
                    if (baseTypeDef == null) break;
                    baseType = baseTypeDef.BaseType;
                }
            }
            catch
            {
                // If we can't resolve, fall back to name-based detection
            }

            return false;
        }

        private bool IsHangfireJobType(TypeDefinition type)
        {
            // Check for Hangfire attributes
            if (type.HasCustomAttributes)
            {
                foreach (var attr in type.CustomAttributes)
                {
                    var attrName = attr.AttributeType.Name;
                    if (_hangfireIndicators.Any(indicator => attrName.Contains(indicator.Replace("[", "").Replace("]", ""))))
                    {
                        return true;
                    }
                }
            }

            // Check for Hangfire interfaces
            if (type.HasInterfaces)
            {
                foreach (var interfaceImpl in type.Interfaces)
                {
                    if (interfaceImpl.InterfaceType.Name.Contains("IJob"))
                        return true;
                }
            }

            // Check method attributes for Hangfire indicators
            foreach (var method in type.Methods)
            {
                if (method.HasCustomAttributes)
                {
                    foreach (var attr in method.CustomAttributes)
                    {
                        var attrName = attr.AttributeType.Name;
                        if (_hangfireIndicators.Any(indicator => attrName.Contains(indicator.Replace("[", "").Replace("]", ""))))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private int GetLineNumber(Instruction instruction, MethodDefinition method)
        {
            // Try to get line number from debug symbols if available
            if (method.DebugInformation?.HasSequencePoints == true)
            {
                var sequencePoint = method.DebugInformation.SequencePoints
                    .FirstOrDefault(sp => sp.Offset <= instruction.Offset);
                
                if (sequencePoint != null)
                    return sequencePoint.StartLine;
            }

            return 0; // No debug info available
        }

        private string GetCodeContext(MethodDefinition method, Instruction targetInstruction)
        {
            // Build a simple context showing the IL instructions around the target
            var context = new List<string>();
            var instructions = method.Body.Instructions;
            var targetIndex = instructions.IndexOf(targetInstruction);
            
            var start = Math.Max(0, targetIndex - 2);
            var end = Math.Min(instructions.Count - 1, targetIndex + 2);
            
            for (int i = start; i <= end; i++)
            {
                var prefix = i == targetIndex ? ">>> " : "    ";
                var inst = instructions[i];
                context.Add($"{prefix}IL_{inst.Offset:X4}: {inst.OpCode} {inst.Operand}");
            }
            
            return string.Join("\n", context);
        }

        private string GetSourceFileFromType(TypeDefinition type)
        {
            // Try to get source file from debug information
            if (type.HasCustomAttributes)
            {
                var sourceFileAttr = type.CustomAttributes
                    .FirstOrDefault(attr => attr.AttributeType.Name.Contains("CompilerGenerated"));
                // This is simplified - actual source file extraction is more complex
            }

            return $"{type.Module.Assembly.Name.Name}.dll";
        }

        private string GetProjectNameFromAssembly(AssemblyDefinition assembly)
        {
            return assembly.Name.Name;
        }

        private class EventInformation
        {
            public string EventName { get; set; } = "Unknown";
            public string FullEventName { get; set; } = "";
        }
    }
}