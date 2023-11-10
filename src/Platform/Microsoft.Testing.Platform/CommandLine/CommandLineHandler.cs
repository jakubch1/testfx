﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Testing.Platform.Extensions.CommandLine;
using Microsoft.Testing.Platform.Extensions.OutputDevice;
using Microsoft.Testing.Platform.Helpers;
using Microsoft.Testing.Platform.OutputDevice;
using Microsoft.Testing.Platform.Services;
using Microsoft.Testing.Platform.Tools;

namespace Microsoft.Testing.Platform.CommandLine;

internal sealed class CommandLineHandler : ICommandLineHandler, ICommandLineOptions, IOutputDeviceDataProducer
{
    private readonly TextOutputDeviceData _textOutputDeviceData = new(string.Empty);

    public string[] Arguments { get; }

    private readonly ICommandLineOptionsProvider[] _systemCommandLineOptionsProviders;
    private readonly IRuntime _runtime;
    private readonly IRuntimeFeature _runtimeFeature;
    private readonly IPlatformOutputDevice _platformOutputDevice;
#if !NETCOREAPP
    [SuppressMessage("CodeQuality", "IDE0052:RemoveVariable unread private members", Justification = "Used in netcoreapp")]
#endif
    private readonly IEnvironment _environment;

#if NETCOREAPP
    [SuppressMessage("CodeQuality", "IDE0052:RemoveVariable unread private members", Justification = "Used in netstandard")]
#endif
    private readonly IProcessHandler _process;

    private readonly CommandLineParseResult _parseResult;

    public ICommandLineOptionsProvider[] ExtensionsCommandLineOptionsProviders { get; }

    public string Uid => nameof(CommandLineHandler);

    public string Version => AppVersion.DefaultSemVer;

    public string DisplayName => string.Empty;

    public string Description => string.Empty;

    public CommandLineHandler(string[] args, CommandLineParseResult parseResult, ICommandLineOptionsProvider[] extensionsCommandLineOptionsProviders,
        ICommandLineOptionsProvider[] systemCommandLineOptionsProviders, IRuntime runtime, IRuntimeFeature runtimeFeature,
        IPlatformOutputDevice platformOutputDevice, IEnvironment environment, IProcessHandler process)
    {
        Arguments = args;
        _parseResult = parseResult;
        _systemCommandLineOptionsProviders = systemCommandLineOptionsProviders;
        ExtensionsCommandLineOptionsProviders = extensionsCommandLineOptionsProviders;
        _runtime = runtime;
        _runtimeFeature = runtimeFeature;
        _platformOutputDevice = platformOutputDevice;
        _environment = environment;
        _process = process;
    }

    public async Task<bool> ParseAndValidateAsync()
    {
        if (_parseResult.HasError)
        {
            StringBuilder stringBuilder = new();
            stringBuilder.AppendLine("Invalid command line arguments:");
            foreach (string error in _parseResult.Errors)
            {
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"\t* {error}");
            }

            await _platformOutputDevice.DisplayAsync(this, FormattedTextOutputDeviceDataHelper.CreateRedConsoleColorText(stringBuilder.ToString()));
            return false;
        }

        if (ExtensionOptionsContainReservedPrefix(out string? reservedPrefixError))
        {
            await _platformOutputDevice.DisplayAsync(this, FormattedTextOutputDeviceDataHelper.CreateRedConsoleColorText(reservedPrefixError));
            return false;
        }

        if (ExtensionOptionsContainReservedOptions(out string? reservedOptionError))
        {
            await _platformOutputDevice.DisplayAsync(this, FormattedTextOutputDeviceDataHelper.CreateRedConsoleColorText(reservedOptionError));
            return false;
        }

        if (ExtensionOptionAreDuplicated(out string? duplicationError))
        {
            await _platformOutputDevice.DisplayAsync(this, FormattedTextOutputDeviceDataHelper.CreateRedConsoleColorText(duplicationError));
            return false;
        }

