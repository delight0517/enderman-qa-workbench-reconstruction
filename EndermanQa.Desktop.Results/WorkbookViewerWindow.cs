using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

namespace EndermanQa.Desktop.Results;

public class WorkbookViewerWindow : Window, IComponentConnector
{
	private string? _path;

	private readonly string? _initialDirectory;

	private bool _updatingPicker;

	internal TextBlock SourceText;

	internal ComboBox SheetPicker;

	internal TextBlock SheetInfo;

	internal DataGrid CellsGrid;

	internal TextBlock NoticeText;

	private bool _contentLoaded;

	public WorkbookViewerWindow(string? initialPath = null, string? initialDirectory = null)
	{
		InitializeComponent();
		_initialDirectory = ResolveInitialDirectory(initialDirectory);
		if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
		{
			LoadWorkbook(initialPath);
		}
	}

	public void OpenWorkbookFile(string path)
	{
		LoadWorkbook(path);
	}

	private static string? ResolveInitialDirectory(string? root)
	{
		if (!Directory.Exists(root))
		{
			return null;
		}
		return new string[3]
		{
			Path.Combine(root, "exports"),
			Path.Combine(root, "reports"),
			root
		}.FirstOrDefault((string path) => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.xlsx", SearchOption.TopDirectoryOnly).Any()) ?? root;
	}

	private void OpenFile_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx",
			CheckFileExists = true,
			Multiselect = false,
			InitialDirectory = _initialDirectory
		};
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			LoadWorkbook(openFileDialog.FileName);
		}
	}

	private void LoadWorkbook(string path)
	{
		try
		{
			_path = Path.GetFullPath(path);
			IReadOnlyList<string> readOnlyList = WorkbookReadService.ListSheets(_path);
			if (readOnlyList.Count == 0)
			{
				throw new InvalidDataException("통합 문서에 시트가 없습니다.");
			}
			SourceText.Text = _path;
			_updatingPicker = true;
			SheetPicker.ItemsSource = readOnlyList;
			SheetPicker.SelectedIndex = 0;
			_updatingPicker = false;
			LoadSelectedSheet();
		}
		catch (Exception ex)
		{
			_updatingPicker = false;
			NoticeText.Text = "파일을 읽지 못했습니다: " + ex.Message;
		}
	}

	private void SheetPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_updatingPicker)
		{
			LoadSelectedSheet();
		}
	}

	private void LoadSelectedSheet()
	{
		if (_path == null || !(SheetPicker.SelectedItem is string sheetName))
		{
			return;
		}
		try
		{
			WorkbookSheetData workbookSheetData = WorkbookReadService.ReadSheet(_path, sheetName);
			DataTable dataTable = new DataTable(workbookSheetData.Name);
			for (int i = 0; i < workbookSheetData.Columns.Count; i++)
			{
				dataTable.Columns.Add($"{i + 1:00} · {workbookSheetData.Columns[i]}");
			}
			foreach (IReadOnlyList<string> row in workbookSheetData.Rows)
			{
				dataTable.Rows.Add(row.Cast<object>().ToArray());
			}
			CellsGrid.ItemsSource = dataTable.DefaultView;
			SheetInfo.Text = $"{workbookSheetData.Rows.Count:N0}개 데이터 행 · {workbookSheetData.Columns.Count}개 열";
			NoticeText.Text = (workbookSheetData.Truncated ? $"읽기 전용 미리보기: 안전 한도 {10000:N0}행 × {100}열까지 표시합니다. 원본 파일은 변경하지 않았습니다." : "읽기 전용 미리보기 · 원본 Excel 파일과 QA 프로젝트 데이터는 변경하지 않았습니다. 셀은 원본 값(수식은 저장된 계산값)으로 표시됩니다.");
		}
		catch (Exception ex)
		{
			NoticeText.Text = "시트를 읽지 못했습니다: " + ex.Message;
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			EndermanQa.Desktop.XamlLoader.BamlHelper.LoadBaml(this, "results/workbookviewerwindow.baml");
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
			((Button)target).Click += OpenFile_Click;
			break;
		case 2:
			SourceText = (TextBlock)target;
			break;
		case 3:
			SheetPicker = (ComboBox)target;
			SheetPicker.SelectionChanged += SheetPicker_SelectionChanged;
			break;
		case 4:
			SheetInfo = (TextBlock)target;
			break;
		case 5:
			CellsGrid = (DataGrid)target;
			break;
		case 6:
			NoticeText = (TextBlock)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
