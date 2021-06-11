using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using UnityEditor;

public class SolutionFilePostprocessor : AssetPostprocessor
{
    public static class SolutionGuidGenerator
    {
        public static string GuidForProject(string projectName)
        {
            return ComputeGuidHashFor(projectName + "salt");
        }

        public static string GuidForSolution(string projectName, bool isSDK)
        {
            if (!isSDK) {
                return "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";
            }

            return "9A19103F-16F7-4668-BE54-9A1E7A4F7556";
        }

        private static string ComputeGuidHashFor(string input)
        {
            var hash = MD5.Create().ComputeHash(Encoding.Default.GetBytes(input));
            return HashAsGuid(HashToString(hash));
        }

        private static string HashAsGuid(string hash)
        {
            var guid = hash.Substring(0, 8) + "-" + hash.Substring(8, 4) + "-" + hash.Substring(12, 4) + "-" + hash.Substring(16, 4) + "-" + hash.Substring(20, 12);
            return guid.ToUpper();
        }

        private static string HashToString(byte[] bs)
        {
            var sb = new StringBuilder();
            foreach (byte b in bs)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    internal class Solution
    {
        public SolutionProjectEntry[] Projects { get; set; }
        public SolutionProperties[] Properties { get; set; }
    }

    internal class SolutionProperties
    {
        public string Name { get; set; }
        public IList<KeyValuePair<string, string>> Entries { get; set; }
        public string Type { get; set; }
    }

    internal class SolutionProjectEntry
    {
        public string ProjectFactoryGuid { get; set; }
        public string Name { get; set; }
        public string FileName { get; set; }
        public string ProjectGuid { get; set; }
        public string Metadata { get; set; }

        public bool IsSolutionFolderProjectFactory()
        {
            return ProjectFactoryGuid != null && ProjectFactoryGuid.Equals("2150E333-8FDC-42A3-9474-1A3956D46DE8", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class SolutionParser
    {
        // Compared to the bridge implementation, we are not returning "{" "}" from Guids
        private static readonly Regex ProjectDeclaration = new Regex(@"Project\(\""{(?<projectFactoryGuid>.*?)}\""\)\s+=\s+\""(?<name>.*?)\"",\s+\""(?<fileName>.*?)\"",\s+\""{(?<projectGuid>.*?)}\""(?<metadata>.*?)\bEndProject\b", RegexOptions.Singleline | RegexOptions.ExplicitCapture);
        private static readonly Regex PropertiesDeclaration = new Regex(@"GlobalSection\((?<name>([\w]+Properties|NestedProjects))\)\s+=\s+(?<type>(?:post|pre)Solution)(?<entries>.*?)EndGlobalSection", RegexOptions.Singleline | RegexOptions.ExplicitCapture);
        private static readonly Regex PropertiesEntryDeclaration = new Regex(@"^\s*(?<key>.*?)=(?<value>.*?)$", RegexOptions.Multiline | RegexOptions.ExplicitCapture);

        public static Solution ParseSolutionFile(string content)
        {
            return ParseSolutionContent(content);
        }

        public static Solution ParseSolutionContent(string content)
        {
            return new Solution {
                Projects = ParseSolutionProjects(content),
                Properties = ParseSolutionProperties(content)
            };
        }

        private static SolutionProjectEntry[] ParseSolutionProjects(string content)
        {
            var projects = new List<SolutionProjectEntry>();
            var mc = ProjectDeclaration.Matches(content);

            foreach (Match match in mc) {
                projects.Add(new SolutionProjectEntry {
                    ProjectFactoryGuid = match.Groups["projectFactoryGuid"].Value,
                    Name = match.Groups["name"].Value,
                    FileName = match.Groups["fileName"].Value,
                    ProjectGuid = match.Groups["projectGuid"].Value,
                    Metadata = match.Groups["metadata"].Value
                });
            }

            return projects.ToArray();
        }

        private static SolutionProperties[] ParseSolutionProperties(string content)
        {
            var properties = new List<SolutionProperties>();
            var mc = PropertiesDeclaration.Matches(content);

            foreach (Match match in mc) {
                var sp = new SolutionProperties {
                    Entries = new List<KeyValuePair<string, string>>(),
                    Name = match.Groups["name"].Value,
                    Type = match.Groups["type"].Value
                };

                var entries = match.Groups["entries"].Value;
                var mec = PropertiesEntryDeclaration.Matches(entries);
                foreach (Match entry in mec) {
                    var key = entry.Groups["key"].Value.Trim();
                    var value = entry.Groups["value"].Value.Trim();
                    sp.Entries.Add(new KeyValuePair<string, string>(key, value));
                }

                properties.Add(sp);
            }

            return properties.ToArray();
        }
    }

    private static string GetSolutionText()
    {
        return string.Join("\r\n",
        @"",
        @"Microsoft Visual Studio Solution File, Format Version {0}",
        @"# Visual Studio {1}",
        @"{2}",
        @"Global",
        @"    GlobalSection(SolutionConfigurationPlatforms) = preSolution",
        @"        Debug|Any CPU = Debug|Any CPU",
        @"        Release|Any CPU = Release|Any CPU",
        @"    EndGlobalSection",
        @"    GlobalSection(ProjectConfigurationPlatforms) = postSolution",
        @"{3}",
        @"    EndGlobalSection",
        @"{4}",
        @"EndGlobal",
        @"").Replace("    ", "\t");
    }

    const string k_WindowsNewline = "\r\n";
    static string m_SolutionProjectEntryTemplate = @"Project(""{{{0}}}"") = ""{1}"", ""{2}"", ""{{{3}}}""{4}EndProject";
    static string m_SolutionProjectConfigurationTemplate = string.Join("\r\n",
    @"        {{{0}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
    @"        {{{0}}}.Debug|Any CPU.Build.0 = Debug|Any CPU",
    @"        {{{0}}}.Release|Any CPU.ActiveCfg = Release|Any CPU",
    @"        {{{0}}}.Release|Any CPU.Build.0 = Release|Any CPU").Replace("    ", "\t");
    private static string GetPropertiesText(SolutionProperties[] array)
    {
        if (array == null || array.Length == 0) {
            // HideSolution by default
            array = new SolutionProperties[] {
                    new SolutionProperties() {
                        Name = "SolutionProperties",
                        Type = "preSolution",
                        Entries = new List<KeyValuePair<string,string>>() { new KeyValuePair<string, string> ("HideSolutionNode", "FALSE") }
                    }
                };
        }
        var result = new StringBuilder();

        for (var i = 0; i < array.Length; i++) {
            if (i > 0)
                result.Append(k_WindowsNewline);

            var properties = array[i];

            result.Append($"\tGlobalSection({properties.Name}) = {properties.Type}");
            result.Append(k_WindowsNewline);

            foreach (var entry in properties.Entries) {
                result.Append($"\t\t{entry.Key} = {entry.Value}");
                result.Append(k_WindowsNewline);
            }

            result.Append("\tEndGlobalSection");
        }

        return result.ToString();
    }

    private static string GetProjectEntriesText(IEnumerable<SolutionProjectEntry> entries)
    {
        var projectEntries = entries.Select(entry => string.Format(
            m_SolutionProjectEntryTemplate,
            entry.ProjectFactoryGuid, entry.Name, entry.FileName, entry.ProjectGuid, entry.Metadata
        ));

        return string.Join(k_WindowsNewline, projectEntries.ToArray());
    }

    private static string GetProjectActiveConfigurations(string projectGuid)
    {
        return string.Format(
            m_SolutionProjectConfigurationTemplate,
            projectGuid);
    }

    public static bool IsExternalFile(string root, string project)
    {
        if (string.IsNullOrEmpty(project))
            return false;

        var fullProjectPath = Path.GetFullPath(project);

        var slnRoot = Path.GetDirectoryName(root);
        var projectRoot = Path.GetDirectoryName(fullProjectPath);

        return projectRoot != slnRoot;
    }

    public static string GetRelativePath(string fromPath, string toPath)
    {
        if (string.IsNullOrEmpty(fromPath)) {
            throw new ArgumentNullException("fromPath");
        }

        if (string.IsNullOrEmpty(toPath)) {
            throw new ArgumentNullException("toPath");
        }

        Uri fromUri = new Uri(AppendDirectorySeparatorChar(fromPath));
        Uri toUri = new Uri(AppendDirectorySeparatorChar(toPath));

        if (fromUri.Scheme != toUri.Scheme) {
            return toPath;
        }

        Uri relativeUri = fromUri.MakeRelativeUri(toUri);
        string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

        if (string.Equals(toUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)) {
            relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        return relativePath;
    }

    private static string AppendDirectorySeparatorChar(string path)
    {
        if (!Path.HasExtension(path) &&
            !path.EndsWith(Path.DirectorySeparatorChar.ToString())) {
            return path + Path.DirectorySeparatorChar;
        }

        return path;
    }

    public static string OnGeneratedSlnSolution(string path, string content)
    {
        const string fileversion = "12.00";
        const string vsversion = "15";

        var solution = SolutionParser.ParseSolutionContent(content);

        var xx = Path.GetFullPath(solution.Projects[0].FileName);

        var externalProjects = solution.Projects.Where(x => IsExternalFile(path, x.FileName));

        var wtf = Directory.GetParent(path).Parent.FullName;
        var searchPath = Path.Combine(wtf, "Source");
        if (!Directory.Exists(searchPath))
            return content;

        var csProjs = Directory.GetFiles(searchPath, "*.csproj", SearchOption.AllDirectories);
        var newProjects = new List<SolutionProjectEntry>();

        if (csProjs.Any()) {
            // we have found some project files;
            foreach (var proj in csProjs) {
                var fileName = Path.GetFileNameWithoutExtension(proj);
                var alreadyExists = externalProjects.Any(y => y.Name == fileName);
                if (alreadyExists)
                    continue;

                var relativePath = GetRelativePath(path, proj);
                newProjects.Add(new SolutionProjectEntry() {
                    ProjectFactoryGuid = SolutionGuidGenerator.GuidForSolution(fileName, true),
                    Name = fileName,
                    FileName = relativePath,
                    ProjectGuid = SolutionGuidGenerator.GuidForProject(fileName),
                    Metadata = k_WindowsNewline
                });
            }
        }

        var currentProjects = solution.Projects.ToList();
        var currentProperties = solution.Properties.ToList();

        if (newProjects.Any()) {

            var solutionFolder = solution.Projects.Where(p => p.IsSolutionFolderProjectFactory()).FirstOrDefault();
            if (solutionFolder == null) {
                // we need to create a new external folder
                solutionFolder = new SolutionProjectEntry() {
                    ProjectFactoryGuid = "2150E333-8FDC-42A3-9474-1A3956D46DE8",
                    Name = "External",
                    FileName = "External",
                    ProjectGuid = "EC53F180-D7F9-46DF-B6A5-54511207D496",
                    Metadata = k_WindowsNewline
                };

                currentProjects.Add(solutionFolder);
            }

            var hasNestedProjectsProperty = solution.Properties.Where(pp => pp.Name == "NestedProjects").FirstOrDefault();
            if (hasNestedProjectsProperty == null) {
                hasNestedProjectsProperty = new SolutionProperties() {
                    Name = "NestedProjects",
                    Type = "preSolution",
                    Entries = new List<KeyValuePair<string, string>>()
                };
            }

            foreach (var item in newProjects) {
                hasNestedProjectsProperty.Entries.Add(new KeyValuePair<string, string>($"{{{item.ProjectGuid}}}", $"{{{solutionFolder.ProjectGuid}}}"));
            }

            currentProperties.Add(hasNestedProjectsProperty);
            currentProjects.AddRange(newProjects);
        }

        string propertiesText = GetPropertiesText(currentProperties.ToArray());
        string projectEntriesText = GetProjectEntriesText(currentProjects);

        var configurableProjects = currentProjects.Where(p => !p.IsSolutionFolderProjectFactory());
        string projectConfigurationsText = string.Join(k_WindowsNewline, configurableProjects.Select(p => GetProjectActiveConfigurations(p.ProjectGuid)).ToArray());

        var newSolution = string.Format(GetSolutionText(), fileversion, vsversion, projectEntriesText, projectConfigurationsText, propertiesText);
        return newSolution;
    }
}
