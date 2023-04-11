using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpModelsToJson
{
    public class Model
    {
        public string ModelName { get; set; }
        public IEnumerable<Field> Fields { get; set; }
        public IEnumerable<Property> Properties { get; set; }
        public IEnumerable<string> BaseClasses { get; set; }
    }

    public class Field
    {
        public string Identifier { get; set; }
        public string Type { get; set; }
    }

    public class Property
    {
        public string Identifier { get; set; }
        public string Type { get; set; }
    }

    public class ModelCollector : CSharpSyntaxWalker
    {
        public readonly List<Model> Models = new List<Model>();
        public bool greenlisted = false;
        public bool generateModel = false;
        //public bool generateController = false;

        public ModelCollector(bool greenlisted) => this.greenlisted = greenlisted;

        private bool CheckModel(TypeDeclarationSyntax node)
        {
            if(!greenlisted)
            {
                generateModel = node.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString() == Program.modelAttribute));
            } else
            {
                generateModel = true;
            }
            var baseclasses = node.BaseList?.Types.Select(s => s.ToString());
            if(baseclasses != null && baseclasses.Count() > 0 && Program.controllerParents.Any(p => baseclasses.Any(b => b.EndsWith(p))))
            {
                //Console.WriteLine("Cancel model " + node.Identifier.ToString());
                generateModel = false;
            }
            return generateModel;
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (!CheckModel(node)) return;
            var model = CreateModel(node);
            Models.Add(model);
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            if (!CheckModel(node)) return;
            var model = CreateModel(node);
            Models.Add(model);
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            if (!CheckModel(node)) return;
            var model = new Model()
            {
                ModelName = $"{node.Identifier.ToString()}{node.TypeParameterList?.ToString()}",
                BaseClasses = new List<string>(),
                Fields = node.ParameterList?.Parameters
                                .Where(field => IsAccessible(field.Modifiers))
                                .Where(property => !IsIgnored(property.AttributeLists))
                                .Select((field) => new Field
                                    {
                                        Identifier = field.Identifier.ToString(),
                                        Type = field.Type.ToString(),
                                    }),
                Properties = node.Members.OfType<PropertyDeclarationSyntax>()
                                .Where(property => IsAccessible(property.Modifiers))
                                .Where(property => !IsIgnored(property.AttributeLists))
                                .Select(ConvertProperty),
            };
            Models.Add(model);
        }

        private Model CreateModel(TypeDeclarationSyntax node)
        {
            return new Model()
            {
                ModelName = $"{node.Identifier.ToString()}{node.TypeParameterList?.ToString()}",
                BaseClasses = node.BaseList?.Types.Select(s => s.ToString()),
                Fields = node.Members.OfType<FieldDeclarationSyntax>()
                                .Where(field => IsAccessible(field.Modifiers))
                                .Where(property => !IsIgnored(property.AttributeLists))
                                .Select(ConvertField),
                Properties = node.Members.OfType<PropertyDeclarationSyntax>()
                                .Where(property => IsAccessible(property.Modifiers))
                                .Where(property => !IsIgnored(property.AttributeLists))
                                .Select(ConvertProperty),
            };
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

        private Field ConvertField(FieldDeclarationSyntax field) => new Field
        {
            Identifier = field.Declaration.Variables.First().GetText().ToString(),
            Type = field.Declaration.Type.ToString(),
        };

        private Property ConvertProperty(PropertyDeclarationSyntax property) => new Property
        {
            Identifier = property.Identifier.ToString(),
            Type = property.Type.ToString(),
        };
    }
}