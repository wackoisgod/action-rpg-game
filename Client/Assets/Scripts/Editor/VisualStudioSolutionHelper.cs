using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using UnityEditor;

using UnityEngine;

public class SolutionFilePostprocessor : AssetPostprocessor
{
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
    string m_SolutionProjectEntryTemplate = @"Project(""{{{0}}}"") = ""{1}"", ""{2}"", ""{{{3}}}""{4}EndProject";

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

    private string GetProjectEntriesText(IEnumerable<SolutionProjectEntry> entries)
    {
        var projectEntries = entries.Select(entry => string.Format(
            m_SolutionProjectEntryTemplate,
            entry.ProjectFactoryGuid, entry.Name, entry.FileName, entry.ProjectGuid, entry.Metadata
        ));

        return string.Join(k_WindowsNewline, projectEntries.ToArray());
    }

    public static string OnGeneratedSlnSolution(string path, string content)
    {
        var solution = SolutionParser.ParseSolutionContent(content);
       
        return content;
    }
}
