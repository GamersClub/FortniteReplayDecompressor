﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Unreal.Core.Models.Enums;

namespace SourceGeneratorSamples
{
    [Generator]
    public class HelloWorldGenerator : ISourceGenerator
    {
        public void Execute(SourceGeneratorContext context)
        {
            var visitor = new GetAllSymbolsVisitor();
            visitor.Visit(context.Compilation.GlobalNamespace);

            // begin creating the source we'll inject into the users compilation
            var sourceBuilder = new StringBuilder(@"
using Unreal.Core.Attributes;
using Unreal.Core.Models.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;
using System;

namespace HelloWorldGenerated
{
    [NetFieldExportGroup(""/Script/FortniteGame.FortClientObservedStat"", minimalParseMode: ParseMode.Debug)]
    public partial class RandomClass : INetFieldExportGroup
    {
        public object Role { get; set; }
        public float? StormCNDamageVulnerabilityLevel2 { get; set; }

        public void Hello() {
            Console.WriteLine(""--------------- START"");
");

            //foreach (var sym in visitor.Symbols)
            //{
            //    sourceBuilder.Append(TestAdapters(sym));
            //    sourceBuilder.Append($@"Console.WriteLine(""\n"");");
            //}

            //foreach (var sym in visitor.ClassNetCacheSymbols)
            //{
            //    sourceBuilder.Append(TestClassNetCache(sym));
            //    sourceBuilder.Append($@"Console.WriteLine(""\n"");");
            //}

            sourceBuilder.Append($@"Console.WriteLine(""--------------- END"");");

            sourceBuilder.Append(@"
        }
    }    
}");

            context.AddSource("helloWorldGenerated", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));


            context.AddSource("NetFieldParserGenerated", SourceText.From(CreateNetFieldParser(visitor), Encoding.UTF8));

            foreach (var sym in visitor.Symbols)
            {
                context.AddSource($"{sym.Name}AdapterGenerated", SourceText.From(AddAdapters(sym), Encoding.UTF8));
            }
        }

        public void Initialize(InitializationContext context)
        {
            //
        }

        private string CreateNetFieldParser(GetAllSymbolsVisitor visitor)
        {
            var symbols = visitor.Symbols;

            StringBuilder source = new StringBuilder(@"
            using Unreal.Core.Models;
            using Unreal.Core.Models.Contracts;
            using Unreal.Core.Models.Enums;
            using System.Collections.Generic;
            ");

            var netFieldExportGroups = symbols.Where(symbol => !((ParseMode)symbol.GetAttributes().First(attr => attr.AttributeClass.Name.Equals("NetFieldExportGroupAttribute")).ConstructorArguments[1].Value).Equals(ParseMode.Ignore));

            var playerControllers = netFieldExportGroups.Where(symbol => symbol.GetAttributes().Any(attr => attr.AttributeClass.Name.Equals("PlayerControllerAttribute"))).Select(symbol =>
                symbol.GetAttributes().First(attr => attr.AttributeClass.Name.Equals("PlayerControllerAttribute")).ConstructorArguments[0].Value
            ).Select(path => $"\"{path}\"");

            var netFieldGroups = netFieldExportGroups.Select(symbol => symbol.GetAttributes().First(attr => attr.AttributeClass.Name.Equals("NetFieldExportGroupAttribute")))
                .Select(attr => new
                {
                    Path = attr.ConstructorArguments[0].Value,
                    Mode = (ParseMode)attr.ConstructorArguments[1].Value,
                })
                .Select(group => $"{{ \"{group.Path}\", ParseMode.{group.Mode} }}");

            var classNetCaches = AddClassNetCacheProperties(visitor.ClassNetCacheSymbols);

            foreach (var ns in GetUniqueNamespaces(netFieldExportGroups))
            {
                source.Append($"using {ns};");
            }

            source.Append($@"
            namespace Unreal.Core
            {{
                public class NetFieldParserGenerated : INetFieldParser
                {{
                    private static HashSet<string> _playerControllers = new HashSet<string>() {{ {string.Join(",", playerControllers)} }};
                    private static Dictionary<string, ParseMode> _netFieldGroups = new Dictionary<string, ParseMode>() {{ {string.Join(",", netFieldGroups)} }};
                    private static Dictionary<string, ClassNetCache> _classNetCaches = new Dictionary<string, ClassNetCache> {{ { classNetCaches } }};
                    
                    public bool IsPlayerController(string group)
                    {{
                        return _playerControllers.Contains(group);
                    }}

                    public bool WillReadClassNetCache(string group, ParseMode mode)
                    {{
                        if (_classNetCaches.TryGetValue(group, out var cache)) 
                        {{
                            return mode >= cache.Mode;
                        }}
                        return false;
                    }}

                    public bool WillReadType(string group, ParseMode mode)
                    {{
                        if (_netFieldGroups.TryGetValue(group, out var parseMode))
                        {{
                            return mode >= parseMode;
                        }}
                        return false;
                    }}

                    public bool TryGetClassNetCacheProperty(string property, string group, out ClassNetCacheProperty info)
                    {{
                        info = null;
                        if (_classNetCaches.TryGetValue(group, out var cache))
                        {{
                            return cache.Properties.TryGetValue(property, out info);
                        }}
                        return false;
                    }}

                    public INetFieldExportGroupAdapter CreateType(string path)
                    {{
                        switch (path)
                        {{
            ");

            foreach (var symbol in netFieldExportGroups)
            {
                source.Append(AddToCreateType(symbol));
            }

            source.Append(@"
                    default:
                        return null;
                    }
                }"
            );

            source.Append(@"
                }
            }");

            // TODO add parse mode ?
            return source.ToString();
        }

        /// <summary>
        /// Adds switch case for object creation for each class marked with NetFieldExportGroup.
        /// </summary>
        /// <param name="classSymbol"></param>
        private string AddToCreateType(INamedTypeSymbol classSymbol)
        {
            var source = new StringBuilder();

            var attrs = classSymbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass.Name.Equals("NetFieldExportGroupAttribute"));
            if (attrs == null)
            {
                return "";
            }

            var path = (string)attrs.ConstructorArguments[0].Value;
            var mode = (ParseMode)attrs.ConstructorArguments[1].Value;

            if (mode.Equals(ParseMode.Ignore))
            {
                return "";
            }

            source.Append($@"
                case ""{path}"":
                    return new {classSymbol.Name}Adapter();
            ");
            return source.ToString();
        }

        private string AddClassNetCacheProperties(IEnumerable<INamedTypeSymbol> symbols)
        {
            var classNetCaches = new List<string>();
            foreach (var symbol in symbols)
            {
                var attrs = symbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass.Name.Equals("NetFieldExportClassNetCacheAttribute"));
                if (attrs == null)
                {
                    continue;
                }

                var path = (string)attrs.ConstructorArguments[0].Value;
                var mode = (ParseMode)attrs.ConstructorArguments[1].Value;

                if (mode.Equals(ParseMode.Ignore))
                {
                    continue;
                }

                var properties = new List<string>();
                foreach (var property in GetAllProperties(symbol))
                {
                    var rpc = property.GetAttributes().FirstOrDefault(attr => attr.AttributeClass.Name.Equals("NetFieldExportRPCAttribute"));
                    if (rpc != null)
                    {
                        var propertySource = new StringBuilder();

                        var name = (string)rpc.ConstructorArguments[0].Value;
                        var pathname = (string)rpc.ConstructorArguments[1].Value;
                        var function = rpc.ConstructorArguments[2].Value.ToString().ToLower();
                        var checksum = rpc.ConstructorArguments[3].Value.ToString().ToLower();
                        var customStuct = rpc.ConstructorArguments[4].Value.ToString().ToLower();

                        propertySource.Append($@"
                            {{ ""{name}"", new ClassNetCacheProperty {{
                                    Name = ""{name}"",
                                    PathName = ""{pathname}"",
                                    EnablePropertyChecksum = {checksum},
                                    IsCustomStruct = {customStuct},
                                    IsFunction = {function}
                                }}
                            }}
                        ");
                        properties.Add(propertySource.ToString());
                    }
                }

                if (properties.Any())
                {
                    classNetCaches.Add($@"{{ ""{path}"", new ClassNetCache {{
                            Name = ""{path}"",
                            Mode = ParseMode.{mode},
                            Properties = new Dictionary<string, ClassNetCacheProperty> {{ {string.Join(",", properties)} }}
                        }}
                    }}");
                }
            }

            return string.Join(",", classNetCaches);
        }

        /// <summary>
        /// Adds a ReadField(string field, INetBitReader reader) method to each (partial) class marked with NetFieldExportGroup.
        /// </summary>
        /// <param name="classSymbol"></param>
        private string AddAdapters(INamedTypeSymbol classSymbol)
        {
            var attrs = classSymbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass.Name.Equals("NetFieldExportGroupAttribute"));
            if (attrs == null)
            {
                return "";
            }

            var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            // begin building the generated source
            StringBuilder source = new StringBuilder($@"
                using Unreal.Core.Models;
                using Unreal.Core.Models.Contracts;
                using Unreal.Core.Models.Enums;
            ");

            foreach (var ns in GetUniqueNamespaces(GetAllProperties(classSymbol)))
            {
                source.Append($"using {ns};");
            }

            source.Append($@"
            namespace {namespaceName}
            {{
                public class {classSymbol.Name}Adapter : INetFieldExportGroupAdapter<{classSymbol.Name}>
                {{
                    public {classSymbol.Name} Data {{ get; set; }} = new {classSymbol.Name}();

                    public INetFieldExportGroup GetData()
                    {{
                        return Data;
                    }}

                    public bool ReadField(string field, INetBitReader netBitReader) 
                    {{
                        switch (field)
                        {{
            ");

            // create properties for each field 
            foreach (var propertySymbol in GetAllProperties(classSymbol))
            {
                ProcessProperties(source, propertySymbol);
            }

            source.Append(@"
                default:
                    return false;
                }

                return true;
            }");

            source.Append(@"
                }
            }");

            return source.ToString();
        }

        /// <summary>
        /// Adds a switch case for each property marked with NetFieldExportAttribute.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="propertySymbol"></param>
        private string ProcessProperties(StringBuilder source, IPropertySymbol propertySymbol)
        {
            var attrs = propertySymbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass.Name.Equals("NetFieldExportAttribute"));
            if (attrs is not null)
            {
                if (attrs.ConstructorArguments.Length < 2)
                {
                    return "";
                }

                var movement = propertySymbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass.Name.Equals("RepMovementAttribute"));

                var field = (string)attrs.ConstructorArguments[0].Value;
                var repLayout = (RepLayoutCmdType)attrs.ConstructorArguments[1].Value;

                var reader = repLayout switch
                {
                    RepLayoutCmdType.PropertyBool => "netBitReader.SerializePropertyBool();",
                    RepLayoutCmdType.PropertyNativeBool => "netBitReader.SerializePropertyNativeBool();",
                    RepLayoutCmdType.PropertyName => "netBitReader.SerializePropertyName();",
                    RepLayoutCmdType.PropertyFloat => "netBitReader.SerializePropertyFloat();",
                    RepLayoutCmdType.PropertyNetId => "netBitReader.SerializePropertyNetId();",
                    RepLayoutCmdType.PropertyPlane => "throw new NotImplementedException(\"Plane RepLayoutCmdType not implemented\");",
                    RepLayoutCmdType.PropertyObject => "netBitReader.SerializePropertyObject();",
                    RepLayoutCmdType.PropertyRotator => "netBitReader.SerializePropertyRotator();",
                    RepLayoutCmdType.PropertyString => "netBitReader.SerializePropertyString();",
                    RepLayoutCmdType.PropertyVector10 => "netBitReader.SerializePropertyVector10();",
                    RepLayoutCmdType.PropertyVector100 => "netBitReader.SerializePropertyVector100();",
                    RepLayoutCmdType.PropertyVectorNormal => "netBitReader.SerializePropertyVectorNormal();",
                    RepLayoutCmdType.PropertyVectorQ => "netBitReader.SerializePropertyQuantizedVector(VectorQuantization.RoundWholeNumber);",
                    RepLayoutCmdType.PropertyVector => "netBitReader.SerializePropertyVector();",
                    RepLayoutCmdType.PropertyVector2D => "netBitReader.SerializePropertyVector2D();",
                    RepLayoutCmdType.RepMovement => movement != null ? @$"netBitReader.SerializeRepMovement(
                            {movement.ConstructorArguments[0].ToCSharpString()}, 
                            {movement.ConstructorArguments[1].ToCSharpString()}, 
                            {movement.ConstructorArguments[2].ToCSharpString()});" : "netBitReader.SerializeRepMovement();",
                    RepLayoutCmdType.Enum => "netBitReader.SerializePropertyEnum();",
                    RepLayoutCmdType.PropertyByte => "netBitReader.ReadByte();",
                    RepLayoutCmdType.PropertyInt => "netBitReader.ReadInt32();",
                    RepLayoutCmdType.PropertyInt16 => "netBitReader.ReadInt16();",
                    RepLayoutCmdType.PropertyUInt16 => "netBitReader.SerializePropertyUInt16();",
                    RepLayoutCmdType.PropertyUInt32 => "netBitReader.ReadUInt32();",
                    RepLayoutCmdType.PropertyUInt64 => "netBitReader.ReadUInt64();",
                    //RepLayoutCmdType.Property => "(data as IProperty).Serialize(netBitReader); (data as IResolvable)?.Resolve(GuidCache);",
                    _ => "",
                };

                if (string.IsNullOrEmpty(reader))
                {
                    return "";
                }

                source.Append($@"
                    case ""{field}"":
                        Data.{propertySymbol.Name} = {reader};
                        break;
                ");

                return source.ToString();
            }

            return "";
        }

        private static string TestClassNetCache(INamedTypeSymbol classSymbol)
        {
            var attrs = classSymbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass.Name.Equals("NetFieldExportClassNetCacheAttribute"));
            if (attrs == null)
            {
                return "";
            }

            StringBuilder source = new StringBuilder();

            var path = (string)attrs.ConstructorArguments[0].Value;
            var mode = (ParseMode)attrs.ConstructorArguments[1].Value;

            source.Append($@"Console.WriteLine(""{path}"");");
            source.Append($@"Console.WriteLine(""{mode}"");");

            var properties = new List<string>();
            foreach (var property in GetAllProperties(classSymbol))
            {
                var rpc = property.GetAttributes().FirstOrDefault(attr => attr.AttributeClass.Name.Equals("NetFieldExportRPCAttribute"));
                if (rpc != null)
                {
                    var args = rpc.ConstructorArguments.Count();
                    var name = (string)rpc.ConstructorArguments[0].Value;
                    var pathname = (string)rpc.ConstructorArguments[1].Value;
                    var function = rpc.ConstructorArguments[2].Value;
                    var checksum = rpc.ConstructorArguments[3].Value;
                    var customStuct = rpc.ConstructorArguments[4].Value.ToString().ToLower();


                    source.Append($@"Console.WriteLine(""{args}"");");
                    source.Append($@"Console.WriteLine(""{name}"");");
                    source.Append($@"Console.WriteLine(""{pathname}"");");
                    source.Append($@"Console.WriteLine(""{function}"");");
                    source.Append($@"Console.WriteLine(""{checksum}"");");
                    source.Append($@"Console.WriteLine(""{customStuct}"");");
                }
            }

            return source.ToString();
        }

        private static string TestAdapters(INamedTypeSymbol classSymbol)
        {
            var attrs = classSymbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass.Name.Equals("NetFieldExportGroupAttribute"));
            if (attrs == null)
            {
                return "";
            }

            var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            StringBuilder source = new StringBuilder();

            source.Append($@"Console.WriteLine(""{classSymbol.Name}"");");

            var path = (string)attrs.ConstructorArguments[0].Value;
            var parseMode = (ParseMode)attrs.ConstructorArguments[1].Value;


            source.Append($@"Console.WriteLine(""{path}"");");
            source.Append($@"Console.WriteLine(""{parseMode}"");");

            foreach (var ns in GetUniqueNamespaces(GetAllProperties(classSymbol)))
            {
                source.Append($@"Console.WriteLine(""{ns}"");");
            }

            foreach (var propertySymbol in GetAllProperties(classSymbol))
            {
                var exportAttrs = propertySymbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass.Name.Equals("NetFieldExportAttribute"));

                if (exportAttrs is not null)
                {
                    if (attrs.ConstructorArguments.Length < 2)
                    {
                        return "";
                    }

                    var field = (string)attrs.ConstructorArguments[0].Value;
                    var repLayout = (RepLayoutCmdType)attrs.ConstructorArguments[1].Value;

                    source.Append($@"Console.WriteLine(""{propertySymbol.Name}"");");
                    source.Append($@"Console.WriteLine(""{propertySymbol.Type.ToDisplayString()}"");");


                    var movement = propertySymbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass.Name.Equals("RepMovementAttribute"));
                    if (movement != null)
                    {
                        source.Append($@"Console.WriteLine(""{movement.ConstructorArguments[0].ToCSharpString()}"");");
                        source.Append($@"Console.WriteLine(""{movement.ConstructorArguments[1].ToCSharpString()}"");");
                        source.Append($@"Console.WriteLine(""{movement.ConstructorArguments[2].ToCSharpString()}"");");
                    }
                }
            }

            return source.ToString();
        }


        /// <summary>
        /// Get all unique namespaces.
        /// </summary>
        /// <param name="symbols"></param>
        private static IEnumerable<string> GetUniqueNamespaces(IEnumerable<ISymbol> symbols)
        {
            var namespaces = new HashSet<string>();
            foreach (var symbol in symbols)
            {
                namespaces.Add(symbol.ContainingNamespace.ToDisplayString());
            }
            return namespaces;
        }

        /// <summary>
        /// Get all properties of a class, including properties from base classes.
        /// </summary>
        /// <param name="symbol"></param>
        private static IEnumerable<IPropertySymbol> GetAllProperties(INamedTypeSymbol symbol)
        {
            foreach (var prop in symbol.GetMembers().OfType<IPropertySymbol>())
            {
                yield return prop;
            }

            var baseType = symbol.BaseType;
            if (baseType != null)
            {
                foreach (var prop in GetAllProperties(baseType))
                {
                    yield return prop;
                }
            }
        }

        /// <summary>
        /// SymbolVisitor to get all required classes for Fortnite replay parsing from a Compilation.
        /// </summary>
        class GetAllSymbolsVisitor : SymbolVisitor
        {
            public List<INamedTypeSymbol> Symbols { get; set; } = new List<INamedTypeSymbol>();
            public List<INamedTypeSymbol> ClassNetCacheSymbols { get; set; } = new List<INamedTypeSymbol>();

            public override void VisitNamespace(INamespaceSymbol symbol)
            {
                foreach (var s in symbol.GetMembers())
                {
                    s.Accept(this);
                }
            }

            public override void VisitNamedType(INamedTypeSymbol symbol)
            {
                if (symbol.GetAttributes().Any(i => i.AttributeClass.Name.Equals("NetFieldExportGroupAttribute")))
                {
                    Symbols.Add(symbol);
                }

                if (symbol.GetAttributes().Any(i => i.AttributeClass.Name.Equals("NetFieldExportClassNetCacheAttribute")))
                {
                    ClassNetCacheSymbols.Add(symbol);
                }
            }
        }
    }
}