using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace EndermanQa.Desktop.Results;

public static class WorkbookReadService
{
	public const int MaxRows = 10000;

	public const int MaxColumns = 100;

	public static IReadOnlyList<string> ListSheets(string path)
	{
		using SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Open(path, isEditable: false);
		return (from sheet in spreadsheetDocument.WorkbookPart?.Workbook.Sheets?.Elements<Sheet>()
			select sheet.Name?.Value ?? "(이름 없는 시트)").ToArray() ?? Array.Empty<string>();
	}

	public static WorkbookSheetData ReadSheet(string path, string sheetName)
	{
		string sheetName2 = sheetName;
		using SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Open(path, isEditable: false);
		WorkbookPart workbookPart = spreadsheetDocument.WorkbookPart ?? throw new InvalidDataException("통합 문서 구조를 읽을 수 없습니다.");
		Sheet sheet = workbookPart.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault((Sheet item) => string.Equals(item.Name?.Value, sheetName2, StringComparison.Ordinal)) ?? throw new InvalidDataException("시트를 찾을 수 없습니다: " + sheetName2);
		WorksheetPart obj = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
		string[] shared = (from item in workbookPart.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>()
			select item.InnerText).ToArray() ?? Array.Empty<string>();
		Row[] array = obj.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>().Take(10002).ToArray() ?? Array.Empty<Row>();
		bool truncated = array.Length > 10001;
		SortedDictionary<int, string>[] parsed = array.Take(10001).Select(delegate(Row row)
		{
			SortedDictionary<int, string> sortedDictionary = new SortedDictionary<int, string>();
			int num = 0;
			foreach (Cell item in row.Elements<Cell>())
			{
				int num2 = ColumnIndex(item.CellReference?.Value);
				if (num2 < 0)
				{
					num2 = num;
				}
				if (num2 >= 100)
				{
					num = num2 + 1;
				}
				else
				{
					sortedDictionary[num2] = CellText(item, shared);
					num = num2 + 1;
				}
			}
			return sortedDictionary;
		}).ToArray();
		int width = Math.Min(100, parsed.SelectMany((SortedDictionary<int, string> row) => row.Keys).DefaultIfEmpty(-1).Max() + 1);
		if (width == 0)
		{
			return new WorkbookSheetData(sheet.Name?.Value ?? sheetName2, Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>(), truncated);
		}
		string[] array2 = (from i in Enumerable.Range(0, width)
			select ValueAt(parsed.FirstOrDefault() ?? new SortedDictionary<int, string>(), i)).ToArray();
		if (array2.All(string.IsNullOrWhiteSpace))
		{
			array2 = (from i in Enumerable.Range(0, width)
				select ColumnName(i)).ToArray();
		}
		else
		{
			for (int j = 0; j < array2.Length; j++)
			{
				if (string.IsNullOrWhiteSpace(array2[j]))
				{
					array2[j] = ColumnName(j);
				}
			}
		}
		IReadOnlyList<string>[] rows = parsed.Skip(1).Select((Func<SortedDictionary<int, string>, IReadOnlyList<string>>)((SortedDictionary<int, string> row) => (from i in Enumerable.Range(0, width)
			select ValueAt(row, i)).ToArray())).ToArray();
		return new WorkbookSheetData(sheet.Name?.Value ?? sheetName2, array2, rows, truncated);
	}

	private static string CellText(Cell cell, string[] shared)
	{
		CellValues? cellValues = cell.DataType?.Value;
		CellValues sharedString = CellValues.SharedString;
		if (cellValues.HasValue && cellValues.GetValueOrDefault() == sharedString && int.TryParse(cell.CellValue?.Text, out var result) && result >= 0 && result < shared.Length)
		{
			return shared[result];
		}
		if (cell.DataType?.Value == CellValues.InlineString)
		{
			return cell.InlineString?.InnerText ?? string.Empty;
		}
		if (!(cell.DataType?.Value == CellValues.Boolean))
		{
			return cell.CellValue?.Text ?? cell.InlineString?.InnerText ?? string.Empty;
		}
		if (!(cell.CellValue?.Text == "1"))
		{
			return "FALSE";
		}
		return "TRUE";
	}

	private static string ValueAt(SortedDictionary<int, string> row, int index)
	{
		if (!row.TryGetValue(index, out string value))
		{
			return string.Empty;
		}
		return value;
	}

	private static int ColumnIndex(string? address)
	{
		if (string.IsNullOrWhiteSpace(address))
		{
			return -1;
		}
		int num = 0;
		foreach (char c in address)
		{
			if ((c < 'A' || c > 'Z') && (c < 'a' || c > 'z'))
			{
				break;
			}
			num = num * 26 + (char.ToUpperInvariant(c) - 65 + 1);
		}
		return num - 1;
	}

	private static string ColumnName(int index)
	{
		string text = string.Empty;
		for (index++; index > 0; index = (index - 1) / 26)
		{
			char reference = (char)(65 + (index - 1) % 26);
			text = string.Concat(new ReadOnlySpan<char>(ref reference), text);
		}
		return text;
	}
}
