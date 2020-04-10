#module nuget:?package=Cake.LongPath.Module&version=0.7.0

#addin nuget:?package=Cake.FileHelpers&version=3.2.1
#addin nuget:?package=Cake.Powershell&version=0.4.8

#tool nuget:?package=MSTest.TestAdapter&version=2.1.0
#tool nuget:?package=vswhere&version=2.8.4

using System;
using System.Linq;
using System.Text.RegularExpressions;

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");

//////////////////////////////////////////////////////////////////////
// VERSIONS
//////////////////////////////////////////////////////////////////////

var gitVersioningVersion = "2.1.65";
var inheritDocVersion = "1.1.1.1";

//////////////////////////////////////////////////////////////////////
// VARIABLES
//////////////////////////////////////////////////////////////////////

var baseDir = MakeAbsolute(Directory("../")).ToString();
var buildDir = baseDir + "/build";
var Solution = baseDir + "/Windows Community Toolkit.sln";
var toolsDir = buildDir + "/tools";

var binDir = baseDir + "/bin";
var nupkgDir = binDir + "/nupkg";

var styler = toolsDir + "/XamlStyler.Console/tools/xstyler.exe";
var stylerFile = baseDir + "/settings.xamlstyler";

var versionClient = toolsDir + "/nerdbank.gitversioning/tools/Get-Version.ps1";
string Version = null;

var inheritDoc = toolsDir + "/InheritDoc/tools/InheritDoc.exe";
var inheritDocExclude = "Foo.*";

//////////////////////////////////////////////////////////////////////
// METHODS
//////////////////////////////////////////////////////////////////////

void VerifyHeaders(bool Replace)
{
    var header = FileReadText("header.txt") + "\r\n";
    bool hasMissing = false;

    Func<IFileSystemInfo, bool> exclude_objDir =
        fileSystemInfo => !fileSystemInfo.Path.Segments.Contains("obj");

    var files = GetFiles(baseDir + "/**/*.cs", new GlobberSettings { Predicate = exclude_objDir }).Where(file =>
    {
        var path = file.ToString();
        return !(path.EndsWith(".g.cs") || path.EndsWith(".i.cs") || System.IO.Path.GetFileName(path).Contains("TemporaryGeneratedFile"));
    });

    Information("\nChecking " + files.Count() + " file header(s)");
    foreach(var file in files)
    {
        var oldContent = FileReadText(file);
		if(oldContent.Contains("// <auto-generated>"))
		{
		   continue;
		}
        var rgx = new Regex("^(//.*\r?\n)*\r?\n");
        var newContent = header + rgx.Replace(oldContent, "");

        if(!newContent.Equals(oldContent, StringComparison.Ordinal))
        {
            if(Replace)
            {
                Information("\nUpdating " + file + " header...");
                FileWriteText(file, newContent);
            }
            else
            {
                Error("\nWrong/missing header on " + file);
                hasMissing = true;
            }
        }
    }

    if(!Replace && hasMissing)
    {
        throw new Exception("Please run UpdateHeaders.bat or '.\\build.ps1 -target=UpdateHeaders' and commit the changes.");
    }
}

void RetrieveVersion()
{
	Information("\nRetrieving version...");
    var results = StartPowershellFile(versionClient);
    Version = results[1].Properties["NuGetPackageVersion"].Value.ToString();
    Information("\nBuild Version: " + Version);
}

//////////////////////////////////////////////////////////////////////
// DEFAULT TASK
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Description("Clean the output folder")
    .Does(() =>
{
    if(DirectoryExists(binDir))
    {
        Information("\nCleaning Working Directory");
        CleanDirectory(binDir);
    }
    else
    {
        CreateDirectory(binDir);
    }
});

Task("Verify")
    .Description("Run pre-build verifications")
    .IsDependentOn("Clean")
    .Does(() =>
{
    VerifyHeaders(false);

    StartPowershellFile("./Find-WindowsSDKVersions.ps1");
});

Task("Version")
    .Description("Updates the version information in all Projects")
    .IsDependentOn("Verify")
    .Does(() =>
{
    Information("\nDownloading NerdBank GitVersioning...");
    var installSettings = new NuGetInstallSettings {
        ExcludeVersion  = true,
        Version = gitVersioningVersion,
        OutputDirectory = toolsDir
    };

    NuGetInstall(new []{"nerdbank.gitversioning"}, installSettings);

	RetrieveVersion();
});

Task("BuildProjects")
    .Description("Build all projects")
    .IsDependentOn("Version")
    .Does(() =>
{
    Information("\nBuilding Solution");
    var buildSettings = new MSBuildSettings
    {
        MaxCpuCount = 0
    }
    .SetConfiguration("Release")
    .WithTarget("Restore");
	
	// Workaround for https://github.com/cake-build/cake/issues/2128
	var vsInstallation = VSWhereLatest(new VSWhereLatestSettings { Requires = "Microsoft.Component.MSBuild", IncludePrerelease = true });

	if (vsInstallation != null)
	{
		buildSettings.ToolPath = vsInstallation.CombineWithFilePath(@"MSBuild\Current\Bin\MSBuild.exe");
		if (!FileExists(buildSettings.ToolPath))
			buildSettings.ToolPath = vsInstallation.CombineWithFilePath(@"MSBuild\15.0\Bin\MSBuild.exe");
	}

    MSBuild(Solution, buildSettings);

    EnsureDirectoryExists(nupkgDir);

	// Build once with normal dependency ordering
    buildSettings = new MSBuildSettings
    {
        MaxCpuCount = 0
    }
    .SetConfiguration("Release")
    .WithTarget("Build")
    .WithProperty("GenerateLibraryLayout", "true");

	MSBuild(Solution, buildSettings);
});

