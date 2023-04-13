using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Ganss.IO;
using System.Xml.Linq;
using System.Linq;
using Microsoft.Extensions.FileSystemGlobbing.Internal;
using System;

namespace CSharpModelsToJson
{
    class File
    {
        public string FileName { get; set; }
        public string Namespace { get; set; }
        public HashSet<string> Imports { get; set; } = new HashSet<string>();
        public IEnumerable<Model> Models { get; set; }
        public IEnumerable<Enum> Enums { get; set; }
        public IEnumerable<Controller> Controllers { get; set; }
    }

    class Program
    {
        public static List<string> include = new List<string>();
        public static List<string> exclude = new List<string>();
        public static List<string> controllerParents = new List<string>();
        public static string root = "";
        public static string modelAttribute = "";
        public static string controllerAttribute = "";


        static void Main(string[] args)
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile(args[0], true, true)
                .Build();

            root = (string) config.GetValue(typeof(string), nameof(root));
            modelAttribute = (string)config.GetValue(typeof(string), nameof(modelAttribute));
            controllerAttribute = (string)config.GetValue(typeof(string), nameof(controllerAttribute));
            config.Bind(nameof(include), include);
            config.Bind(nameof(exclude), exclude);
            config.Bind(nameof(controllerParents), controllerParents);
            //Console.WriteLine("controllerParents: " + string.Join(", ", controllerParents));
            if (root == "") return;

            List<File> files = new List<File>();
            var sourceFiles = Glob.Expand(root).Select(p => p.FullName);

            foreach (string fileName in sourceFiles) {
                var output = parseFile(fileName);
                if(output != null) 
                    files.Add(output);
            }
            AddImports(files);

            string json = JsonConvert.SerializeObject(files);
            System.Console.WriteLine(json);
        }

        static List<string> getFileNames(List<string> includes, List<string> excludes) {
            List<string> fileNames = new List<string>();

            foreach (var path in expandGlobPatterns(includes)) {
                fileNames.Add(path);
            }

            foreach (var path in expandGlobPatterns(excludes)) {
                fileNames.Remove(path);
            }

            return fileNames;
        }

        static List<string> expandGlobPatterns(List<string> globPatterns) {
            List<string> fileNames = new List<string>();

            foreach (string pattern in globPatterns) {
                var paths = Glob.Expand(pattern);

                foreach (var path in paths) {
                    fileNames.Add(path.FullName);
                }
            }

            return fileNames;
        }

        static File parseFile(string path) {
            path = path.Replace("\\", "/");
            string source = System.IO.File.ReadAllText(path);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
            var root = (CompilationUnitSyntax) tree.GetRoot();
 
            var hasModelAttribute = root.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString() == Program.modelAttribute));
            var hasControllerAttribute = root.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString() == Program.controllerAttribute));
            var excluded = exclude.Any(i => path.Contains(i));
            var included = include.Any(i => path.Contains(i));

            var modelCollector = new ModelCollector(included && !excluded);
            var enumCollector = new EnumCollector(included && !excluded);
            var controllerCollector = new ControllerCollector(included && !excluded);
            
            //if (path.Contains("HomeController"))
            //{
            //    Console.WriteLine($"File: {path}, {hasControllerAttribute}, {included}, {excluded}, {controllerCollector.generateController}");
            //}
            //Console.WriteLine($"File: {path}, {hasAttribute}, {included}, {excluded}");

            //Console.WriteLine("File accepted: " + path);
            modelCollector.Visit(root);
            enumCollector.Visit(root);
            controllerCollector.Visit(root);

            if (modelCollector.Models.Count > 0 || enumCollector.Enums.Count > 0 || controllerCollector.Controllers.Count > 0)
            {
                return new File()
                {
                    FileName = System.IO.Path.GetFullPath(path).Replace("\\", "/"),
                    Namespace = root.ChildNodes().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString(),
                    Models = modelCollector.Models,
                    Enums = enumCollector.Enums,
                    Controllers = controllerCollector.Controllers
                };
            }
            return null;
        }

        static void AddImports(List<File> files)
        {
            foreach(var file in files)
            {
                if (file == null) continue;
                foreach (var m in file.Models)
                {
                    if (m == null) continue;
                    if(m.Fields != null) 
                        foreach (var field in m.Fields)
                        {

                            file.Imports.Add(FindFilepathContainingType(files, field.Type));
                        }
                    if (m.Properties != null)
                        foreach (var prop in m.Properties)
                        {

                            file.Imports.Add(FindFilepathContainingType(files, prop.Type));
                        }
                }
                foreach (var c in file.Controllers)
                {
                    if (c.Methods != null)
                        foreach (var method in c.Methods)
                        {
                            file.Imports.Add(FindFilepathContainingType(files, method.Type));
                            foreach (var par in method.Parameters)
                            {
                                file.Imports.Add(FindFilepathContainingType(files, par.Type));
                            }
                        }
                }
            }
        }

        static string FindFilepathContainingType(List<File> files, string type)
        {
            foreach (var f in files)
            {
                foreach(var m in f.Models)
                {
                    if (m.ModelName == type) return f.FileName;
                }
                foreach(var e in f.Enums)
                {
                    if (e.Identifier == type) return f.FileName;
                }
                foreach(var c in f.Controllers)
                {
                    if (c.ModelName == type) return f.FileName;
                }
            }
            return "";
        }

    }
}