        if (UnknownOptions(out string? unknownOptionsError))
        {
            await _platformOutputDevice.DisplayAsync(this, FormattedTextOutputDeviceDataHelper.CreateRedConsoleColorText(unknownOptionsError));
            await _platformOutputDevice.DisplayAsync(this, _textOutputDeviceData);
            await PrintHelpAsync();
            return false;
        }

        if (ExtensionArgumentArityAreInvalid(out string? arityErrors))
        {
            await _platformOutputDevice.DisplayAsync(this, FormattedTextOutputDeviceDataHelper.CreateRedConsoleColorText(arityErrors));
            return false;
        }

        if (InvalidOptionsArguments(out string? invalidOptionsArguments))
        {
            await _platformOutputDevice.DisplayAsync(this, FormattedTextOutputDeviceDataHelper.CreateRedConsoleColorText(invalidOptionsArguments));
            return false;
        }

        if (IsInvalidValidConfiguration(out string? configurationError))
        {
            await _platformOutputDevice.DisplayAsync(this, FormattedTextOutputDeviceDataHelper.CreateRedConsoleColorText(configurationError));
            return false;
        }

        return true;
    }

    private bool ExtensionOptionsContainReservedPrefix([NotNullWhen(true)] out string? error)
    {
        StringBuilder? stringBuilder = null;
        foreach (ICommandLineOptionsProvider commandLineOptionsProvider in ExtensionsCommandLineOptionsProviders)
        {
            foreach (CommandLineOption option in commandLineOptionsProvider.GetCommandLineOptions())
            {
                if (option.IsBuiltIn)
                {
                    continue;
                }

                string trimmedOption = option.Name.Trim(CommandLineParseResult.OptionPrefix);
                if (trimmedOption.StartsWith("internal", StringComparison.OrdinalIgnoreCase) || option.Name.StartsWith("-internal", StringComparison.OrdinalIgnoreCase))
                {
                    stringBuilder ??= new();
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Option --{trimmedOption} of {commandLineOptionsProvider.DisplayName} is using reserved prefix --internal");
                }
            }
        }

        error = stringBuilder?.ToString();
        return stringBuilder?.Length > 0;
    }

    private bool IsInvalidValidConfiguration([NotNullWhen(true)] out string? error)
    {
        StringBuilder? stringBuilder = null;
        foreach (ICommandLineOptionsProvider commandLineOptionsProvider in _systemCommandLineOptionsProviders.Union(ExtensionsCommandLineOptionsProviders))
        {
            if (!commandLineOptionsProvider.IsValidConfiguration(this, out string? providerError))
            {
                stringBuilder ??= new();
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Invalid configuration for '{commandLineOptionsProvider.DisplayName}' provider:\n{providerError}");
                stringBuilder.AppendLine();
            }
        }

        error = stringBuilder?.ToString();
        return stringBuilder?.Length > 0;
    }

    private bool InvalidOptionsArguments([NotNullWhen(true)] out string? error)
    {
        error = null;

        ArgumentGuard.IsNotNull(_parseResult);

        StringBuilder? stringBuilder = null;
        foreach (OptionRecord optionRecord in _parseResult.Options)
        {
            ICommandLineOptionsProvider extension = GetAllCommandLineOptionsProviderByOptionName(optionRecord.Option).Single();
            if (!extension.OptionArgumentsAreValid(extension.GetCommandLineOptions().Single(x => x.Name == optionRecord.Option), optionRecord.Arguments, out string? argumentsError))
            {
                stringBuilder ??= new();
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Invalid arguments for option '--{optionRecord.Option}': {argumentsError}");
            }
        }

        if (stringBuilder?.Length > 0)
        {
            error = stringBuilder.ToString();
            return true;
        }

        return false;
    }

    private bool UnknownOptions([NotNullWhen(true)] out string? error)
    {
        error = null;

        ArgumentGuard.IsNotNull(_parseResult);

        StringBuilder? stringBuilder = null;
        foreach (OptionRecord optionRecord in _parseResult.Options)
        {
            if (!GetAllCommandLineOptionsProviderByOptionName(optionRecord.Option).Any())
            {
                stringBuilder ??= new();
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Unknown option '{optionRecord.Option}'");
            }
        }

        if (stringBuilder?.Length > 0)
        {
            error = stringBuilder.ToString();
            return true;
        }

        return false;
    }

    private bool ExtensionArgumentArityAreInvalid([NotNullWhen(true)] out string? error)
    {
        error = null;

        ArgumentGuard.IsNotNull(_parseResult);

        StringBuilder? stringBuilder = null;
        foreach (IGrouping<string, OptionRecord> optionRecord in _parseResult.Options.GroupBy(x => x.Option))
        {
            // getting the arguments count for an option.
            int arity = 0;
            foreach (OptionRecord record in optionRecord)
            {
                arity += record.Arguments.Length;
            }

            string optionName = optionRecord.Key;
            ICommandLineOptionsProvider extension = GetAllCommandLineOptionsProviderByOptionName(optionName).Single();
            CommandLineOption option = extension.GetCommandLineOptions().Single(x => x.Name == optionName);

            if (arity > option.Arity.Max && option.Arity.Max == 0)
            {
                stringBuilder ??= new();
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Option '--{optionName}' expects 0 argument for extension '{extension.DisplayName}'.");
            }
            else if (arity < option.Arity.Min)
            {
                stringBuilder ??= new();
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Option '--{optionName}' expects at least {option.Arity.Min} argument(s) for extension '{extension.DisplayName}'.");
            }
            else if (arity > option.Arity.Max)
            {
                stringBuilder ??= new();
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Option '--{optionName}' expects at most {option.Arity.Max} argument(s) for extension '{extension.DisplayName}'.");
            }
        }

        if (stringBuilder?.Length > 0)
        {
            error = stringBuilder.ToString();
            return true;
        }

        return false;
    }

    private bool ExtensionOptionAreDuplicated([NotNullWhen(true)] out string? error)
    {
        error = null;
        IEnumerable<string> duplications = ExtensionsCommandLineOptionsProviders.SelectMany(x => x.GetCommandLineOptions())
            .Select(x => x.Name)
            .GroupBy(x => x)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key);

        StringBuilder? stringBuilder = null;
        foreach (string duplicatedOption in duplications)
        {
            IEnumerable<ICommandLineOptionsProvider> commandLineOptionProviders = GetExtensionCommandLineOptionsProviderByOptionName(duplicatedOption);
            stringBuilder ??= new();
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Option '--{duplicatedOption}' is used by more than one extension: {commandLineOptionProviders.Select(x => x.DisplayName).Aggregate((a, b) => $"{a}, {b}")}");
        }

        if (stringBuilder?.Length > 0)
        {
            stringBuilder.AppendLine("To fix the above optionProviders clash you can override the option name using the configuration file");
            error = stringBuilder.ToString();
            return true;
        }

        return false;
    }

    private bool ExtensionOptionsContainReservedOptions([NotNullWhen(true)] out string? error)
    {
        error = null;

        IEnumerable<string> allExtensionOptions = ExtensionsCommandLineOptionsProviders.SelectMany(x => x.GetCommandLineOptions()).Select(x => x.Name).Distinct();
        IEnumerable<string> allSystemOptions = _systemCommandLineOptionsProviders.SelectMany(x => x.GetCommandLineOptions()).Select(x => x.Name).Distinct();

        IEnumerable<string> invalidReservedOptions = allSystemOptions.Intersect(allExtensionOptions);
        StringBuilder? stringBuilder = null;
        if (invalidReservedOptions.Any())
        {
            stringBuilder = new();
            foreach (string reservedOption in invalidReservedOptions)
            {
                IEnumerable<ICommandLineOptionsProvider> commandLineOptionProviders = GetExtensionCommandLineOptionsProviderByOptionName(reservedOption);
                stringBuilder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"The option '--{reservedOption}' is reserved and cannot be used by extensions: {commandLineOptionProviders.Select(x => x.DisplayName).Aggregate((a, b) => $"{a}, {b}")}");
            }

            error = stringBuilder.ToString();
            return true;
        }

        return false;
    }

    private IEnumerable<ICommandLineOptionsProvider> GetExtensionCommandLineOptionsProviderByOptionName(string optionName)
    {
        foreach (ICommandLineOptionsProvider commandLineOptionsProvider in ExtensionsCommandLineOptionsProviders)
        {
            if (commandLineOptionsProvider.GetCommandLineOptions().Any(option => option.Name == optionName))
            {
                yield return commandLineOptionsProvider;
            }
        }
    }

    private IEnumerable<ICommandLineOptionsProvider> GetAllCommandLineOptionsProviderByOptionName(string optionName)
    {
        foreach (ICommandLineOptionsProvider commandLineOptionsProvider in _systemCommandLineOptionsProviders.Union(ExtensionsCommandLineOptionsProviders))
        {
            if (commandLineOptionsProvider.GetCommandLineOptions().Any(option => option.Name == optionName))
            {
                yield return commandLineOptionsProvider;
            }
        }
    }

    public bool IsHelpInvoked() => IsOptionSet(PlatformCommandLineProvider.HelpOptionKey);

    public bool IsInfoInvoked() => IsOptionSet(PlatformCommandLineProvider.InfoOptionKey);

