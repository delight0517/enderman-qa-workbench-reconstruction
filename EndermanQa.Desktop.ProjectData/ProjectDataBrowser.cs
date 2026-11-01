using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EndermanQa.Desktop.Persistence;

namespace EndermanQa.Desktop.ProjectData;

public sealed class ProjectDataBrowser : Window
{
	private readonly TreeView _folders = new TreeView();

	private readonly ListBox _files = new ListBox();

	private readonly TextBox _path = new TextBox
	{
		IsReadOnly = true
	};

	private readonly TextBlock _status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap
	};

	private string? _selected;

	private string? _root;

	public ProjectDataBrowser(QaProjectCatalog catalog)
	{
		base.Title = "프로젝트 데이터 · 폴더 탐색";
		base.Width = 900.0;
		base.Height = 600.0;
		DockPanel dockPanel = (DockPanel)(base.Content = new DockPanel
		{
			Margin = new Thickness(15.0)
		});
		StackPanel stackPanel = new StackPanel();
		DockPanel.SetDock(stackPanel, Dock.Top);
		dockPanel.Children.Add(stackPanel);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "프로젝트를 선택해 기록·실행 결과·증거 폴더를 확인하세요. 현재 작업 프로젝트는 바뀌지 않습니다.",
			TextWrapping = TextWrapping.Wrap
		});
		ComboBox projects = new ComboBox
		{
			ItemsSource = catalog.ListProjects(),
			DisplayMemberPath = "Name",
			Margin = new Thickness(0.0, 10.0, 0.0, 8.0)
		};
		stackPanel.Children.Add(projects);
		stackPanel.Children.Add(_path);
		WrapPanel buttons = new WrapPanel
		{
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		stackPanel.Children.Add(buttons);
		Button("선택 폴더 열기", delegate
		{
			if (ValidSelection())
			{
				Process.Start(new ProcessStartInfo("explorer.exe")
				{
					UseShellExecute = true,
					ArgumentList = { _selected }
				});
			}
		});
		Button("경로 복사", delegate
		{
			if (ValidSelection())
			{
				Clipboard.SetText(_selected);
			}
		});
		Button("새로고침", delegate
		{
			if (_root != null)
			{
				LoadRoot(_root);
			}
		});
		DockPanel.SetDock(_status, Dock.Bottom);
		dockPanel.Children.Add(_status);
		Grid grid = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition(),
				new ColumnDefinition()
			},
			Children = { (UIElement)_folders }
		};
		Grid.SetColumn(_files, 1);
		grid.Children.Add(_files);
		dockPanel.Children.Add(grid);
		projects.SelectionChanged += delegate
		{
			object selectedItem = projects.SelectedItem;
			QaProjectDefinition p2 = selectedItem as QaProjectDefinition;
			if ((object)p2 != null)
			{
				Safe(delegate
				{
					LoadRoot(p2.RootPath);
				});
			}
		};
		_folders.SelectedItemChanged += delegate
		{
			if (_folders.SelectedItem is TreeViewItem { Tag: var tag })
			{
				string path = tag as string;
				if (path != null)
				{
					Safe(delegate
					{
						Select(path);
					});
				}
			}
		};
		QaProjectDefinition active = catalog.GetActiveProject();
		projects.SelectedItem = projects.Items.Cast<QaProjectDefinition>().FirstOrDefault((QaProjectDefinition p) => p.ProjectId == active?.ProjectId) ?? projects.Items.Cast<QaProjectDefinition>().FirstOrDefault();
		void Button(string title, Action action)
		{
			Action action2 = action;
			Button button = new Button
			{
				Content = title,
				Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
				Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
			};
			button.Click += delegate
			{
				Safe(action2);
			};
			buttons.Children.Add(button);
		}
	}

	private void Safe(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is Win32Exception) ? 1 : 0) != 0)
		{
			_status.Text = "폴더 접근 실패: " + ex.Message;
		}
	}

	private bool ValidSelection()
	{
		if (_selected != null && _root != null && Directory.Exists(_selected))
		{
			if (!_selected.Equals(_root, StringComparison.OrdinalIgnoreCase))
			{
				string? selected = _selected;
				ReadOnlySpan<char> readOnlySpan = _root;
				char reference = Path.DirectorySeparatorChar;
				if (!selected.StartsWith(string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference)), StringComparison.OrdinalIgnoreCase))
				{
					goto IL_0071;
				}
			}
			return (File.GetAttributes(_selected) & FileAttributes.ReparsePoint) == 0;
		}
		goto IL_0071;
		IL_0071:
		return false;
	}

	private void LoadRoot(string path)
	{
		_root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
		_folders.Items.Clear();
		TreeViewItem treeViewItem = Node(_root);
		_folders.Items.Add(treeViewItem);
		treeViewItem.IsSelected = true;
		treeViewItem.IsExpanded = true;
	}

	private TreeViewItem Node(string path)
	{
		string path2 = path;
		TreeViewItem node = new TreeViewItem
		{
			Header = Path.GetFileName(path2),
			Tag = path2
		};
		node.Items.Add("불러오기…");
		node.Expanded += delegate(object _, RoutedEventArgs e)
		{
			if (e.OriginalSource == node && node.Items.Count == 1 && node.Items[0] is string)
			{
				Safe(delegate
				{
					node.Items.Clear();
					foreach (string item in (from p in Directory.EnumerateDirectories(path2)
						where (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0
						orderby p
						select p).Take(300))
					{
						node.Items.Add(Node(item));
					}
				});
			}
		};
		return node;
	}

	private void Select(string path)
	{
		_selected = path;
		_path.Text = path;
		_files.Items.Clear();
		foreach (string item in (from p in Directory.EnumerateFiles(path)
			orderby p
			select p).Take(301))
		{
			if (_files.Items.Count == 300)
			{
				_files.Items.Add("더 많은 파일은 ‘선택 폴더 열기’에서 확인하세요.");
				break;
			}
			_files.Items.Add(Path.GetFileName(item));
		}
		_status.Text = "폴더별 최대 300개 표시 · 파일은 자동 실행하지 않습니다. 실행 결과와 SOP는 data/suite-runs, 수동 기록과 증거는 data 아래에서 확인하세요.";
	}
}
