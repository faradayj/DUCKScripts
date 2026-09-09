using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace SkuaCompileTester
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--inspect")
            {
                Console.WriteLine("SkuaCompileTester inspect mode ready.");
                return 0;
            }
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: SkuaCompileTester <path_to_script.cs>");
                return 1;
            }

            string targetScript = Path.GetFullPath(args[0]);
            if (!File.Exists(targetScript))
            {
                Console.WriteLine($"Error: File not found -> {targetScript}");
                return 1;
            }

            string baseDir = GetSkuaScriptsBaseDir(targetScript);
            if (string.IsNullOrEmpty(baseDir))
            {
                Console.WriteLine("Error: Could not determine Skua 'Scripts' base directory.");
                return 1;
            }

            Console.WriteLine($"Base Scripts Dir: {baseDir}");

            HashSet<string> collectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectDependencies(targetScript, baseDir, collectedFiles);

            Console.WriteLine($"\nCompiling {collectedFiles.Count} files...");

            List<SyntaxTree> syntaxTrees = new List<SyntaxTree>();
            foreach (var file in collectedFiles)
            {
                string code = File.ReadAllText(file);
                // Parse it using C# 12 (like Skua does)
                SyntaxTree tree = CSharpSyntaxTree.ParseText(code, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12), path: file);
                syntaxTrees.Add(tree);
            }

            string globalUsings = @"
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Windows.Forms;
global using CommunityToolkit.Mvvm.DependencyInjection;
";
            SyntaxTree globalTree = CSharpSyntaxTree.ParseText(globalUsings, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12), path: "GlobalUsings.cs");
            syntaxTrees.Add(globalTree);

            // Load references
            string assembliesPath = @"C:\Program Files\Skua\Assemblies";
            List<MetadataReference> references = new List<MetadataReference>();

            // Add standard BCL references via TrustPlatformAssemblies
            var trustedAssembliesPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
            foreach (var path in trustedAssembliesPaths)
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }

            // Add Skua dependencies
            string[] skuaDlls = { "Skua.Core.dll", "Skua.Core.Interfaces.dll", "Skua.Core.Models.dll", "Skua.Core.Utils.dll", "Skua.WPF.dll" };
            foreach (var dll in skuaDlls)
            {
                string path = Path.Combine(assembliesPath, dll);
                if (File.Exists(path))
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
            }

            string jsonPath = @"C:\Program Files\Skua\Newtonsoft.Json.dll";
            if (File.Exists(jsonPath))
            {
                references.Add(MetadataReference.CreateFromFile(jsonPath));
            }

            var compilation = CSharpCompilation.Create(
                "SkuaScriptCompilation",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            using var ms = new MemoryStream();
            EmitResult result = compilation.Emit(ms);

            if (!result.Success)
            {
                Console.WriteLine("\n[ BUILD FAILED OR HAS WARNINGS ]");
            }
            else
            {
                Console.WriteLine("\n[ BUILD SUCCEEDED ]");
            }

            foreach (var diagnostic in result.Diagnostics)
            {
                var lineSpan = diagnostic.Location.GetLineSpan();
                Console.WriteLine($"{lineSpan.Path}({lineSpan.StartLinePosition.Line + 1},{lineSpan.StartLinePosition.Character + 1}): {diagnostic.Severity} {diagnostic.Id}: {diagnostic.GetMessage()}");
            }

            return result.Success ? 0 : 1;
        }

        static void CollectDependencies(string currentFile, string baseDir, HashSet<string> collected)
        {
            currentFile = Path.GetFullPath(currentFile);
            if (collected.Contains(currentFile))
                return;
            
            if (!File.Exists(currentFile))
            {
                Console.WriteLine($"Warning: Included file not found: {currentFile}");
                return;
            }

            collected.Add(currentFile);
            Console.WriteLine($"Included: {currentFile}");

            string[] lines = File.ReadAllLines(currentFile);
            Regex csIncludeRegex = new Regex(@"^\s*//cs_include\s+(.+)$", RegexOptions.IgnoreCase);

            foreach (string line in lines)
            {
                Match match = csIncludeRegex.Match(line);
                if (match.Success)
                {
                    string includePath = match.Groups[1].Value.Trim().Replace("/", @"\");
                    string fullPath = "";
                    
                    if (includePath.StartsWith(@"Scripts\", StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = includePath.Substring(8);
                        fullPath = Path.Combine(baseDir, relative);
                    }
                    else
                    {
                        fullPath = Path.Combine(baseDir, includePath);
                    }

                    CollectDependencies(fullPath, baseDir, collected);
                }
            }
        }

        static string GetSkuaScriptsBaseDir(string path)
        {
            DirectoryInfo? di = new DirectoryInfo(Path.GetDirectoryName(path)!);
            while (di != null)
            {
                if (di.Name.Equals("Scripts", StringComparison.OrdinalIgnoreCase))
                {
                    return di.FullName;
                }
                di = di.Parent;
            }
            return @"C:\Users\farad\AppData\Roaming\Skua\Scripts";
        }
    }
}
