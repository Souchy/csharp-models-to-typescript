using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpModelsToJson
{
    public class Controller
    {
        public string ModelName { get; set; }
        public IEnumerable<Method> Methods { get; set; }
        public IEnumerable<string> BaseClasses { get; set; }
    }

    public class Method
    {
        public string Identifier { get; set; }
        public string Type { get; set; }
        public string Route { get; set; }
        public string HttpMethod { get; set; }
        public IEnumerable<Parameter> Parameters { get; set; }
    }
    public class Parameter
    {
        public string Identifier { get; set; }
        public string Type { get; set; }
    }

    public class ControllerCollector : CSharpSyntaxWalker
    {
        public readonly List<Controller> Controllers = new List<Controller>();
        public bool greenlisted = false;
        public bool generateController = false;

        public ControllerCollector(bool greenlisted) => this.greenlisted = greenlisted;

        private bool CheckController(TypeDeclarationSyntax node)
        {
            if(!greenlisted)
            {
                generateController = node.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString() == Program.controllerAttribute));
            }
            else
            {
                generateController = true;
            }
            var baseclasses = node.BaseList?.Types.Select(s => s.ToString());
            if (baseclasses == null || baseclasses.Count() == 0 || !Program.controllerParents.Any(p => baseclasses.Any(b => b.EndsWith(p))))
            {
                //Console.WriteLine("Cancel controller " + node.Identifier.ToString());
                generateController = false;
            }
            return generateController;
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (!CheckController(node)) return;
            var model = CreateModel(node);
            Controllers.Add(model);
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            if (!CheckController(node)) return;
            var model = CreateModel(node);
            Controllers.Add(model);
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            if (!CheckController(node)) return;
            var model = new Controller()
            {
                ModelName = $"{node.Identifier.ToString()}{node.TypeParameterList?.ToString()}",
                BaseClasses = new List<string>(),
                Methods = generateController ? node.Members.OfType<MethodDeclarationSyntax>()
                                .Where(property => IsAccessible(property.Modifiers))
                                .Where(property => !IsIgnored(property.AttributeLists))
                                .Select(ConvertMethod)
                          : null,
            };
            Controllers.Add(model);
        }

        private Controller CreateModel(TypeDeclarationSyntax node)
        {
            return new Controller()
            {
                ModelName = $"{node.Identifier.ToString()}{node.TypeParameterList?.ToString()}",
                BaseClasses = node.BaseList?.Types.Select(s => s.ToString()),
                Methods = node.Members.OfType<MethodDeclarationSyntax>()
                                .Where(property => IsAccessible(property.Modifiers))
                                .Where(property => !IsIgnored(property.AttributeLists))
                                .Select(ConvertMethod),
            };
        }
        private Method ConvertMethod(MethodDeclarationSyntax node) {
            var m = new Method
            {
                Identifier = node.Identifier.ToString(),
                Type = node.ReturnType.ToString(), 
                Parameters = node.ParameterList?.Parameters.Select(p =>
                {
                    return new Parameter()
                    {
                        Identifier = p.Identifier.ToString(),
                        Type = p.Type.ToString(),
                    };
                }),
            };
            string[] tags = new[] { "HttpGet", "HttpPost", "Route" };
            var attr = node.AttributeLists.SelectMany(al => al.Attributes.Where(a => tags.Contains(a.Name.ToString()))).FirstOrDefault();
            var arg = attr?.ArgumentList.Arguments.FirstOrDefault();
            if (arg != null) m.Route = arg.Expression.ToString();
            if(attr != null)
            {
                m.HttpMethod = attr.Name.ToString();
            }
            return m;
        }

        private bool IsIgnored(SyntaxList<AttributeListSyntax> propertyAttributeLists) => 
            propertyAttributeLists.Any(attributeList => 
                attributeList.Attributes.Any(attribute => 
                    attribute.Name.ToString().Equals("JsonIgnore")));

        private bool IsAccessible(SyntaxTokenList modifiers) => modifiers.All(modifier =>
            modifier.ToString() != "const" &&
            modifier.ToString() != "static" &&
            modifier.ToString() != "private"
        );
    }
}