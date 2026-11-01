using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using EndermanQa.Desktop.Integrations;

namespace EndermanQa.Desktop.Tools;

public class ToolStudioView : UserControl, IComponentConnector
{
	private readonly Dictionary<AiConversationProfile, string> _targets = new Dictionary<AiConversationProfile, string>
	{
		[AiConversationProfile.ToolDesigner] = string.Empty,
		[AiConversationProfile.WorkbenchEvolution] = "Enderman QA Workbench 자체"
	};

	private AiConversationProfile _previousProfile;

	private bool _initialized;

	private bool _settingPrompt;

	private bool _settingProfile;

	internal ComboBox ProfilePicker;

	internal TextBox TargetBox;

	internal ListBox ConversationList;

	internal TextBox PromptBox;

	internal TextBlock StatusText;

	internal ListBox GeneratedFilesList;

	internal TextBox GeneratedFilePreview;

	private bool _contentLoaded;

	public AiConversationProfile SelectedProfile
	{
		get
		{
			if (!Enum.TryParse<AiConversationProfile>((ProfilePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString(), ignoreCase: false, out var result))
			{
				return AiConversationProfile.ToolDesigner;
			}
			return result;
		}
	}

	public string TargetName => TargetBox.Text.Trim();

	public string PromptText => PromptBox.Text;

	public string? SelectedGeneratedFile => GeneratedFilesList.SelectedItem as string;

	public event RoutedEventHandler? ProfileChanged;

	public event RoutedEventHandler? SendRequested;

	public event RoutedEventHandler? SaveRequested;

	public event RoutedEventHandler? CreatePackageRequested;

	public event RoutedEventHandler? OpenFilesRequested;

	public event RoutedEventHandler? DraftChanged;

	public event RoutedEventHandler? GeneratedFileSelected;

	public event RoutedEventHandler? OnlyChatRequested;

	public event RoutedEventHandler? OnlyChatConnectRequested;

	public ToolStudioView()
	{
		InitializeComponent();
		_previousProfile = SelectedProfile;
		_initialized = true;
	}

	public void SetPrompt(string text)
	{
		_settingPrompt = true;
		try
		{
			PromptBox.Text = text;
		}
		finally
		{
			_settingPrompt = false;
		}
	}

	public void ClearPrompt()
	{
		SetPrompt(string.Empty);
	}

	public void SetSelectedProfile(AiConversationProfile profile)
	{
		_settingProfile = true;
		try
		{
			ComboBoxItem comboBoxItem = ProfilePicker.Items.Cast<ComboBoxItem>().FirstOrDefault((ComboBoxItem x) => x.Tag?.ToString() == profile.ToString());
			if (comboBoxItem != null)
			{
				ProfilePicker.SelectedItem = comboBoxItem;
			}
			_previousProfile = profile;
			TargetBox.Text = (_targets.TryGetValue(profile, out string value) ? value : string.Empty);
		}
		finally
		{
			_settingProfile = false;
		}
	}

	public void FocusPrompt()
	{
		PromptBox.Focus();
	}

	public void SetStatus(string text)
	{
		StatusText.Text = text;
	}

	public void SetGeneratedFiles(IEnumerable<string> files)
	{
		GeneratedFilesList.ItemsSource = files.ToArray();
	}

	public void SelectGeneratedFile(string label)
	{
		GeneratedFilesList.SelectedItem = label;
	}

	public void SetGeneratedPreview(string text)
	{
		GeneratedFilePreview.Text = text;
	}

	public void SetConversation(IEnumerable<string> messages)
	{
		ConversationList.ItemsSource = messages.ToArray();
		if (ConversationList.Items.Count > 0)
		{
			ListBox conversationList = ConversationList;
			ItemCollection items = ConversationList.Items;
			conversationList.ScrollIntoView(items[items.Count - 1]);
		}
	}

	private void ProfilePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_initialized && !_settingProfile)
		{
			AiConversationProfile selectedProfile = SelectedProfile;
			if (selectedProfile != _previousProfile)
			{
				_targets[_previousProfile] = TargetBox.Text;
				_previousProfile = selectedProfile;
				TargetBox.Text = (_targets.TryGetValue(selectedProfile, out string value) ? value : string.Empty);
				this.ProfileChanged?.Invoke(this, e);
			}
		}
	}

	private void PromptBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_initialized && !_settingPrompt)
		{
			this.DraftChanged?.Invoke(this, e);
		}
	}

	private void Send_Click(object sender, RoutedEventArgs e)
	{
		this.SendRequested?.Invoke(this, e);
	}

	private void OnlyChat_Click(object sender, RoutedEventArgs e)
	{
		this.OnlyChatRequested?.Invoke(this, e);
	}

	private void OnlyChatConnect_Click(object sender, RoutedEventArgs e)
	{
		this.OnlyChatConnectRequested?.Invoke(this, e);
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		this.SaveRequested?.Invoke(this, e);
	}

	private void CreatePackage_Click(object sender, RoutedEventArgs e)
	{
		this.CreatePackageRequested?.Invoke(this, e);
	}

	private void OpenFiles_Click(object sender, RoutedEventArgs e)
	{
		this.OpenFilesRequested?.Invoke(this, e);
	}

	private void GeneratedFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		this.GeneratedFileSelected?.Invoke(this, e);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			EndermanQa.Desktop.XamlLoader.BamlHelper.LoadBaml(this, "tools/toolstudioview.baml");
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			ProfilePicker = (ComboBox)target;
			ProfilePicker.SelectionChanged += ProfilePicker_SelectionChanged;
			break;
		case 2:
			TargetBox = (TextBox)target;
			break;
		case 3:
			ConversationList = (ListBox)target;
			break;
		case 4:
			PromptBox = (TextBox)target;
			PromptBox.TextChanged += PromptBox_TextChanged;
			break;
		case 5:
			StatusText = (TextBlock)target;
			break;
		case 6:
			((Button)target).Click += Send_Click;
			break;
		case 7:
			((Button)target).Click += OnlyChatConnect_Click;
			break;
		case 8:
			((Button)target).Click += OnlyChat_Click;
			break;
		case 9:
			((Button)target).Click += Save_Click;
			break;
		case 10:
			((Button)target).Click += CreatePackage_Click;
			break;
		case 11:
			((Button)target).Click += OpenFiles_Click;
			break;
		case 12:
			GeneratedFilesList = (ListBox)target;
			GeneratedFilesList.SelectionChanged += GeneratedFilesList_SelectionChanged;
			break;
		case 13:
			GeneratedFilePreview = (TextBox)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
