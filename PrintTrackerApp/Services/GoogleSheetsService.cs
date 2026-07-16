using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Drive.v3;
using SheetColor = Google.Apis.Sheets.v4.Data.Color;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PrintTrackerApp.Services
{
    public class GoogleSheetsService
    {
        private static readonly string[] Scopes = { SheetsService.Scope.Spreadsheets, DriveService.Scope.DriveReadonly };
        private readonly string _applicationName = "Print Tracker App";
        private readonly SheetsService _service;
        private readonly DriveService _driveService;
        private readonly string _spreadsheetId;

        public GoogleSheetsService(string spreadsheetId, string credentialsPath)
        {
            _spreadsheetId = spreadsheetId;

            GoogleCredential credential;
            using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);
            }

            _service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = _applicationName,
            });

            _driveService = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = _applicationName,
            });
        }

        public async Task<DateTime?> GetSpreadsheetModifiedTimeAsync()
        {
            try
            {
                var request = _driveService.Files.Get(_spreadsheetId);
                request.Fields = "modifiedTime";
                var file = await request.ExecuteAsync();
                return file.ModifiedTimeDateTimeOffset?.DateTime;
            }
            catch
            {
                return null;
            }
        }

        public async Task<int> EnsureSheetExistsAsync(string sheetName)
        {
            var spreadsheet = await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
            foreach (var sheet in spreadsheet.Sheets)
            {
                if (string.Equals(sheet.Properties.Title, sheetName, StringComparison.OrdinalIgnoreCase))
                {
                    return sheet.Properties.SheetId.Value;
                }
            }

            var addSheetRequest = new Request
            {
                AddSheet = new AddSheetRequest
                {
                    Properties = new SheetProperties
                    {
                        Title = sheetName
                    }
                }
            };

            var batchUpdate = new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request> { addSheetRequest }
            };

            var response = await _service.Spreadsheets.BatchUpdate(batchUpdate, _spreadsheetId).ExecuteAsync();
            return response.Replies[0].AddSheet.Properties.SheetId.Value;
        }

        public async Task ClearSheetAsync(string sheetName, string startCell = "A1")
        {
            await EnsureSheetExistsAsync(sheetName);
            var requestBody = new ClearValuesRequest();
            string normalized = NormalizeStartCell(startCell);
            string colLetter = System.Text.RegularExpressions.Regex.Match(normalized, @"[A-Z]+").Value;
            int startRow = ParseStartRow(normalized);
            string range = (startRow > 1 || colLetter != "A") ? $"{sheetName}!{colLetter}{startRow}:Z" : $"{sheetName}";
            var deleteRequest = _service.Spreadsheets.Values.Clear(requestBody, _spreadsheetId, range);
            await deleteRequest.ExecuteAsync();
        }

        public async Task WriteDataAsync(string sheetName, IList<IList<object>> data)
        {
            var valueRange = new ValueRange { Values = data };
            var updateRequest = _service.Spreadsheets.Values.Update(valueRange, _spreadsheetId, $"{sheetName}!A1");
            updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            
            await updateRequest.ExecuteAsync();
        }

        public async Task WriteAndFormatDashboardDataAsync(string sheetName, IList<IList<object>> data, IList<IList<string>> notes = null, string startCell = "A1")
        {
            if (data == null || data.Count == 0) return;

            int sheetId = await EnsureSheetExistsAsync(sheetName);

            string normalizedStart = NormalizeStartCell(startCell);
            int startRow = ParseStartRow(normalizedStart);
            var colMatch = System.Text.RegularExpressions.Regex.Match(normalizedStart, @"[A-Z]+");
            int colOffset = colMatch.Success ? ColumnLetterToColumnIndex(colMatch.Value) : 0;
            int rowOffset = startRow - 1;

            // 1. Write text values
            var valueRange = new ValueRange { Values = data };
            var updateRequest = _service.Spreadsheets.Values.Update(valueRange, _spreadsheetId, $"{sheetName}!{normalizedStart}");
            updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await updateRequest.ExecuteAsync();

            // 2. Build formatting requests
            var requests = new List<Request>();

            // Clear old formatting and notes
            requests.Add(new Request
            {
                RepeatCell = new RepeatCellRequest
                {
                    Range = new GridRange { SheetId = sheetId, StartRowIndex = rowOffset, StartColumnIndex = colOffset },
                    Cell = new CellData { UserEnteredFormat = new CellFormat(), Note = "" },
                    Fields = "userEnteredFormat,note"
                }
            });

            var thinBorder = new Border
            {
                Style = "SOLID",
                Color = new SheetColor { Red = 0.8f, Green = 0.8f, Blue = 0.8f }
            };
            var borders = new Borders
            {
                Top = thinBorder,
                Bottom = thinBorder,
                Left = thinBorder,
                Right = thinBorder
            };

            int tables = 4;
            int colsPerTable = (sheetName == "PT") ? 4 : 3;
            int totalCols = (colsPerTable + 1) * tables - 1; // 19 for PT, 15 for FT/KH

            // Format Header Row (Row 0)
            for (int t = 0; t < tables; t++)
            {
                int startCol = t * (colsPerTable + 1);
                int endCol = startCol + colsPerTable;

                requests.Add(new Request
                {
                    RepeatCell = new RepeatCellRequest
                    {
                        Range = new GridRange { 
                            SheetId = sheetId, 
                            StartRowIndex = rowOffset, 
                            EndRowIndex = rowOffset + 1, 
                            StartColumnIndex = colOffset + startCol, 
                            EndColumnIndex = colOffset + endCol 
                        },
                        Cell = new CellData
                        {
                            UserEnteredFormat = new CellFormat
                            {
                                BackgroundColor = new SheetColor { Red = 0.169f, Green = 0.341f, Blue = 0.604f }, // Excel Blue
                                TextFormat = new TextFormat { Bold = true, ForegroundColor = new SheetColor { Red = 1f, Green = 1f, Blue = 1f }, FontSize = 10 },
                                HorizontalAlignment = "CENTER",
                                VerticalAlignment = "MIDDLE",
                                Borders = borders
                            }
                        },
                        Fields = "userEnteredFormat(backgroundColor,textFormat,horizontalAlignment,verticalAlignment,borders)"
                    }
                });
            }

            // Format Data Rows (Row 1 to data.Count)
            if (data.Count > 1)
            {
                for (int t = 0; t < tables; t++)
                {
                    int startCol = t * (colsPerTable + 1);
                    int gradeCol = startCol + colsPerTable - 1;

                    // Format non-Grade columns (Teacher)
                    requests.Add(new Request
                    {
                        RepeatCell = new RepeatCellRequest
                        {
                            Range = new GridRange { 
                                SheetId = sheetId, 
                                StartRowIndex = rowOffset + 1, 
                                EndRowIndex = rowOffset + data.Count, 
                                StartColumnIndex = colOffset + startCol, 
                                EndColumnIndex = colOffset + startCol + 1 
                            },
                            Cell = new CellData
                            {
                                UserEnteredFormat = new CellFormat
                                {
                                    TextFormat = new TextFormat { FontSize = 10 },
                                    HorizontalAlignment = "LEFT",
                                    VerticalAlignment = "MIDDLE",
                                    Borders = borders
                                }
                            },
                            Fields = "userEnteredFormat(textFormat,horizontalAlignment,verticalAlignment,borders)"
                        }
                    });

                    if (colsPerTable > 2) // Middle columns (Level, Session)
                    {
                        requests.Add(new Request
                        {
                            RepeatCell = new RepeatCellRequest
                            {
                                Range = new GridRange { 
                                    SheetId = sheetId, 
                                    StartRowIndex = rowOffset + 1, 
                                    EndRowIndex = rowOffset + data.Count, 
                                    StartColumnIndex = colOffset + startCol + 1, 
                                    EndColumnIndex = colOffset + startCol + colsPerTable - 1 
                                },
                                Cell = new CellData
                                {
                                    UserEnteredFormat = new CellFormat
                                    {
                                        TextFormat = new TextFormat { FontSize = 10 },
                                        HorizontalAlignment = "CENTER",
                                        VerticalAlignment = "MIDDLE",
                                        Borders = borders
                                    }
                                },
                                Fields = "userEnteredFormat(textFormat,horizontalAlignment,verticalAlignment,borders)"
                            }
                        });
                    }

                    // Default formatting for Grade column
                    requests.Add(new Request
                    {
                        RepeatCell = new RepeatCellRequest
                        {
                            Range = new GridRange { 
                                SheetId = sheetId, 
                                StartRowIndex = rowOffset + 1, 
                                EndRowIndex = rowOffset + data.Count, 
                                StartColumnIndex = colOffset + gradeCol, 
                                EndColumnIndex = colOffset + gradeCol + 1 
                            },
                            Cell = new CellData
                            {
                                UserEnteredFormat = new CellFormat
                                {
                                    TextFormat = new TextFormat { FontSize = 10 },
                                    HorizontalAlignment = "CENTER",
                                    VerticalAlignment = "MIDDLE",
                                    Borders = borders
                                }
                            },
                            Fields = "userEnteredFormat(textFormat,horizontalAlignment,verticalAlignment,borders)"
                        }
                    });
                }

                // Apply specific colors to Grade cells
                for (int r = 1; r < data.Count; r++)
                {
                    for (int t = 0; t < tables; t++)
                    {
                        int startCol = t * (colsPerTable + 1);
                        int gradeCol = startCol + colsPerTable - 1;

                        if (gradeCol < data[r].Count)
                        {
                            string gradeVal = data[r][gradeCol]?.ToString() ?? "";
                            var bgColor = GetGradeBackgroundColor(gradeVal);
                            if (bgColor != null)
                            {
                                requests.Add(new Request
                                {
                                    RepeatCell = new RepeatCellRequest
                                    {
                                        Range = new GridRange { 
                                            SheetId = sheetId, 
                                            StartRowIndex = rowOffset + r, 
                                            EndRowIndex = rowOffset + r + 1, 
                                            StartColumnIndex = colOffset + gradeCol, 
                                            EndColumnIndex = colOffset + gradeCol + 1 
                                        },
                                        Cell = new CellData
                                        {
                                            UserEnteredFormat = new CellFormat
                                            {
                                                BackgroundColor = bgColor,
                                                TextFormat = new TextFormat { Bold = true, ForegroundColor = new SheetColor { Red = 1f, Green = 1f, Blue = 1f }, FontSize = 10 },
                                                HorizontalAlignment = "CENTER",
                                                VerticalAlignment = "MIDDLE",
                                                Borders = borders
                                            }
                                        },
                                        Fields = "userEnteredFormat(backgroundColor,textFormat,horizontalAlignment,verticalAlignment,borders)"
                                    }
                                });
                            }
                        }
                    }
                }
            }

            // Auto-resize columns
            requests.Add(new Request
            {
                AutoResizeDimensions = new AutoResizeDimensionsRequest
                {
                    Dimensions = new DimensionRange
                    {
                        SheetId = sheetId,
                        Dimension = "COLUMNS",
                        StartIndex = colOffset,
                        EndIndex = colOffset + totalCols
                    }
                }
            });

            // Set spacer column widths
            int[] spacers = new[] { colsPerTable, 2 * colsPerTable + 1, 3 * colsPerTable + 2 };
            foreach (int sp in spacers)
            {
                requests.Add(new Request
                {
                    UpdateDimensionProperties = new UpdateDimensionPropertiesRequest
                    {
                        Range = new DimensionRange
                        {
                            SheetId = sheetId,
                            Dimension = "COLUMNS",
                            StartIndex = colOffset + sp,
                            EndIndex = colOffset + sp + 1
                        },
                        Properties = new DimensionProperties { PixelSize = 25 },
                        Fields = "pixelSize"
                    }
                });
            }

            if (notes != null && notes.Count > 0)
            {
                var rowsData = new List<RowData>();
                foreach (var noteRow in notes)
                {
                    var rowData = new RowData { Values = new List<CellData>() };
                    if (noteRow != null)
                    {
                        foreach (var noteText in noteRow)
                        {
                            rowData.Values.Add(new CellData { Note = string.IsNullOrWhiteSpace(noteText) ? null : noteText });
                        }
                    }
                    rowsData.Add(rowData);
                }

                requests.Add(new Request
                {
                    UpdateCells = new UpdateCellsRequest
                    {
                        Start = new GridCoordinate { SheetId = sheetId, RowIndex = rowOffset, ColumnIndex = colOffset },
                        Rows = rowsData,
                        Fields = "note"
                    }
                });
            }

            var batchUpdate = new BatchUpdateSpreadsheetRequest { Requests = requests };
            await _service.Spreadsheets.BatchUpdate(batchUpdate, _spreadsheetId).ExecuteAsync();
        }

        private string NormalizeStartCell(string startCell)
        {
            if (string.IsNullOrWhiteSpace(startCell)) return "A1";
            startCell = startCell.Trim().ToUpperInvariant();
            
            var colMatch = System.Text.RegularExpressions.Regex.Match(startCell, @"[A-Z]+");
            string col = colMatch.Success ? colMatch.Value : "A";
            
            var rowMatch = System.Text.RegularExpressions.Regex.Match(startCell, @"\d+");
            string row = rowMatch.Success && int.TryParse(rowMatch.Value, out int r) && r >= 1 ? r.ToString() : "1";
            
            return col + row;
        }

        private int ParseStartRow(string startCell)
        {
            if (string.IsNullOrWhiteSpace(startCell)) return 1;
            var match = System.Text.RegularExpressions.Regex.Match(startCell, @"\d+");
            if (match.Success && int.TryParse(match.Value, out int row) && row >= 1)
            {
                return row;
            }
            return 1;
        }

        private int ColumnLetterToColumnIndex(string columnLetter)
        {
            if (string.IsNullOrWhiteSpace(columnLetter)) return 0;
            columnLetter = columnLetter.Trim().ToUpperInvariant();
            int index = 0;
            foreach (char c in columnLetter)
            {
                if (c >= 'A' && c <= 'Z')
                {
                    index = index * 26 + (c - 'A' + 1);
                }
            }
            return index - 1;
        }

        private SheetColor GetGradeBackgroundColor(string grade)
        {
            if (string.IsNullOrWhiteSpace(grade)) return null;
            grade = grade.Trim();
            if (grade.Equals("A", StringComparison.OrdinalIgnoreCase)) return new SheetColor { Red = 0.298f, Green = 0.686f, Blue = 0.314f }; // #4CAF50
            if (grade.Equals("B", StringComparison.OrdinalIgnoreCase)) return new SheetColor { Red = 0.129f, Green = 0.588f, Blue = 0.953f }; // #2196F3
            if (grade.Equals("C", StringComparison.OrdinalIgnoreCase)) return new SheetColor { Red = 1.0f, Green = 0.757f, Blue = 0.027f }; // #FFC107
            if (grade.Equals("D", StringComparison.OrdinalIgnoreCase)) return new SheetColor { Red = 1.0f, Green = 0.596f, Blue = 0.0f }; // #FF9800
            if (grade.Equals("E", StringComparison.OrdinalIgnoreCase) || grade.StartsWith("Struggling", StringComparison.OrdinalIgnoreCase)) return new SheetColor { Red = 0.957f, Green = 0.263f, Blue = 0.212f }; // #F44336
            if (grade.Equals("Exam", StringComparison.OrdinalIgnoreCase)) return new SheetColor { Red = 0.612f, Green = 0.153f, Blue = 0.690f }; // #9C27B0
            if (grade.Equals("No Teach", StringComparison.OrdinalIgnoreCase)) return new SheetColor { Red = 0.376f, Green = 0.490f, Blue = 0.545f }; // #607D8B
            if (grade.Contains("Exam", StringComparison.OrdinalIgnoreCase) && grade.Contains("No Teach", StringComparison.OrdinalIgnoreCase)) return new SheetColor { Red = 0.0f, Green = 0.588f, Blue = 0.533f }; // #009688
            return null;
        }

        public async Task<List<string>> GetSheetNamesAsync()
        {
            var spreadsheet = await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
            var names = new List<string>();
            foreach (var sheet in spreadsheet.Sheets)
            {
                names.Add(sheet.Properties.Title);
            }
            return names;
        }

        public async Task<System.Data.DataTable> ReadSheetAsDataTableAsync(string sheetName)
        {
            try
            {
                var request = _service.Spreadsheets.Values.Get(_spreadsheetId, sheetName);
                var response = await request.ExecuteAsync();
                var values = response.Values;

                if (values == null || values.Count == 0)
                    return null;

                var table = new System.Data.DataTable(sheetName);

                int maxCols = 0;
                foreach (var row in values)
                {
                    if (row.Count > maxCols) maxCols = row.Count;
                }

                for (int i = 0; i < maxCols; i++)
                {
                    table.Columns.Add($"Column{i}");
                }

                foreach (var row in values)
                {
                    var dataRow = table.NewRow();
                    for (int i = 0; i < row.Count; i++)
                    {
                        dataRow[i] = row[i]?.ToString() ?? "";
                    }
                    table.Rows.Add(dataRow);
                }

                return table;
            }
            catch
            {
                return null;
            }
        }

        public async Task CleanupOldPrintLogsAsync(int retentionDays)
        {
            try
            {
                var spreadsheet = await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
                var requests = new List<Request>();
                
                foreach (var sheet in spreadsheet.Sheets)
                {
                    string title = sheet.Properties.Title;
                    if (title.StartsWith("PrintLog_", StringComparison.OrdinalIgnoreCase))
                    {
                        string datePart = title.Substring("PrintLog_".Length);
                        if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime sheetDate))
                        {
                            if ((DateTime.Today - sheetDate).TotalDays > retentionDays)
                            {
                                requests.Add(new Request
                                {
                                    DeleteSheet = new DeleteSheetRequest
                                    {
                                        SheetId = sheet.Properties.SheetId
                                    }
                                });
                                System.Diagnostics.Debug.WriteLine($"Google Sheets: Adding old sheet tab '{title}' to deletion queue.");
                            }
                        }
                    }
                }

                if (requests.Count > 0)
                {
                    var batchUpdate = new BatchUpdateSpreadsheetRequest
                    {
                        Requests = requests
                    };
                    await _service.Spreadsheets.BatchUpdate(batchUpdate, _spreadsheetId).ExecuteAsync();
                    System.Diagnostics.Debug.WriteLine($"Google Sheets: Successfully cleaned up {requests.Count} old print log sheet tabs.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Google Sheets error during old logs cleanup: " + ex.Message);
            }
        }
    }
}