#pragma warning disable IDE0060 // Remove unused parameter, temporary we don't use it.
    public async Task PrintHelpAsync(ITool[]? availableTools = null)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        string applicationName = GetApplicationName(_runtime);
        await PrintApplicationUsageAsync(applicationName);

        // Temporary disabled, we don't remove the code because could be useful in future.
        // PrintApplicationToolUsage(availableTools, applicationName);
        await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData(string.Empty));

        async Task<bool> PrintOptionsAsync(IEnumerable<ICommandLineOptionsProvider> optionProviders, int leftPaddingDepth, bool builtInOnly = false)
        {
            IEnumerable<CommandLineOption> options =
                optionProviders
               .SelectMany(provider => provider.GetCommandLineOptions())
               .Where(option => !option.IsHidden)
               .OrderBy(option => option.Name);

            options = builtInOnly ? options.Where(option => option.IsBuiltIn) : options.Where(option => !option.IsBuiltIn);

            if (!options.Any())
            {
                return false;
            }

            int maxOptionNameLength = options.Max(option => option.Name.Length);

            foreach (CommandLineOption? option in options)
            {
                await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"{new string(' ', leftPaddingDepth * 2)}--{option.Name}{new string(' ', maxOptionNameLength - option.Name.Length)} {option.Description}"));
            }

            return options.Any();
        }

        async Task PrintApplicationUsageAsync(string applicationName)
        {
            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"Usage {applicationName} [optionProviders] [extension-optionProviders]"));
            await _platformOutputDevice.DisplayAsync(this, _textOutputDeviceData);
            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData("Execute a .NET Test Application."));
            await _platformOutputDevice.DisplayAsync(this, _textOutputDeviceData);

            RoslynDebug.Assert(
                !_systemCommandLineOptionsProviders.OfType<IToolCommandLineOptionsProvider>().Any(),
                "System command line options should not have any tool option registered.");
            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData("Options:"));
            await PrintOptionsAsync(_systemCommandLineOptionsProviders.Union(ExtensionsCommandLineOptionsProviders), 1, builtInOnly: true);
            await _platformOutputDevice.DisplayAsync(this, _textOutputDeviceData);

            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData("Extension Options:"));
            if (!await PrintOptionsAsync(ExtensionsCommandLineOptionsProviders.Where(provider => provider is not IToolCommandLineOptionsProvider), 1))
            {
                await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData("  No extension registered."));
            }

            await _platformOutputDevice.DisplayAsync(this, _textOutputDeviceData);
        }

        // Temporary disabled, we don't remove the code because could be useful in future.
        // void PrintApplicationToolUsage(ITool[]? availableTools, string applicationName)
        // {
        //     _console.WriteLine($"Usage {applicationName} [tool-name] [tool-optionProviders]");
        //     _console.WriteLine();
        //     _console.WriteLine("Execute a .NET Test Application tool.");
        //     _console.WriteLine();
        //     _console.WriteLine("Tools:");
        //     var tools = availableTools
        //         ?.Where(tool => !tool.Hidden)
        //         .OrderBy(tool => tool.DisplayName)
        //         .ToList();
        //     if (tools is null || tools.Count == 0)
        //     {
        //         _console.WriteLine("No tools registered.");
        //         return;
        //     }
        //     int maxToolNameLength = tools.Max(tool => tool.Name.Length);
        //     foreach (ITool tool in tools)
        //     {
        //         _console.WriteLine($"  {tool.Name}{new string(' ', maxToolNameLength - tool.Name.Length)} ({tool.DisplayName}): {tool.Description}");
        //         PrintOptions(ExtensionsCommandLineOptionsProviders.Where(provider => provider is IToolCommandLineOptionsProvider), 2);
        //     }
        // }
    }

    public async Task PrintInfoAsync(ITool[]? availableTools = null)
    {
        await DisplayPlatformInfoAsync();
        await _platformOutputDevice.DisplayAsync(this, _textOutputDeviceData);
        await DisplayBuiltInExtensionsInfoAsync();
        await _platformOutputDevice.DisplayAsync(this, _textOutputDeviceData);

        List<IToolCommandLineOptionsProvider> toolExtensions = [];
        List<ICommandLineOptionsProvider> nonToolExtensions = [];
        foreach (ICommandLineOptionsProvider provider in ExtensionsCommandLineOptionsProviders)
        {
            if (provider is IToolCommandLineOptionsProvider toolProvider)
            {
                toolExtensions.Add(toolProvider);
            }
            else
            {
                nonToolExtensions.Add(provider);
            }
        }

        await DisplayRegisteredExtensionsInfoAsync(nonToolExtensions);
        await _platformOutputDevice.DisplayAsync(this, _textOutputDeviceData);
        await DisplayRegisteredToolsInfoAsync(availableTools, toolExtensions);
        await _platformOutputDevice.DisplayAsync(this, _textOutputDeviceData);

        return;

        async Task DisplayPlatformInfoAsync()
        {
            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData("Microsoft Testing Platform:"));

            // TODO: Replace Assembly with IAssembly
            var version = (AssemblyInformationalVersionAttribute?)Assembly.GetExecutingAssembly().GetCustomAttribute(typeof(AssemblyInformationalVersionAttribute));
            string versionInfo = version?.InformationalVersion ?? "Not Available";
            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"  Version: {versionInfo}"));

            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"  Dynamic Code Supported: {_runtimeFeature.IsDynamicCodeSupported}"));

            // TODO: Replace RuntimeInformation with IRuntimeInformation
