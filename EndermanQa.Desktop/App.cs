using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;

namespace EndermanQa.Desktop;

public class App : Application
{
	private Mutex? _singleInstanceMutex;

	private bool _contentLoaded;
	private static string _startupStage = "process-entry";
	private static readonly object _diagnosticLock = new object();
	private static string? _diagnosticPath;

	protected override void OnStartup(StartupEventArgs e)
	{
		// This recovered candidate must never claim the installed app's single-instance identity.
		_singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\EndermanQaWorkbench-Candidate-v27", out var createdNew);
		if (!createdNew)
		{
			_singleInstanceMutex.Dispose();
			_singleInstanceMutex = null;
			Shutdown();
		}
		else
		{
			base.OnStartup(e);
		}
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_singleInstanceMutex?.ReleaseMutex();
		_singleInstanceMutex?.Dispose();
		base.OnExit(e);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
		}
	}

	[STAThread]
	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	public static void Main(string[] args)
	{
		ConfigureDiagnostics();
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			WriteDiagnostic("appdomain-unhandled", args.ExceptionObject as Exception, args.IsTerminating);
		};

		try
		{
			WriteDiagnostic("create-application");
			App app = new App();
			app.InitializeComponent();
			app.DispatcherUnhandledException += (_, args) =>
			{
				WriteDiagnostic("dispatcher-unhandled", args.Exception);
				args.Handled = true;
				app.Shutdown(-1);
			};

			_startupStage = "construct-main-window";
			WriteDiagnostic(_startupStage);
			MainWindow mainWindow = new MainWindow();
			app.MainWindow = mainWindow;
			mainWindow.ContentRendered += (_, _) => WriteDiagnostic("main-window-content-rendered");
			if (Array.Exists(args, argument => string.Equals(argument, "--construct-only", StringComparison.Ordinal)))
			{
				WriteDiagnostic("main-window-constructed-only");
				Environment.ExitCode = 0;
				return;
			}

			_startupStage = "run-application";
			WriteDiagnostic(_startupStage);
			int exitCode = app.Run(mainWindow);
			WriteDiagnostic("application-exited", exitCode: exitCode);
			Environment.ExitCode = exitCode;
		}
		catch (Exception exception)
		{
			WriteDiagnostic(_startupStage, exception);
			Environment.ExitCode = 1;
		}
	}

	private static void ConfigureDiagnostics()
	{
		try
		{
			string? configuredRoot = Environment.GetEnvironmentVariable("ENDERMAN_QA_PREVIEW_DATA_ROOT");
			string root = string.IsNullOrWhiteSpace(configuredRoot)
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndermanQaWorkbench-Candidate-v27")
				: Path.GetFullPath(configuredRoot);
			string diagnosticsDirectory = Path.Combine(root, "startup-diagnostics");
			Directory.CreateDirectory(diagnosticsDirectory);
			_diagnosticPath = Path.Combine(diagnosticsDirectory, $"startup-{Environment.ProcessId}.jsonl");
			WriteDiagnostic("diagnostics-ready");
		}
		catch (Exception exception)
		{
			Debug.WriteLine("Could not configure reconstructed preview diagnostics: " + exception);
		}
	}

	private static void WriteDiagnostic(string stage, Exception? exception = null, bool? isTerminating = null, int? exitCode = null)
	{
		_startupStage = stage;
		try
		{
			if (string.IsNullOrWhiteSpace(_diagnosticPath))
				return;

			var record = new
			{
				timestampUtc = DateTimeOffset.UtcNow,
				processId = Environment.ProcessId,
				stage,
				exceptionType = exception?.GetType().FullName,
				exception = exception?.ToString(),
				isTerminating,
				exitCode
			};
			lock (_diagnosticLock)
				File.AppendAllText(_diagnosticPath, JsonSerializer.Serialize(record) + Environment.NewLine);
		}
		catch (Exception diagnosticException)
		{
			Debug.WriteLine("Could not write reconstructed preview diagnostics: " + diagnosticException);
		}
	}
}
