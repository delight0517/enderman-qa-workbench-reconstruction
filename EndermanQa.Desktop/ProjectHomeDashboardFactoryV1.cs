using System;
using System.IO;
using System.Security.Cryptography;
using EndermanQa.Desktop.Modules;
using EndermanQa.Desktop.Results;
using System.Windows.Controls;

namespace EndermanQa.Desktop;

internal sealed class ProjectHomeDashboardFactoryV1 : IModuleFactoryV1
{
    private readonly StackPanel _projectPage;
    private readonly SuiteResultsView _results;
    private readonly Action _openResults;
    private readonly Action _exportPortfolio;
    private readonly Action _openFolders;
    private readonly Action _editProject;

    public string ModuleId => "qa.project-brief";
    public string Version => "1.0.0";
    public string SourcePath => Path.GetFullPath(typeof(ProjectHomeDashboardFactoryV1).Assembly.Location);
    public string SourceHash => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SourcePath))).ToLowerInvariant();

    public ProjectHomeDashboardFactoryV1(StackPanel projectPage, SuiteResultsView results, Action openResults, Action exportPortfolio, Action openFolders, Action editProject)
    {
        _projectPage = projectPage ?? throw new ArgumentNullException(nameof(projectPage));
        _results = results ?? throw new ArgumentNullException(nameof(results));
        _openResults = openResults ?? throw new ArgumentNullException(nameof(openResults));
        _exportPortfolio = exportPortfolio ?? throw new ArgumentNullException(nameof(exportPortfolio));
        _openFolders = openFolders ?? throw new ArgumentNullException(nameof(openFolders));
        _editProject = editProject ?? throw new ArgumentNullException(nameof(editProject));
    }

    public IModuleV1 Create(string verifiedSourcePath)
    {
        var verified = Path.GetFullPath(verifiedSourcePath);
        if (!verified.Equals(SourcePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Project brief module source is not the active candidate assembly.");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(verified))).ToLowerInvariant();
        if (!hash.Equals(SourceHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Project brief module assembly changed after review.");

        return new ProjectHomeDashboard(_projectPage, _results, _openResults, _exportPortfolio, _openFolders, _editProject, hash);
    }
}
