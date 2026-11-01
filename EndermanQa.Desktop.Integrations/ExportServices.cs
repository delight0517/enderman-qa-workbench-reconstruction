using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace EndermanQa.Desktop.Integrations;

public static class ExportServices
{
	public static void ExportJiraCsv(string path, IEnumerable<QaExportRow> rows)
	{
		string[] source = new string[10] { "Issue Type", "Summary", "Description", "Priority", "Labels", "Environment", "QA Case ID", "QA Verdict", "QA Run ID", "QA Evidence" };
		List<string> list = new List<string> { string.Join(",", source.Select(Csv)) };
		foreach (QaExportRow row in rows)
		{
			string text = $"Case: {row.CaseId}\nExpected: {row.Expected}\nActual: {row.Actual}\nVerdict: {row.Verdict}\nEvidence: {row.Evidence}\nRun: {row.RunId}";
			list.Add(string.Join(",", new string[10]
			{
				"Bug",
				row.Title,
				text,
				"Medium",
				"qa,enderman",
				row.Target + " " + row.Version,
				row.CaseId,
				row.Verdict,
				row.RunId,
				row.Evidence
			}.Select(Csv)));
		}
		File.WriteAllText(path, string.Join("\r\n", list), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
	}

	public static void ExportExcel(string path, IEnumerable<QaExportRow> source)
	{
		QaExportRow[] array = source.ToArray();
		using SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
		WorkbookPart workbookPart = spreadsheetDocument.AddWorkbookPart();
		workbookPart.Workbook = new Workbook();
		Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
		AddSheet(workbookPart, sheets, "Test Cases", new string[9] { "Case ID", "Title", "Target", "Version", "Expected", "Actual", "Verdict", "Evidence", "Run ID" }, array.Select((QaExportRow r) => new string[9] { r.CaseId, r.Title, r.Target, r.Version, r.Expected, r.Actual, r.Verdict, r.Evidence, r.RunId }));
		AddSheet(workbookPart, sheets, "Summary", new string[2] { "Metric", "Value" }, new string[4][]
		{
			new string[2]
			{
				"Total cases",
				array.Length.ToString(CultureInfo.InvariantCulture)
			},
			new string[2]
			{
				"Pass",
				array.Count((QaExportRow r) => r.Verdict == "Pass").ToString(CultureInfo.InvariantCulture)
			},
			new string[2]
			{
				"Fail",
				array.Count((QaExportRow r) => r.Verdict == "Fail").ToString(CultureInfo.InvariantCulture)
			},
			new string[2]
			{
				"Inconclusive",
				array.Count((QaExportRow r) => r.Verdict == "Inconclusive").ToString(CultureInfo.InvariantCulture)
			}
		});
		AddEvidenceIndex(workbookPart, sheets, array);
		workbookPart.Workbook.Save();
	}

	public static void ExportSuiteResultsExcel(string path, IEnumerable<SuiteQaExportRow> source, string? evidenceRoot = null, bool relativeEvidencePaths = false)
	{
		SuiteQaExportRow[] array = source.OrderBy<SuiteQaExportRow, string>((SuiteQaExportRow row) => row.CaseNumber, StringComparer.Ordinal).ToArray();
		using SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
		WorkbookPart workbookPart = spreadsheetDocument.AddWorkbookPart();
		workbookPart.Workbook = new Workbook();
		Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
		AddSheet(workbookPart, sheets, "Test Cases", new string[20]
		{
			"Case #", "Case ID", "Title", "Status", "Verdict", "Preconditions", "Expected actions", "Expected observation", "Pass criteria", "Actual observation",
			"Coverage", "Attempts", "Repeatability", "Limitation", "Next evidence / current checkpoint note", "Capture status", "Candidate", "Suite ID", "Journal paths", "Capture media paths"
		}, array.Select((SuiteQaExportRow row) => new string[20]
		{
			row.CaseNumber, row.CaseId, row.Title, row.Status, row.Verdict, row.Preconditions, row.ExpectedActions, row.ExpectedObservation, row.PassCriteria, row.Actual,
			row.Coverage, row.Attempts, row.Repeatability, row.Limitation, row.FollowUpGuidance, row.CaptureStatus, row.SourceCandidate, row.SuiteId, row.JournalPath, row.MediaPaths
		}));
		int num = array.Count((SuiteQaExportRow row) => IsExecutedStatus(row.Status));
		string ClassifyResult(SuiteQaExportRow row)
		{
			if (row.Verdict.Equals("Fail", StringComparison.OrdinalIgnoreCase) || row.Status.StartsWith("failure-", StringComparison.OrdinalIgnoreCase))
			{
				return "Fail";
			}
			if (row.Verdict.Equals("Inconclusive", StringComparison.OrdinalIgnoreCase)
				|| (row.Status.Equals("anomaly-unconfirmed", StringComparison.OrdinalIgnoreCase) && !row.Verdict.Equals("Pass", StringComparison.OrdinalIgnoreCase)))
			{
				return "Inconclusive";
			}
			if (row.Status.Equals("executed-partial", StringComparison.OrdinalIgnoreCase)
				|| (row.Status.Equals("anomaly-unconfirmed", StringComparison.OrdinalIgnoreCase) && row.Verdict.Equals("Pass", StringComparison.OrdinalIgnoreCase))
				|| row.Verdict.Equals("Partial", StringComparison.OrdinalIgnoreCase)
				|| (row.Verdict.Equals("Pass", StringComparison.OrdinalIgnoreCase) && row.Coverage.Equals("partial", StringComparison.OrdinalIgnoreCase)))
			{
				return "Partial verification";
			}
			if (row.Status.Equals("executed-pass", StringComparison.OrdinalIgnoreCase)
				&& row.Verdict.Equals("Pass", StringComparison.OrdinalIgnoreCase)
				&& !row.Coverage.Equals("partial", StringComparison.OrdinalIgnoreCase))
			{
				return "Full Pass";
			}
			if (!IsExecutedStatus(row.Status))
			{
				return "Not Run";
			}
			return "Other";
		}
		string[][] array2 = new string[10][]
		{
			new string[2] { "Metric", "Value" },
			new string[2]
			{
				"Total cases",
				array.Length.ToString(CultureInfo.InvariantCulture)
			},
			new string[2]
			{
				"Dispatched to Minecraft",
				num.ToString(CultureInfo.InvariantCulture)
			},
			new string[2]
			{
				"Full Pass",
				array.Count((SuiteQaExportRow row) => ClassifyResult(row) == "Full Pass").ToString(CultureInfo.InvariantCulture)
			},
			new string[2]
			{
				"Fail",
				array.Count((SuiteQaExportRow row) => ClassifyResult(row) == "Fail").ToString(CultureInfo.InvariantCulture)
			},
			new string[2]
			{
				"Inconclusive",
				array.Count((SuiteQaExportRow row) => ClassifyResult(row) == "Inconclusive").ToString(CultureInfo.InvariantCulture)
			},
			new string[2]
			{
				"Partial verification",
				array.Count((SuiteQaExportRow row) => ClassifyResult(row) == "Partial verification").ToString(CultureInfo.InvariantCulture)
			},
			new string[2]
			{
				"Not Run",
				array.Count((SuiteQaExportRow row) => ClassifyResult(row) == "Not Run").ToString(CultureInfo.InvariantCulture)
			},
			new string[2]
			{
				"Unconfirmed anomaly",
				array.Count((SuiteQaExportRow row) => row.Status.Equals("anomaly-unconfirmed", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture)
			},
			new string[2]
			{
				"Screenshot/video available",
				array.Count((SuiteQaExportRow row) => !string.IsNullOrWhiteSpace(row.MediaPaths)).ToString(CultureInfo.InvariantCulture)
			}
		};
		AddSheet(workbookPart, sheets, "Summary", array2[0], array2.Skip(1));
		AddSuiteEvidenceIndex(workbookPart, sheets, array, evidenceRoot, relativeEvidencePaths);
		workbookPart.Workbook.Save();
	}

	public static void ExportSuiteResultsCsv(string path, IEnumerable<SuiteQaExportRow> source)
	{
		SuiteQaExportRow[] rows = source.OrderBy(row => row.CaseNumber, StringComparer.Ordinal).ToArray();
		string[] headers = { "Case #", "Case ID", "Title", "Status", "Verdict", "Preconditions", "Expected actions", "Expected observation", "Pass criteria", "Actual observation", "Coverage", "Attempts", "Repeatability", "Limitation", "Follow-up guidance", "Capture status", "Evidence journal (relative)", "Evidence media (relative)" };
		List<string> lines = new() { string.Join(",", headers.Select(Csv)) };
		foreach (SuiteQaExportRow row in rows)
		{
			lines.Add(string.Join(",", new[] { row.CaseNumber, row.CaseId, row.Title, row.Status, row.Verdict, row.Preconditions, row.ExpectedActions, row.ExpectedObservation, row.PassCriteria, row.Actual, row.Coverage, row.Attempts, row.Repeatability, row.Limitation, row.FollowUpGuidance, row.CaptureStatus, row.JournalPath, row.MediaPaths }.Select(Csv)));
		}
		File.WriteAllText(path, string.Join("\r\n", lines), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
	}

	private static bool IsExecutedStatus(string status)
	{
		if (!status.StartsWith("executed-", StringComparison.OrdinalIgnoreCase) && !status.StartsWith("failure-", StringComparison.OrdinalIgnoreCase))
		{
			return status.Equals("anomaly-unconfirmed", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static void AddSuiteEvidenceIndex(WorkbookPart wb, Sheets sheets, SuiteQaExportRow[] rows, string? evidenceRoot, bool relativeEvidencePaths)
	{
		WorksheetPart worksheetPart = wb.AddNewPart<WorksheetPart>();
		SheetData sheetData = new SheetData();
		sheetData.Append(Row(new _003C_003Ez__ReadOnlyArray<string>(new string[6] { "Case #", "Case ID", "Title", "Evidence type", "File", "Availability" }), header: true));
		Hyperlinks hyperlinks = new Hyperlinks();
		uint num = 1u;
		foreach (SuiteQaExportRow item in rows.Where((SuiteQaExportRow row) => !string.IsNullOrWhiteSpace(row.JournalPath) || !string.IsNullOrWhiteSpace(row.MediaPaths)))
		{
			foreach (var item2 in (from path in item.JournalPath.Split(new char[3] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				select (Path: path, Type: "Execution journal / file")).Concat(from path in item.MediaPaths.Split(new char[3] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				select ((string Path, string Type))(Path: path, Type: null)))
			{
				num++;
				string fullPath = relativeEvidencePaths
					? Path.GetFullPath(Path.Combine(evidenceRoot ?? throw new ArgumentNullException(nameof(evidenceRoot)), item2.Path.Replace('/', Path.DirectorySeparatorChar)))
					: Path.GetFullPath(item2.Path);
				if (relativeEvidencePaths)
				{
					string normalizedEvidenceRoot = Path.GetFullPath(evidenceRoot!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
					if (!fullPath.StartsWith(normalizedEvidenceRoot, StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException("Relative portfolio evidence path escaped its bundle folder.");
				}
				string text = Path.GetExtension(fullPath).ToLowerInvariant();
				string text2 = item2.Type;
				if (text2 == null)
				{
					if (text != null)
					{
						int length = text.Length;
						if (length != 4)
						{
							if (length == 5)
							{
								char c = text[4];
								if (c != 'g')
								{
									if (c != 'm')
									{
										if (c == 'p' && text == ".webp")
										{
											goto IL_0278;
										}
									}
									else if (text == ".webm")
									{
										goto IL_0281;
									}
								}
								else if (text == ".jpeg")
								{
									goto IL_0278;
								}
							}
						}
						else
						{
							switch (text[2])
							{
							case 'n':
								break;
							case 'p':
								goto IL_01fa;
							case 'i':
								goto IL_0218;
							case 'k':
								goto IL_0228;
							case 'o':
								goto IL_0238;
							default:
								goto IL_028a;
							}
							if (text == ".png")
							{
								goto IL_0278;
							}
						}
					}
					goto IL_028a;
				}
				goto IL_0295;
				IL_0281:
				string text3 = "Video";
				goto IL_0291;
				IL_0218:
				if (text == ".gif")
				{
					goto IL_0278;
				}
				goto IL_028a;
				IL_0238:
				if (text == ".mov")
				{
					goto IL_0281;
				}
				goto IL_028a;
				IL_0228:
				if (text == ".mkv")
				{
					goto IL_0281;
				}
				goto IL_028a;
				IL_01fa:
				if (text == ".jpg")
				{
					goto IL_0278;
				}
				if (text == ".mp4")
				{
					goto IL_0281;
				}
				goto IL_028a;
				IL_0295:
				string text4 = text2;
				bool flag = File.Exists(fullPath);
				sheetData.Append(Row(new _003C_003Ez__ReadOnlyArray<string>(new string[6]
				{
					item.CaseNumber,
					item.CaseId,
					item.Title,
					text4,
						relativeEvidencePaths ? item2.Path.Replace('\\', '/') : fullPath,
					flag ? (relativeEvidencePaths ? "Included in portfolio bundle" : "Available · click file link") : "Missing"
				}), header: false));
				if (flag && !relativeEvidencePaths)
				{
					HyperlinkRelationship hyperlinkRelationship = worksheetPart.AddHyperlinkRelationship(new Uri(fullPath), isExternal: true);
					hyperlinks.Append(new Hyperlink
					{
						Reference = $"E{num}",
						Id = hyperlinkRelationship.Id
					});
				}
				continue;
				IL_0291:
				text2 = text3;
				goto IL_0295;
				IL_028a:
				text3 = "Capture / media file";
				goto IL_0291;
				IL_0278:
				text3 = "Image";
				goto IL_0291;
			}
		}
		if (num == 1)
		{
			sheetData.Append(Row(new _003C_003Ez__ReadOnlyArray<string>(new string[6] { "", "", "", "", "", "No execution journals are attached yet." }), header: false));
		}
		worksheetPart.Worksheet = new Worksheet(sheetData);
		if (hyperlinks.ChildElements.Count > 0)
		{
			worksheetPart.Worksheet.Append(hyperlinks);
		}
		worksheetPart.Worksheet.Save();
		sheets.Append(new Sheet
		{
			Id = wb.GetIdOfPart(worksheetPart),
			SheetId = (uint)(sheets.Count() + 1),
			Name = "Evidence Index"
		});
	}

	private static void AddSheet(WorkbookPart wb, Sheets sheets, string name, string[] headers, IEnumerable<string[]> rows)
	{
		WorksheetPart worksheetPart = wb.AddNewPart<WorksheetPart>();
		SheetData sheetData = new SheetData();
		sheetData.Append(Row(headers, header: true));
		foreach (string[] row in rows)
		{
			sheetData.Append(Row(row, header: false));
		}
		worksheetPart.Worksheet = new Worksheet(sheetData);
		worksheetPart.Worksheet.Save();
		sheets.Append(new Sheet
		{
			Id = wb.GetIdOfPart(worksheetPart),
			SheetId = (uint)(sheets.Count() + 1),
			Name = name
		});
	}

	private static Row Row(IEnumerable<string> values, bool header)
	{
		return new Row(values.Select((string v) => new Cell
		{
			DataType = CellValues.InlineString,
			InlineString = new InlineString(new Text(v ?? string.Empty)
			{
				Space = SpaceProcessingModeValues.Preserve
			})
		}));
	}

	private static void AddEvidenceIndex(WorkbookPart wb, Sheets sheets, QaExportRow[] rows)
	{
		WorksheetPart worksheetPart = wb.AddNewPart<WorksheetPart>();
		SheetData sheetData = new SheetData();
		sheetData.Append(Row(new _003C_003Ez__ReadOnlyArray<string>(new string[6] { "Case ID", "Title", "Verdict", "Media type", "File", "Availability" }), header: true));
		Hyperlinks hyperlinks = new Hyperlinks();
		uint num = 1u;
		foreach (QaExportRow qaExportRow in rows)
		{
			string[] array = qaExportRow.Evidence.Split(new char[3] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (array.Length == 0)
			{
				continue;
			}
			string[] array2 = array;
			int num2 = 0;
			while (num2 < array2.Length)
			{
				string path = array2[num2];
				num++;
				string fullPath = Path.GetFullPath(path);
				bool flag = File.Exists(fullPath);
				string text = Path.GetExtension(fullPath).ToLowerInvariant();
				if (text != null)
				{
					int length = text.Length;
					if (length != 4)
					{
						if (length == 5)
						{
							char c = text[4];
							if (c != 'g')
							{
								if (c != 'm')
								{
									if (c == 'p' && text == ".webp")
									{
										goto IL_01e2;
									}
								}
								else if (text == ".webm")
								{
									goto IL_01eb;
								}
							}
							else if (text == ".jpeg")
							{
								goto IL_01e2;
							}
						}
					}
					else
					{
						switch (text[2])
						{
						case 'n':
							break;
						case 'p':
							goto IL_0164;
						case 'i':
							goto IL_0182;
						case 'k':
							goto IL_0192;
						case 'o':
							goto IL_01a2;
						default:
							goto IL_01f4;
						}
						if (text == ".png")
						{
							goto IL_01e2;
						}
					}
				}
				goto IL_01f4;
				IL_01fb:
				string text2;
				string text3 = text2;
				sheetData.Append(Row(new _003C_003Ez__ReadOnlyArray<string>(new string[6]
				{
					qaExportRow.CaseId,
					qaExportRow.Title,
					qaExportRow.Verdict,
					text3,
					fullPath,
					flag ? "Available · click file link" : "Missing"
				}), header: false));
				if (flag)
				{
					HyperlinkRelationship hyperlinkRelationship = worksheetPart.AddHyperlinkRelationship(new Uri(fullPath), isExternal: true);
					hyperlinks.Append(new Hyperlink
					{
						Reference = $"E{num}",
						Id = hyperlinkRelationship.Id
					});
				}
				num2++;
				continue;
				IL_0192:
				if (text == ".mkv")
				{
					goto IL_01eb;
				}
				goto IL_01f4;
				IL_01a2:
				if (text == ".mov")
				{
					goto IL_01eb;
				}
				goto IL_01f4;
				IL_0182:
				if (text == ".gif")
				{
					goto IL_01e2;
				}
				goto IL_01f4;
				IL_0164:
				if (text == ".jpg")
				{
					goto IL_01e2;
				}
				if (text == ".mp4")
				{
					goto IL_01eb;
				}
				goto IL_01f4;
				IL_01eb:
				text2 = "Video";
				goto IL_01fb;
				IL_01e2:
				text2 = "Image";
				goto IL_01fb;
				IL_01f4:
				text2 = "Log / file";
				goto IL_01fb;
			}
		}
		if (num == 1)
		{
			sheetData.Append(Row(new _003C_003Ez__ReadOnlyArray<string>(new string[6] { "", "", "", "", "", "No screenshot/video was attached to the exported QA records." }), header: false));
		}
		worksheetPart.Worksheet = new Worksheet(sheetData);
		if (hyperlinks.ChildElements.Count > 0)
		{
			worksheetPart.Worksheet.Append(hyperlinks);
		}
		worksheetPart.Worksheet.Save();
		sheets.Append(new Sheet
		{
			Id = wb.GetIdOfPart(worksheetPart),
			SheetId = (uint)(sheets.Count() + 1),
			Name = "Evidence Index"
		});
	}

	private static string Csv(string? value)
	{
		string text = (value ?? string.Empty).Replace("\"", "\"\"").Replace("\r\n", "\n").Replace("\r", "\n");
		return "\"" + text + "\"";
	}
}