#if NETCOREAPP
            string runtimeInformation = $"{RuntimeInformation.RuntimeIdentifier} - {RuntimeInformation.FrameworkDescription}";
#else
            string runtimeInformation = $"{RuntimeInformation.FrameworkDescription}";
#endif
            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"  Runtime information: {runtimeInformation}"));

#if !NETCOREAPP
#pragma warning disable IL3000 // Avoid accessing Assembly file path when publishing as a single file, this branch run only in .NET Framework
            string runtimeLocation = typeof(object).Assembly?.Location ?? "Not Found";
#pragma warning restore IL3000 // Avoid accessing Assembly file path when publishing as a single file
            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"  Runtime location: {runtimeLocation}"));
#endif

            string? moduleName = _runtime.GetCurrentModuleInfo().GetCurrentTestApplicationFullPath();
            moduleName = TAString.IsNullOrEmpty(moduleName)
#if NETCOREAPP
                ? _environment.ProcessPath
#else
                ? _process.GetCurrentProcess().MainModule.FileName
#endif
                : moduleName;
            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"  Test module: {moduleName}"));
        }

        async Task DisplayOptionsAsync(IEnumerable<CommandLineOption> options, int indentLevel)
        {
            string optionNameIndent = new(' ', indentLevel * 2);
            string optionInfoIndent = new(' ', (indentLevel + 1) * 2);
            foreach (CommandLineOption option in options.OrderBy(x => x.Name))
            {
                await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"{optionNameIndent}--{option.Name}"));
                if (option.Arity.Min == option.Arity.Max)
                {
                    await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"{optionInfoIndent}Arity: {option.Arity.Min}"));
                }
                else
                {
                    await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"{optionInfoIndent}Arity: {option.Arity.Min}..{option.Arity.Max}"));
                }

                await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"{optionInfoIndent}Hidden: {option.IsHidden}"));
                await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"{optionInfoIndent}Description: {option.Description}"));
            }
        }

        async Task DisplayProvidersAsync(IEnumerable<ICommandLineOptionsProvider> optionsProviders, int indentLevel)
        {
            string providerIdIndent = new(' ', indentLevel * 2);
            string providerInfoIndent = new(' ', (indentLevel + 1) * 2);
            foreach (IGrouping<string, ICommandLineOptionsProvider>? group in optionsProviders.GroupBy(x => x.Uid).OrderBy(x => x.Key))
            {
                bool isFirst = true;
                foreach (ICommandLineOptionsProvider provider in group)
                {
                    if (isFirst)
                    {
                        isFirst = false;
                        await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"{providerIdIndent}{provider.Uid}"));
                        await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"{providerInfoIndent}Name: {provider.DisplayName}"));
                        await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"{providerInfoIndent}Version: {provider.Version}"));
                        await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"{providerInfoIndent}Description: {provider.Description}"));
                        await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"{providerInfoIndent}Options:"));
                    }

                    await DisplayOptionsAsync(provider.GetCommandLineOptions(), indentLevel + 2);
                }
            }
        }

        async Task DisplayBuiltInExtensionsInfoAsync()
        {
            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData("Built-in command line providers:"));
            if (_systemCommandLineOptionsProviders.Length == 0)
            {
                await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData("  There are no built-in command line providers."));
            }
            else
            {
                await DisplayProvidersAsync(_systemCommandLineOptionsProviders, 1);
            }
        }

        async Task DisplayRegisteredExtensionsInfoAsync(List<ICommandLineOptionsProvider> nonToolExtensions)
        {
            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData("Registered command line providers:"));
            if (nonToolExtensions.Count == 0)
            {
                await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData("  There are no registered command line providers."));
            }
            else
            {
                await DisplayProvidersAsync(nonToolExtensions, 1);
            }
        }

        async Task DisplayRegisteredToolsInfoAsync(ITool[]? availableTools, List<IToolCommandLineOptionsProvider> toolExtensions)
        {
            await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData("Registered tools:"));
            if (availableTools is null || availableTools.Length == 0)
            {
                await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData("  There are no registered tools."));
            }
            else
            {
                var groupedToolExtensions = toolExtensions.GroupBy(x => x.ToolName).ToDictionary(x => x.Key, x => x.ToList());
                foreach (ITool tool in availableTools.OrderBy(x => x.Uid))
                {
                    await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"  {tool.Uid}"));
                    await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"    Command: {tool.Name}"));
                    await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"    Name: {tool.DisplayName}"));
                    await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"    Version: {tool.Version}"));
                    await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData($"    Description: {tool.Description}"));
                    await _platformOutputDevice.DisplayAsync(this, new TextOutputDeviceData("    Tool command line providers:"));
                    await DisplayProvidersAsync(groupedToolExtensions[tool.Name], 3);
                }
            }
        }
    }

    private static string GetApplicationName(IRuntime runtime)
    {
        ITestApplicationModuleInfo currentModuleInfo = runtime.GetCurrentModuleInfo();
        return currentModuleInfo.IsAppHostOrSingleFileOrNativeAot
            ? Path.GetFileName(currentModuleInfo.GetProcessPath())
            : currentModuleInfo.IsCurrentTestApplicationHostDotnetMuxer
                ? $"dotnet exec {Path.GetFileName(currentModuleInfo.GetCurrentTestApplicationFullPath())}"
                : "[Test application runner]";
    }

    public bool IsOptionSet(string optionName)
        => _parseResult?.IsOptionSet(optionName) == true;

    public bool TryGetOptionArgumentList(string optionName, [NotNullWhen(true)] out string[]? arguments)
    {
        arguments = null;
        return _parseResult is not null && _parseResult.TryGetOptionArgumentList(optionName, out arguments);
    }

    public Task<bool> IsEnabledAsync() => Task.FromResult(false);
}