Task("InheritDoc")
    .Description("Updates <inheritdoc /> tags from base classes, interfaces, and similar methods")
    .IsDependentOn("BuildProjects")
    .Does(() =>
{
	Information("\nDownloading InheritDoc...");
	var installSettings = new NuGetInstallSettings {
		ExcludeVersion = true,
        Version = inheritDocVersion,
		OutputDirectory = toolsDir
	};

	NuGetInstall(new []{"InheritDoc"}, installSettings);
    
    var args = new ProcessArgumentBuilder()
                .AppendSwitchQuoted("-b", baseDir)
                .AppendSwitch("-o", "")
                .AppendSwitchQuoted("-x", inheritDocExclude);

    var result = StartProcess(inheritDoc, new ProcessSettings { Arguments = args });
    
    if (result != 0)
    {
        throw new InvalidOperationException("InheritDoc failed!");
    }

    Information("\nFinished generating documentation with InheritDoc");
});

Task("Build")
    .Description("Build all projects runs InheritDoc")
    .IsDependentOn("BuildProjects")
    .IsDependentOn("InheritDoc");

Task("Package")
	.Description("Pack the NuPkg")
	.Does(() =>
{
	// Invoke the pack target in the end
    var buildSettings = new MSBuildSettings {
        MaxCpuCount = 0
    }
    .SetConfiguration("Release")
    .WithTarget("Pack")
    .WithProperty("GenerateLibraryLayout", "true")
	.WithProperty("PackageOutputPath", nupkgDir);

    MSBuild(Solution, buildSettings);

	/*
	// Build and pack C++ packages
    buildSettings = new MSBuildSettings
    {
        MaxCpuCount = 0
    }
    .SetConfiguration("Native");

	// Ignored for now since WinUI3 alpha does not support ARM
    // buildSettings.SetPlatformTarget(PlatformTarget.ARM);
    // MSBuild(Solution, buildSettings);
	
	// Ignored for now since WinUI3 alpha does not support ARM64
	// buildSettings.SetPlatformTarget(PlatformTarget.ARM64);
    // MSBuild(Solution, buildSettings);

    buildSettings.SetPlatformTarget(PlatformTarget.x64);
    MSBuild(Solution, buildSettings);

    buildSettings.SetPlatformTarget(PlatformTarget.x86);
    MSBuild(Solution, buildSettings);

    RetrieveVersion();

	
	// Ignored for now
    var nuGetPackSettings = new NuGetPackSettings
	{
		OutputDirectory = nupkgDir,
        Version = Version
	};
	
    var nuspecs = GetFiles("./*.nuspec");
    foreach (var nuspec in nuspecs)
    {
        NuGetPack(nuspec, nuGetPackSettings);
    }
	*/
});

public string getMSTestAdapterPath(){
    var nugetPaths = GetDirectories("./tools/MSTest.TestAdapter*/build/_common");

    if(nugetPaths.Count == 0){
        throw new Exception(
            "Cannot locate the MSTest test adapter. " +
            "You might need to add '#tool nuget:?package=MSTest.TestAdapter&version=2.1.0' " + 
            "to the top of your build.cake file.");
    }

    return nugetPaths.Last().ToString();
}

Task("Test")
	.Description("Runs all Tests")
    .Does(() =>
{
	var vswhere = VSWhereLatest(new VSWhereLatestSettings
	{
		IncludePrerelease = false
	});

	var testSettings = new VSTestSettings
	{
	    ToolPath = vswhere + "/Common7/IDE/CommonExtensions/Microsoft/TestWindow/vstest.console.exe",
		TestAdapterPath = getMSTestAdapterPath(),
        ArgumentCustomization = arg => arg.Append("/logger:trx;LogFileName=VsTestResultsUwp.trx /framework:FrameworkUap10"),
	};

	VSTest(baseDir + "/**/Release/**/UnitTests.*.appxrecipe", testSettings);
}).DoesForEach(GetFiles(baseDir + "/**/UnitTests.*.NetCore.csproj"), (file) => 
{
    var testSettings = new DotNetCoreTestSettings
	{
		Configuration = "Release",
		NoBuild = true,
		Logger = "trx;LogFilePrefix=VsTestResults",
		Verbosity = DotNetCoreVerbosity.Normal,
		ArgumentCustomization = arg => arg.Append($"-s {baseDir}/.runsettings"),
	};
    DotNetCoreTest(file.FullPath, testSettings);
}).DeferOnError();



//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Build")
    .IsDependentOn("Test")
    .IsDependentOn("Package");

Task("UpdateHeaders")
    .Description("Updates the headers in *.cs files")
    .Does(() =>
{
    VerifyHeaders(true);
});

Task("StyleXaml")
    .Description("Ensures XAML Formatting is Clean")
    .Does(() =>
{
    Information("\nDownloading XamlStyler...");
    var installSettings = new NuGetInstallSettings {
        ExcludeVersion  = true,
        OutputDirectory = toolsDir
    };

    NuGetInstall(new []{"xamlstyler.console"}, installSettings);

    Func<IFileSystemInfo, bool> exclude_objDir =
        fileSystemInfo => !fileSystemInfo.Path.Segments.Contains("obj");

    var files = GetFiles(baseDir + "/**/*.xaml", new GlobberSettings { Predicate = exclude_objDir });
    Information("\nChecking " + files.Count() + " file(s) for XAML Structure");
    foreach(var file in files)
    {
        StartProcess(styler, "-f \"" + file + "\" -c \"" + stylerFile + "\"");
    }
});



//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
