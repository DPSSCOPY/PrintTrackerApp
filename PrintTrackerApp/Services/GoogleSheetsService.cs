using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Drive.v3;
using SheetColor = Google.Apis.Sheets.v4.Data.Color;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PrintTrackerApp.Services
{
    public class DashboardExportTabData
    {
        public string PeriodName { get; set; }
        public string TabName { get; set; }
        public IList<IList<object>> Data { get; set; }
        public IList<IList<string>> Notes { get; set; }
    }

    public class GoogleSheetsService
    {
        private static readonly string[] Scopes = { SheetsService.Scope.Spreadsheets, DriveService.Scope.DriveReadonly };
        private readonly string _applicationName = "Print Tracker App";
        private readonly SheetsService _service;
        private readonly DriveService _driveService;
        private readonly string _spreadsheetId;

        private Dictionary<string, SheetProperties> _sheetCache = new Dictionary<string, SheetProperties>(StringComparer.OrdinalIgnoreCase);
        private bool _isCacheLoaded = false;

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

        private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> apiCall, int maxRetries = 6, int initialDelayMs = 1000)
        {
            int delay = initialDelayMs;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await apiCall();
                }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                                                          (int)ex.HttpStatusCode == 429 ||
                                                          ex.HttpStatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                                                          ex.HttpStatusCode == System.Net.HttpStatusCode.InternalServerError ||
                                                          ex.HttpStatusCode == System.Net.HttpStatusCode.BadGateway)
                {
                    if (i == maxRetries - 1) throw;
                    System.Diagnostics.Debug.WriteLine($"Google Sheets API Transient Error ({ex.HttpStatusCode}). Retrying in {delay}ms... Attempt {i + 1}/{maxRetries}");
                    await Task.Delay(delay);
                    delay *= 2;
                }
                catch (System.Net.Http.HttpRequestException ex)
                {
                    if (i == maxRetries - 1) throw;
                    System.Diagnostics.Debug.WriteLine($"Google Sheets Network Exception ({ex.Message}). Retrying in {delay}ms... Attempt {i + 1}/{maxRetries}");
                    await Task.Delay(delay);
                    delay *= 2;
                }
            }
            return await apiCall();
        }

        private static async Task ExecuteWithRetryAsync(Func<Task> apiCall, int maxRetries = 6, int initialDelayMs = 1000)
        {
            int delay = initialDelayMs;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await apiCall();
                    return;
                }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                                                          (int)ex.HttpStatusCode == 429 ||
                                                          ex.HttpStatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                                                          ex.HttpStatusCode == System.Net.HttpStatusCode.InternalServerError ||
                                                          ex.HttpStatusCode == System.Net.HttpStatusCode.BadGateway)
                {
                    if (i == maxRetries - 1) throw;
                    System.Diagnostics.Debug.WriteLine($"Google Sheets API Transient Error ({ex.HttpStatusCode}). Retrying in {delay}ms... Attempt {i + 1}/{maxRetries}");
                    await Task.Delay(delay);
                    delay *= 2;
                }
                catch (System.Net.Http.HttpRequestException ex)
                {
                    if (i == maxRetries - 1) throw;
                    System.Diagnostics.Debug.WriteLine($"Google Sheets Network Exception ({ex.Message}). Retrying in {delay}ms... Attempt {i + 1}/{maxRetries}");
                    await Task.Delay(delay);
                    delay *= 2;
                }
            }
            await apiCall();
        }

        public async Task EnsureSheetCacheAsync(bool forceRefresh = false)
        {
            if (!_isCacheLoaded || forceRefresh)
            {
                var spreadsheet = await ExecuteWithRetryAsync(() => _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync());
                var newCache = new Dictionary<string, SheetProperties>(StringComparer.OrdinalIgnoreCase);
                if (spreadsheet?.Sheets != null)
                {
                    foreach (var sheet in spreadsheet.Sheets)
                    {
                        if (sheet.Properties?.Title != null)
                        {
                            newCache[sheet.Properties.Title] = sheet.Properties;
                        }
                    }
                }
                _sheetCache = newCache;
                _isCacheLoaded = true;
            }
        }

        public async Task<DateTime?> GetSpreadsheetModifiedTimeAsync()
        {
            try
            {
                var request = _driveService.Files.Get(_spreadsheetId);
                request.Fields = "modifiedTime";
                var file = await ExecuteWithRetryAsync(() => request.ExecuteAsync());
                return file.ModifiedTimeDateTimeOffset?.DateTime;
            }
            catch
            {
                return null;
            }
        }

        public async Task<int> EnsureSheetExistsAsync(string sheetName)
        {
            await EnsureSheetCacheAsync();
            if (_sheetCache.TryGetValue(sheetName, out var props) && props.SheetId.HasValue)
            {
                return props.SheetId.Value;
            }

            await EnsureSheetCacheAsync(forceRefresh: true);
            if (_sheetCache.TryGetValue(sheetName, out props) && props.SheetId.HasValue)
            {
                return props.SheetId.Value;
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

            var response = await ExecuteWithRetryAsync(() => _service.Spreadsheets.BatchUpdate(batchUpdate, _spreadsheetId).ExecuteAsync());
            var createdProps = response.Replies[0].AddSheet.Properties;
            _sheetCache[sheetName] = createdProps;
            return createdProps.SheetId.Value;
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
            await ExecuteWithRetryAsync(() => deleteRequest.ExecuteAsync());
        }

        public async Task WriteDataAsync(string sheetName, IList<IList<object>> data)
        {
            var valueRange = new ValueRange { Values = data };
            var updateRequest = _service.Spreadsheets.Values.Update(valueRange, _spreadsheetId, $"{sheetName}!A1");
            updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            
            await ExecuteWithRetryAsync(() => updateRequest.ExecuteAsync());
        }

        public async Task WriteCellValueAsync(string sheetName, string cellA1, object value)
        {
            var valueRange = new ValueRange { Values = new List<IList<object>> { new List<object> { value } } };
            var updateRequest = _service.Spreadsheets.Values.Update(valueRange, _spreadsheetId, $"{sheetName}!{cellA1}");
            updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await ExecuteWithRetryAsync(() => updateRequest.ExecuteAsync());
        }

        public async Task WriteAndFormatDashboardDataAsync(string sheetName, IList<IList<object>> data, IList<IList<string>> notes = null, string startCell = "A1")
        {
            if (data == null || data.Count == 0) return;

            int sheetId = await EnsureSheetExistsAsync(sheetName);

            string normalizedStart = NormalizeStartCell(startCell);

            // 1. Write text values
            var valueRange = new ValueRange { Values = data };
            var updateRequest = _service.Spreadsheets.Values.Update(valueRange, _spreadsheetId, $"{sheetName}!{normalizedStart}");
            updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await ExecuteWithRetryAsync(() => updateRequest.ExecuteAsync());

            // 2. Build formatting requests & execute
            var requests = BuildDashboardFormattingRequests(sheetId, sheetName, data, notes, startCell);
            var batchUpdate = new BatchUpdateSpreadsheetRequest { Requests = requests };
            await ExecuteWithRetryAsync(() => _service.Spreadsheets.BatchUpdate(batchUpdate, _spreadsheetId).ExecuteAsync());
        }

        private List<Request> BuildDashboardFormattingRequests(int sheetId, string sheetName, IList<IList<object>> data, IList<IList<string>> notes = null, string startCell = "A1")
        {
            var requests = new List<Request>();
            if (data == null || data.Count == 0) return requests;

            string normalizedStart = NormalizeStartCell(startCell);
            int startRow = ParseStartRow(normalizedStart);
            var colMatch = System.Text.RegularExpressions.Regex.Match(normalizedStart, @"[A-Z]+");
            int colOffset = colMatch.Success ? ColumnLetterToColumnIndex(colMatch.Value) : 0;
            int rowOffset = startRow - 1;

            // Clear old formatting and notes
            requests.Add(new Request
            {
                RepeatCell = new RepeatCellRequest
                {
                    Range = new GridRange { SheetId = sheetId, StartRowIndex = 0, StartColumnIndex = 0 },
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
            int colsPerTable = (sheetName.Equals("PT", StringComparison.OrdinalIgnoreCase) || sheetName.Contains("_PT_")) ? 4 : 3;
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

            return requests;
        }

        public async Task BatchExportDashboardAsync(List<DashboardExportTabData> tabDataList, List<string> selectedPeriods, string startCell, string dropdownCell, Dictionary<string, string> botConfigData = null)
        {
            if (tabDataList == null || tabDataList.Count == 0) return;

            // Step 1: Ensure sheet cache is up to date and create all missing sheets in ONE batch update call
            await EnsureSheetCacheAsync(forceRefresh: true);

            var requiredSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "FT", "PT", "KH"
            };
            if (botConfigData != null && botConfigData.Count > 0)
            {
                requiredSheetNames.Add("BotConfig");
            }

            foreach (var item in tabDataList)
            {
                requiredSheetNames.Add($"_Data_{item.TabName}_{item.PeriodName}");
            }

            var addRequests = new List<Request>();
            foreach (var sheetName in requiredSheetNames)
            {
                if (!_sheetCache.ContainsKey(sheetName))
                {
                    addRequests.Add(new Request
                    {
                        AddSheet = new AddSheetRequest
                        {
                            Properties = new SheetProperties { Title = sheetName }
                        }
                    });
                }
            }

            if (addRequests.Count > 0)
            {
                var addBatch = new BatchUpdateSpreadsheetRequest { Requests = addRequests };
                await ExecuteWithRetryAsync(() => _service.Spreadsheets.BatchUpdate(addBatch, _spreadsheetId).ExecuteAsync());
                await EnsureSheetCacheAsync(forceRefresh: true);
            }

            // Step 2: Batch write all values to all sheets in ONE Values.BatchUpdate call
            var valueRanges = new List<ValueRange>();

            if (botConfigData != null && botConfigData.Count > 0)
            {
                var botRows = new List<IList<object>> { new List<object> { "Key", "Value" } };
                foreach (var kvp in botConfigData)
                {
                    botRows.Add(new List<object> { kvp.Key, kvp.Value });
                }
                valueRanges.Add(new ValueRange { Range = "'BotConfig'!A1", Values = botRows });
            }

            string normStart = NormalizeStartCell(startCell);
            foreach (var item in tabDataList)
            {
                string hiddenSheetName = $"_Data_{item.TabName}_{item.PeriodName}";
                if (item.Data != null && item.Data.Count > 0)
                {
                    valueRanges.Add(new ValueRange { Range = $"'{hiddenSheetName}'!{normStart}", Values = item.Data });
                }
            }

            string firstPeriod = selectedPeriods.FirstOrDefault() ?? "";
            string normDropdown = NormalizeStartCell(dropdownCell);
            foreach (var mainTab in new[] { "FT", "PT", "KH" })
            {
                if (_sheetCache.ContainsKey(mainTab))
                {
                    valueRanges.Add(new ValueRange
                    {
                        Range = $"'{mainTab}'!{normDropdown}",
                        Values = new List<IList<object>> { new List<object> { firstPeriod } }
                    });
                }
            }

            // Batch clear main tabs prior to copying to clear previous content
            var clearRanges = new List<string>();
            foreach (var mainTab in new[] { "FT", "PT", "KH" })
            {
                if (_sheetCache.ContainsKey(mainTab))
                {
                    clearRanges.Add($"'{mainTab}'!A1:Z1000");
                }
            }

            if (clearRanges.Count > 0)
            {
                var batchClear = new BatchClearValuesRequest { Ranges = clearRanges };
                await ExecuteWithRetryAsync(() => _service.Spreadsheets.Values.BatchClear(batchClear, _spreadsheetId).ExecuteAsync());
            }

            if (valueRanges.Count > 0)
            {
                var batchValuesReq = new BatchUpdateValuesRequest
                {
                    ValueInputOption = "USER_ENTERED",
                    Data = valueRanges
                };
                await ExecuteWithRetryAsync(() => _service.Spreadsheets.Values.BatchUpdate(batchValuesReq, _spreadsheetId).ExecuteAsync());
            }

            // Step 3: Batch update formatting, notes, hiding, dropdown validation, and copying in ONE master BatchUpdate call
            var masterRequests = new List<Request>();

            foreach (var item in tabDataList)
            {
                string hiddenSheetName = $"_Data_{item.TabName}_{item.PeriodName}";
                if (_sheetCache.TryGetValue(hiddenSheetName, out var props) && props.SheetId.HasValue)
                {
                    int sheetId = props.SheetId.Value;
                    var fmtRequests = BuildDashboardFormattingRequests(sheetId, item.TabName, item.Data, item.Notes, startCell);
                    masterRequests.AddRange(fmtRequests);

                    masterRequests.Add(new Request
                    {
                        UpdateSheetProperties = new UpdateSheetPropertiesRequest
                        {
                            Properties = new SheetProperties { SheetId = sheetId, Hidden = true },
                            Fields = "hidden"
                        }
                    });
                }
            }

            foreach (var mainTab in new[] { "FT", "PT", "KH" })
            {
                if (_sheetCache.TryGetValue(mainTab, out var mainProps) && mainProps.SheetId.HasValue)
                {
                    int mainSheetId = mainProps.SheetId.Value;
                    int dropCol = ColumnLetterToColumnIndex(normDropdown);
                    int dropRow = ParseStartRow(normDropdown) - 1;

                    var conditionValues = selectedPeriods.Select(p => new ConditionValue { UserEnteredValue = p }).ToList();
                    masterRequests.Add(new Request
                    {
                        SetDataValidation = new SetDataValidationRequest
                        {
                            Range = new GridRange
                            {
                                SheetId = mainSheetId,
                                StartRowIndex = dropRow,
                                EndRowIndex = dropRow + 1,
                                StartColumnIndex = dropCol,
                                EndColumnIndex = dropCol + 1
                            },
                            Rule = new DataValidationRule
                            {
                                Condition = new BooleanCondition { Type = "ONE_OF_LIST", Values = conditionValues },
                                ShowCustomUi = true,
                                InputMessage = "Choose a period"
                            }
                        }
                    });

                    string hiddenSource = $"_Data_{mainTab}_{firstPeriod}";
                    if (_sheetCache.TryGetValue(hiddenSource, out var sourceProps) && sourceProps.SheetId.HasValue)
                    {
                        int sourceSheetId = sourceProps.SheetId.Value;
                        int startRow = ParseStartRow(normStart) - 1;
                        var colMatch = System.Text.RegularExpressions.Regex.Match(normStart, @"[A-Z]+");
                        int startCol = colMatch.Success ? ColumnLetterToColumnIndex(colMatch.Value) : 0;

                        int endRow = sourceProps.GridProperties?.RowCount ?? 1000;
                        int endCol = sourceProps.GridProperties?.ColumnCount ?? 26;

                        masterRequests.Add(new Request
                        {
                            CopyPaste = new CopyPasteRequest
                            {
                                Source = new GridRange
                                {
                                    SheetId = sourceSheetId,
                                    StartRowIndex = startRow,
                                    EndRowIndex = endRow,
                                    StartColumnIndex = startCol,
                                    EndColumnIndex = endCol
                                },
                                Destination = new GridRange
                                {
                                    SheetId = mainSheetId,
                                    StartRowIndex = startRow,
                                    EndRowIndex = endRow,
                                    StartColumnIndex = startCol,
                                    EndColumnIndex = endCol
                                },
                                PasteType = "PASTE_NORMAL",
                                PasteOrientation = "NORMAL"
                            }
                        });
                    }
                }
            }

            if (masterRequests.Count > 0)
            {
                var masterBatch = new BatchUpdateSpreadsheetRequest { Requests = masterRequests };
                await ExecuteWithRetryAsync(() => _service.Spreadsheets.BatchUpdate(masterBatch, _spreadsheetId).ExecuteAsync());
            }
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
            await EnsureSheetCacheAsync();
            return _sheetCache.Keys.ToList();
        }

        public async Task<System.Data.DataTable> ReadSheetAsDataTableAsync(string sheetName)
        {
            try
            {
                var request = _service.Spreadsheets.Values.Get(_spreadsheetId, sheetName);
                var response = await ExecuteWithRetryAsync(() => request.ExecuteAsync());
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
                await EnsureSheetCacheAsync();
                var requests = new List<Request>();
                var sheetsToDelete = new List<string>();

                foreach (var kvp in _sheetCache)
                {
                    string title = kvp.Key;
                    var props = kvp.Value;
                    if (title.StartsWith("PrintLog_", StringComparison.OrdinalIgnoreCase))
                    {
                        string datePart = title.Substring("PrintLog_".Length);
                        if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime sheetDate))
                        {
                            if ((DateTime.Today - sheetDate).TotalDays > retentionDays && props.SheetId.HasValue)
                            {
                                requests.Add(new Request
                                {
                                    DeleteSheet = new DeleteSheetRequest
                                    {
                                        SheetId = props.SheetId.Value
                                    }
                                });
                                sheetsToDelete.Add(title);
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
                    await ExecuteWithRetryAsync(() => _service.Spreadsheets.BatchUpdate(batchUpdate, _spreadsheetId).ExecuteAsync());
                    foreach (var title in sheetsToDelete)
                    {
                        _sheetCache.Remove(title);
                    }
                    System.Diagnostics.Debug.WriteLine($"Google Sheets: Successfully cleaned up {requests.Count} old print log sheet tabs.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Google Sheets error during old logs cleanup: " + ex.Message);
            }
        }

        public async Task HideSheetAsync(string sheetName)
        {
            int sheetId = await EnsureSheetExistsAsync(sheetName);
            var request = new Request
            {
                UpdateSheetProperties = new UpdateSheetPropertiesRequest
                {
                    Properties = new SheetProperties
                    {
                        SheetId = sheetId,
                        Hidden = true
                    },
                    Fields = "hidden"
                }
            };
            var batchUpdate = new BatchUpdateSpreadsheetRequest { Requests = new List<Request> { request } } ;
            await ExecuteWithRetryAsync(() => _service.Spreadsheets.BatchUpdate(batchUpdate, _spreadsheetId).ExecuteAsync());
        }

        public async Task SetDropdownValidationAsync(string sheetName, string cellA1, List<string> options)
        {
            int sheetId = await EnsureSheetExistsAsync(sheetName);
            string normalized = NormalizeStartCell(cellA1);
            var colMatch = System.Text.RegularExpressions.Regex.Match(normalized, @"[A-Z]+");
            int colIndex = colMatch.Success ? ColumnLetterToColumnIndex(colMatch.Value) : 0;
            int rowIndex = ParseStartRow(normalized) - 1;

            var conditionValues = new List<ConditionValue>();
            foreach (var opt in options)
            {
                conditionValues.Add(new ConditionValue { UserEnteredValue = opt });
            }

            var rule = new DataValidationRule
            {
                Condition = new BooleanCondition
                {
                    Type = "ONE_OF_LIST",
                    Values = conditionValues
                },
                ShowCustomUi = true,
                InputMessage = "Choose a period"
            };

            var request = new Request
            {
                SetDataValidation = new SetDataValidationRequest
                {
                    Range = new GridRange
                    {
                        SheetId = sheetId,
                        StartRowIndex = rowIndex,
                        EndRowIndex = rowIndex + 1,
                        StartColumnIndex = colIndex,
                        EndColumnIndex = colIndex + 1
                    },
                    Rule = rule
                }
            };

            var batchUpdate = new BatchUpdateSpreadsheetRequest { Requests = new List<Request> { request } };
            await ExecuteWithRetryAsync(() => _service.Spreadsheets.BatchUpdate(batchUpdate, _spreadsheetId).ExecuteAsync());
        }

        public async Task CopyRangeAsync(string sourceSheetName, string destSheetName, string startCellA1)
        {
            await EnsureSheetCacheAsync();

            if (!_sheetCache.TryGetValue(sourceSheetName, out var sourceSheet) || sourceSheet == null || !sourceSheet.SheetId.HasValue)
            {
                throw new Exception($"Source sheet '{sourceSheetName}' not found in the spreadsheet.");
            }

            if (!_sheetCache.TryGetValue(destSheetName, out var destSheet) || destSheet == null || !destSheet.SheetId.HasValue)
            {
                throw new Exception($"Destination sheet '{destSheetName}' not found in the spreadsheet.");
            }

            int sourceSheetId = sourceSheet.SheetId.Value;
            int destSheetId = destSheet.SheetId.Value;

            string normalized = NormalizeStartCell(startCellA1);
            int startRow = ParseStartRow(normalized) - 1;
            var colMatch = System.Text.RegularExpressions.Regex.Match(normalized, @"[A-Z]+");
            int startCol = colMatch.Success ? ColumnLetterToColumnIndex(colMatch.Value) : 0;

            int srcRows = sourceSheet.GridProperties?.RowCount ?? 1000;
            int srcCols = sourceSheet.GridProperties?.ColumnCount ?? 26;

            int destRows = destSheet.GridProperties?.RowCount ?? 1000;
            int destCols = destSheet.GridProperties?.ColumnCount ?? 26;

            // Clamp rows/cols to avoid out-of-bounds exceptions on sheets with different sizes
            int endRow = Math.Min(srcRows, destRows);
            int endCol = Math.Min(srcCols, destCols);

            if (endRow <= startRow || endCol <= startCol)
            {
                return; // Nothing to copy
            }

            var copyPasteRequest = new Request
            {
                CopyPaste = new CopyPasteRequest
                {
                    Source = new GridRange
                    {
                        SheetId = sourceSheetId,
                        StartRowIndex = startRow,
                        EndRowIndex = endRow,
                        StartColumnIndex = startCol,
                        EndColumnIndex = endCol
                    },
                    Destination = new GridRange
                    {
                        SheetId = destSheetId,
                        StartRowIndex = startRow,
                        EndRowIndex = endRow,
                        StartColumnIndex = startCol,
                        EndColumnIndex = endCol
                    },
                    PasteType = "PASTE_NORMAL",
                    PasteOrientation = "NORMAL"
                }
            };

            var batchUpdate = new BatchUpdateSpreadsheetRequest { Requests = new List<Request> { copyPasteRequest } };
            await ExecuteWithRetryAsync(() => _service.Spreadsheets.BatchUpdate(batchUpdate, _spreadsheetId).ExecuteAsync());
        }
    }
}
