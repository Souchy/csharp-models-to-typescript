using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
 
namespace CSharpModelsToJson
{
    class Enum
    {
        public string Identifier { get; set; }
        public Dictionary<string, object> Values { get; set; }
    }

    class EnumCollector: CSharpSyntaxWalker
    {
        public readonly List<Enum> Enums = new List<Enum>();
        public bool greenlisted = false;
        public bool generateModel = false;

        public EnumCollector(bool greenlisted) => this.greenlisted = greenlisted;

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            if (!greenlisted)
            {
                generateModel = node.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString() == Program.modelAttribute));
                if (!generateModel) return;
            }

            var values = new Dictionary<string, object>();

            foreach (var member in node.Members) {
                values[member.Identifier.ToString()] = member.EqualsValue != null
                    ? member.EqualsValue.Value.ToString()
                    : null;
            }

            this.Enums.Add(new Enum() {
                Identifier = node.Identifier.ToString(),
                Values = values
            });
        }
    }
}
