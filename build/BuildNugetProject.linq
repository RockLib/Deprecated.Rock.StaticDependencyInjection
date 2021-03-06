<Query Kind="Program" />

void Main()
{
    var buildDir = Path.GetDirectoryName(Util.CurrentQueryPath);
    var rootDir = new Uri(Path.Combine(buildDir, "..")).LocalPath;
    var srcDir = Path.Combine(rootDir, "src");
    var staticDependencyInjectionDir = Path.Combine(srcDir, "StaticDependencyInjection");
    var artifactsDir = CreateSpecificArtifactDirectory(rootDir);
    var contentDir = CreateContentDirectory(artifactsDir);
    var toolsDir = CreateToolsDirectory(artifactsDir, buildDir);
    
    var generatedFileTemplate = GetGeneratedFileTemplate(staticDependencyInjectionDir);
    var nonGeneratedFileTemplates = GetNonGeneratedFileTemplates(staticDependencyInjectionDir);
    
    var version = GetVersion(srcDir);

    WriteContentFiles(contentDir, generatedFileTemplate, nonGeneratedFileTemplates);
    WriteNuspecFile(buildDir, artifactsDir, generatedFileTemplate, nonGeneratedFileTemplates, version);
    
    artifactsDir.Dump(); // This allows build.cmd to use the artifactsDir for nuget pack.
}

private static string CreateSpecificArtifactDirectory(string rootDir)
{
    const string nowFormat = "yyyy-MM-dd_HH-mm-ss-ffff";
    
    var rootArtifactsDir = Path.Combine(rootDir, "artifacts");
    
    if (!Directory.Exists(rootArtifactsDir))
    {
        Directory.CreateDirectory(rootArtifactsDir);
    }
    
    var now = DateTime.Now;
    
    while (Directory.GetDirectories(rootArtifactsDir).Any(d => d == now.ToString(nowFormat)))
    {
        now += TimeSpan.FromMilliseconds(1);
    }
    
    var artifactsDir = Path.Combine(rootArtifactsDir, now.ToString(nowFormat));
    
    Directory.CreateDirectory(artifactsDir);
    
    return artifactsDir;
}

private static string CreateContentDirectory(string artifactsDir)
{
    var contentDir = Path.Combine(artifactsDir, "content");
    Directory.CreateDirectory(contentDir);
    return contentDir;
}

private static string CreateToolsDirectory(string artifactsDir, string buildDir)
{
    const string install = "install.ps1";
    const string uninstall = "uninstall.ps1";

    var toolsDir = Path.Combine(artifactsDir, "tools");
    
    Directory.CreateDirectory(toolsDir);
    
    File.Copy(Path.Combine(buildDir, install), Path.Combine(toolsDir, install));
    File.Copy(Path.Combine(buildDir, uninstall), Path.Combine(toolsDir, uninstall));
    
    return toolsDir;
}

private static FileTemplate GetGeneratedFileTemplate(string directory)
{
    const string identifierStart = @"(\p{Lu}|\p{Ll}|\p{Lt}|\p{Lm}|\p{Lo}|\p{Nl})";
    const string identifierRest = @"(\p{Mn}|\p{Mc}|\p{Nd}|\p{Pc}|\p{Cf})";
    const string identifierPattern = identifierStart + "(?:" + identifierStart + "|" + identifierRest + ")*";

    var usingRegex = new Regex(string.Format(@"using\s+{0}(?:\.{0})*\s*;", identifierPattern));

    var usings = new HashSet<string>();
    var classDefinitions = new List<string>();
    
    foreach (var path in Directory.GetFiles(directory, "*.Generated.cs").OrderBy(x => Path.GetFileName(x)))
    {
        var contents = File.ReadAllText(path);
        
        foreach (Match match in usingRegex.Matches(contents))
        {
            usings.Add(match.Value);
        }
        
        var startIndex = contents.IndexOf('{') + 1;
        var endIndex = contents.LastIndexOf('}') - 2;
        
        classDefinitions.Add(contents.Substring(startIndex, endIndex - startIndex));
    }
    
    var generatedFileContents = string.Format(
@"//////////////////////////////////////////////////////////////////////////////////////
//                                                                                  //
// This file was generated by a tool. It would be a bad idea to make changes to it. //
//                                                                                  //
//////////////////////////////////////////////////////////////////////////////////////

{0}

namespace $rootnamespace$.Rock.StaticDependencyInjection
{{{1}
}}",
    string.Join("\r\n", usings.OrderBy(x => x, NamespaceComparer.Instance)),
    string.Join("\r\n", classDefinitions));
    
    return
        new FileTemplate
        {
            Name = "StaticDependencyInjection.Generated.cs.pp",
            Contents = generatedFileContents
        };
}

