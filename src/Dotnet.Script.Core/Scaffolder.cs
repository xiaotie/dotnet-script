﻿using Dotnet.Script.Core.Templates;
using Dotnet.Script.DependencyModel.Environment;
using Dotnet.Script.DependencyModel.Logging;
using Dotnet.Script.DependencyModel.Process;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Dotnet.Script.Core
{
    public class Scaffolder
    {
        private readonly ScriptEnvironment _scriptEnvironment;
        private const string DefaultScriptFileName = "main.csx";
        private const string DefaultAspnetScriptFileName = "httpserver.csx";
        private const string DefaultImportAspnetScriptFileName = "aspnet.csx";
        private const string DefaultImportWinuiScriptFileName = "winui.csx";
        private const string DefaultImportDirName = "base";
        private readonly ScriptConsole _scriptConsole;
        private readonly CommandRunner _commandRunner;

        public Scaffolder(LogFactory logFactory) : this(logFactory, ScriptConsole.Default, ScriptEnvironment.Default)
        {
        }

        public Scaffolder(LogFactory logFactory, ScriptConsole scriptConsole, ScriptEnvironment scriptEnvironment)
        {
            _commandRunner = new CommandRunner(logFactory);
            _scriptConsole = scriptConsole;
            _scriptEnvironment = scriptEnvironment;
        }

        public void InitializerFolder(string fileName, string currentWorkingDirectory)
        {
            CreateLaunchConfiguration(currentWorkingDirectory);
            CreateOmniSharpConfigurationFile(currentWorkingDirectory);
            CreateScriptFile(fileName, currentWorkingDirectory);
            CreateDefaultAspnetScriptFile(currentWorkingDirectory);
            CreateImportScriptFile(currentWorkingDirectory, "aspnet.csx", () => CreateImportFile("Microsoft.AspNetCore.App"));
            CreateImportScriptFile(currentWorkingDirectory, "winui.csx", () => CreateImportFile("Microsoft.WindowsDesktop.App"));
        }

        public void CreateNewScriptFileFromTemplate(string fileName, string currentDirectory, string templateName)
        {
            _scriptConsole.WriteNormal($"Creating '{fileName}'");
            if (!Path.HasExtension(fileName))
            {
                fileName = Path.ChangeExtension(fileName, ".csx");
            }
            var pathToScriptFile = Path.Combine(currentDirectory, fileName);
            if (!File.Exists(pathToScriptFile))
            {
                var scriptFileTemplate = TemplateLoader.ReadTemplate(templateName);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // add a shebang to set dotnet-script as the interpreter for .csx files
                    // and make sure we are using environment newlines, because shebang won't work with windows cr\lf
                    scriptFileTemplate = $"#!/usr/bin/env dotnet-script" + Environment.NewLine + scriptFileTemplate.Replace("\r\n", Environment.NewLine);
                }

                File.WriteAllText(pathToScriptFile, scriptFileTemplate, new UTF8Encoding(false /* Linux shebang can't handle BOM */));

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // mark .csx file as executable, this activates the shebang to run dotnet-script as interpreter
                    _commandRunner.Execute($"/bin/chmod", $"+x \"{pathToScriptFile}\"");
                }
                _scriptConsole.WriteSuccess($"...'{pathToScriptFile}' [Created]");
            }
            else
            {
                _scriptConsole.WriteHighlighted($"...'{pathToScriptFile}' already exists [Skipping]");
            }
        }

        /// <summary>
        /// Platform specific registeration of .csx files to be executable
        /// </summary>
        public void RegisterFileHandler()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // register dotnet-script as the tool to process .csx files
                _commandRunner.Execute("reg", @"add HKCU\Software\classes\.csx /f /ve /t REG_SZ /d dotnetscript");
                _commandRunner.Execute("reg", $@"add HKCU\Software\Classes\dotnetscript\Shell\Open\Command /f /ve /t REG_EXPAND_SZ /d ""\""%ProgramFiles%\dotnet\dotnet.exe\"" script \""%1\"" -- %*""");
            }
            _scriptConsole.WriteSuccess($"...[Registered]");
        }

        private void CreateScriptFile(string fileName, string currentWorkingDirectory)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                CreateDefaultScriptFile(currentWorkingDirectory);
            }
            else
            {
                CreateNewScriptFileFromTemplate(fileName, currentWorkingDirectory, "helloworld.csx.template");
            }
        }

        private void CreateDefaultAspnetScriptFile(string currentWorkingDirectory)
        {
            _scriptConsole.Out.WriteLine($"Creating default aspnet script file '{DefaultAspnetScriptFileName}'");
            if (Directory.GetFiles(currentWorkingDirectory, DefaultAspnetScriptFileName).Any())
            {
                _scriptConsole.WriteHighlighted("...Folder already contains one or more aspnet script files [Skipping]");
            }
            else
            {
                CreateNewScriptFileFromTemplate(DefaultAspnetScriptFileName, currentWorkingDirectory, "aspnet.csx.template");
            }
        }

        private void CreateImportScriptFile(string currentWorkingDirectory, string filename, Func<String> generator)
        {
            _scriptConsole.Out.WriteLine($"Creating import script file '{filename}'");
            DirectoryInfo dirInfo = new DirectoryInfo(Path.Combine(currentWorkingDirectory, DefaultImportDirName));
            if (dirInfo.Exists == false) dirInfo.Create();

            if (Directory.GetFiles(dirInfo.FullName, filename).Any())
            {
                _scriptConsole.WriteHighlighted($"...Folder already contains {filename} [Skipping]");
            }
            else
            {
                String pathToScriptFile = Path.Combine(dirInfo.FullName, filename);
                String content = generator();
                File.WriteAllText(pathToScriptFile, content);
                _scriptConsole.WriteSuccess($"...'{pathToScriptFile}' [Created]");
            }
        }

        private static bool IsAssembly(string file)
        {
            // https://docs.microsoft.com/en-us/dotnet/standard/assembly/identify
            try
            {
                System.Reflection.AssemblyName.GetAssemblyName(file);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private String CreateImportFile(String sdkName)
        {
            StringBuilder sb = new StringBuilder();
            var netcoreAppRuntimeAssemblyLocation = Path.GetDirectoryName(typeof(object).Assembly.Location);
            netcoreAppRuntimeAssemblyLocation = netcoreAppRuntimeAssemblyLocation.Replace("Microsoft.NETCore.App", sdkName);
            var netcoreAppRuntimeAssemblies = Directory.GetFiles(netcoreAppRuntimeAssemblyLocation, "*.dll").Where(IsAssembly).ToArray();
            foreach (String item in netcoreAppRuntimeAssemblies)
            {
                String fullPath = item.Replace("\\", "/");
                sb.AppendLine($"#r \"{fullPath}\"");
            }
            return sb.ToString();
        }

        private void CreateDefaultScriptFile(string currentWorkingDirectory)
        {
            _scriptConsole.Out.WriteLine($"Creating default script file '{DefaultScriptFileName}'");
            if (Directory.GetFiles(currentWorkingDirectory, "*.csx").Any())
            {
                _scriptConsole.WriteHighlighted("...Folder already contains one or more script files [Skipping]");
            }
            else
            {
                CreateNewScriptFileFromTemplate(DefaultScriptFileName, currentWorkingDirectory, "helloworld.csx.template");
            }
        }

        private void CreateOmniSharpConfigurationFile(string currentWorkingDirectory)
        {
            _scriptConsole.WriteNormal("Creating OmniSharp configuration file");
            string pathToOmniSharpJson = Path.Combine(currentWorkingDirectory, "omnisharp.json");
            if (!File.Exists(pathToOmniSharpJson))
            {
                var omniSharpFileTemplate = TemplateLoader.ReadTemplate("omnisharp.json.template");
                JObject settings = JObject.Parse(omniSharpFileTemplate);
                settings["script"]["defaultTargetFramework"] = _scriptEnvironment.TargetFramework;
                File.WriteAllText(pathToOmniSharpJson, settings.ToString());
                _scriptConsole.WriteSuccess($"...'{pathToOmniSharpJson}' [Created]");
            }
            else
            {
                _scriptConsole.WriteHighlighted($"...'{pathToOmniSharpJson} already exists' [Skipping]");
            }
        }

        private void CreateLaunchConfiguration(string currentWorkingDirectory)
        {
            string vsCodeDirectory = Path.Combine(currentWorkingDirectory, ".vscode");
            if (!Directory.Exists(vsCodeDirectory))
            {
                Directory.CreateDirectory(vsCodeDirectory);
            }

            _scriptConsole.WriteNormal("Creating VS Code launch configuration file");
            string pathToLaunchFile = Path.Combine(vsCodeDirectory, "launch.json");
            string installLocation = _scriptEnvironment.InstallLocation;
            bool isInstalledAsGlobalTool = installLocation.Contains(".dotnet/tools", StringComparison.OrdinalIgnoreCase);
            string dotnetScriptPath = Path.Combine(installLocation, "nscript.dll").Replace(@"\", "/");
            string launchFileContent;
            if (!File.Exists(pathToLaunchFile))
            {
                if (isInstalledAsGlobalTool)
                {
                    launchFileContent = TemplateLoader.ReadTemplate("globaltool.launch.json.template");
                }
                else
                {
                    string launchFileTemplate = TemplateLoader.ReadTemplate("launch.json.template");
                    launchFileContent = launchFileTemplate.Replace("PATH_TO_DOTNET-SCRIPT", dotnetScriptPath);
                }

                File.WriteAllText(pathToLaunchFile, launchFileContent);
                _scriptConsole.WriteSuccess($"...'{pathToLaunchFile}' [Created]");
            }
            else
            {
                _scriptConsole.WriteHighlighted($"...'{pathToLaunchFile}' already exists' [Skipping]");
                launchFileContent = File.ReadAllText(pathToLaunchFile);
                if (isInstalledAsGlobalTool)
                {
                    var template = TemplateLoader.ReadTemplate("globaltool.launch.json.template");
                    if (template != launchFileContent)
                    {
                        File.WriteAllText(pathToLaunchFile, template);
                        _scriptConsole.WriteHighlighted("...Use global tool launch config [Updated]");
                    }
                }
                else
                {
                    string pattern = @"^(\s*"")(.*dotnet-script.dll)(""\s*,).*$";
                    if (Regex.IsMatch(launchFileContent, pattern, RegexOptions.Multiline))
                    {
                        var newLaunchFileContent = Regex.Replace(launchFileContent, pattern, $"$1{dotnetScriptPath}$3", RegexOptions.Multiline);
                        if (launchFileContent != newLaunchFileContent)
                        {
                            _scriptConsole.WriteHighlighted($"...Fixed path to nscript: '{dotnetScriptPath}' [Updated]");
                            File.WriteAllText(pathToLaunchFile, newLaunchFileContent);
                        }
                    }
                }

            }
        }
    }
}
