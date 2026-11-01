using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace EndermanQa.Desktop;

public class App : Application
{
	private Mutex? _singleInstanceMutex;

	private bool _contentLoaded;

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
	public static void Main()
	{
		App app = new App();
		app.InitializeComponent();
		MainWindow mainWindow = new MainWindow();
		app.Run(mainWindow);
	}
}