private static IEnumerable<FileTemplate> GetNonGeneratedFileTemplates(string directory)
{
    foreach (var path in Directory.GetFiles(directory, "*.cs").Where(path => !path.Contains(".Generated")).OrderBy(x => Path.GetFileName(x)))
    {
        var sb = new StringBuilder();
    
        var skip = false;
        
        foreach (var line in File.ReadAllLines(path))
        {
            if (skip)
            {
                if (line.TrimStart(' ', '\t').StartsWith("// Rock.StaticDependencyInjection: END EXAMPLE"))
                {
                    skip = false;
                }
                else
                {
                    continue;
                }
            }
            else
            {
                if (line.TrimStart(' ', '\t').StartsWith("// Rock.StaticDependencyInjection: BEGIN EXAMPLE"))
                {
                    skip = true;
                }
                else
                {
                    sb.AppendLine(line);
                }
            }
        }
        
        var contents =
            sb.ToString()
                .Replace(
                    "Rock.StaticDependencyInjection",
                    "$rootnamespace$.Rock.StaticDependencyInjection");
        
        yield return
            new FileTemplate
            {
                Name = Path.GetFileName(path) + ".pp",
                Contents = contents.ToString()
            };
    }
}

private static void WriteContentFiles(string contentDir, FileTemplate generatedFileTemplate, IEnumerable<FileTemplate> nonGeneratedFileTemplates)
{
    var staticDependencyInjection = Path.Combine(contentDir, "Rock.StaticDependencyInjection");

    Directory.CreateDirectory(staticDependencyInjection);

    File.WriteAllText(Path.Combine(staticDependencyInjection, generatedFileTemplate.Name), generatedFileTemplate.Contents);
    
    foreach (var template in nonGeneratedFileTemplates)
    {
        File.WriteAllText(Path.Combine(staticDependencyInjection, template.Name), template.Contents);
    }
}

private static string GetVersion(string srcDir)
{
    var assemblyInfoContents = File.ReadAllText(Path.Combine(srcDir, "Properties", "AssemblyInfo.cs"));
    var match = Regex.Match(assemblyInfoContents, @"\[assembly: AssemblyInformationalVersion\(""([^""]+)""\)]");
    return match.Groups[1].Value;
}

private static void WriteNuspecFile(string buildDir, string artifactsDir, FileTemplate generatedFileTemplate, IEnumerable<FileTemplate> nonGeneratedFileTemplates, string version)
{
    const string nuspecFile = "Rock.StaticDependencyInjection.nuspec";
    const string xsd = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd";
    
    var nuspecContents = File.ReadAllText(Path.Combine(buildDir, nuspecFile));
    
    var xml = XDocument.Parse(nuspecContents);
    
    xml.Root.Element(XName.Get("metadata", xsd)).Element(XName.Get("version", xsd)).Value = version;
    
    var filesElement = xml.Root.Element(XName.Get("files", xsd));
    
    var newElements =
        nonGeneratedFileTemplates.Select(template => template.GetXElement(xsd))
        .Concat(new[] { generatedFileTemplate.GetXElement(xsd) });
    
    filesElement.AddFirst(newElements);
    
    File.WriteAllText(Path.Combine(artifactsDir, nuspecFile), xml.ToString());
}

public static void DeleteDirectory(string targetDir)
{
    foreach (string file in Directory.GetFiles(targetDir))
    {
        File.SetAttributes(file, FileAttributes.Normal);
        File.Delete(file);
    }

    foreach (string dir in Directory.GetDirectories(targetDir))
    {
        DeleteDirectory(dir);
    }

    Directory.Delete(targetDir, false);
}

private class FileTemplate
{
    public string Name { get; set; }
    public string Contents { get; set; }
    
    public XElement GetXElement(string xsd)
    {
        var value = string.Format(@"content\Rock.StaticDependencyInjection\{0}", Name);

        return
            new XElement(XName.Get("file", xsd),
                new XAttribute("src", value),
                new XAttribute("target", value));
    }
}

private sealed class NamespaceComparer : IComparer<string>
{
    private static readonly NamespaceComparer _instance = new NamespaceComparer();

    private NamespaceComparer()
    {
    }
    
    public static IComparer<string> Instance
    {
        get { return _instance; }
    }

    public int Compare(string lhs, string rhs)
    {
        var lhsEnumerator = lhs.GetEnumerator();
        var rhsEnumerator = rhs.GetEnumerator();
        
        while (true)
        {
            var lhsHasNext = lhsEnumerator.MoveNext();
            var rhsHasNext = rhsEnumerator.MoveNext();
            
            if (lhsHasNext && rhsHasNext)
            {
                var lhsValue = lhsEnumerator.Current;
                var rhsValue = rhsEnumerator.Current;
                
                if (lhsValue == rhsValue)
                {
                    continue;
                }
                
                if (lhsValue == ';' && rhsValue == '.')
                {
                    return -1;
                }
                
                if (lhsValue == '.' && rhsValue == ';')
                {
                    return 1;
                }
                
                return lhsValue.CompareTo(rhsValue);
            }
            else if (lhsHasNext)
            {
                return 1;
            }
            else if (rhsHasNext)
            {
                return -1;
            }
            else
            {
                return 0;
            }
        }
    }
}