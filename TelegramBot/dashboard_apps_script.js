/**
 * Google Apps Script for Dashboard Spreadsheet (FT, PT, KH tabs)
 * 
 * Instructions:
 * 1. Open your Exported Dashboard Google Spreadsheet.
 * 2. Click Extensions > Apps Script.
 * 3. Delete any default code and paste this script.
 * 4. Save the project.
 * 5. This script handles the Dropdown Week/Monthly selection and dynamically updates
 *    the visible dashboard sheet and teacher comments (notes).
 */

/**
 * Dynamic Dashboard Updates
 * Triggers when the user changes the Week/Monthly dropdown selection.
 */
function onEdit(e) {
  if (!e) return;
  const range = e.range;
  const sheet = range.getSheet();
  const sheetName = sheet.getName();
  
  // 1. Only process edits on the dashboard viewport sheets
  if (sheetName !== "FT" && sheetName !== "PT" && sheetName !== "KH") return;
  
  const val = range.getValue() ? range.getValue().toString().trim() : "";
  
  // 2. Verify if the edited cell is a period selection dropdown
  const isDropdownValue = (val.indexOf("Week ") === 0 || val === "Monthly");
  if (!isDropdownValue) return;
  
  // 3. Locate the table header row dynamically by searching for the "Teacher" header down column A
  let headerRow = -1;
  const colIndex = range.getColumn(); // Column A is 1
  const maxRowsSearch = sheet.getLastRow();
  
  for (let r = range.getRow() + 1; r <= Math.min(range.getRow() + 5, maxRowsSearch); r++) {
    const valAtCell = sheet.getRange(r, colIndex).getValue();
    if (valAtCell && valAtCell.toString().trim() === "Teacher") {
      headerRow = r;
      break;
    }
  }
  
  if (headerRow === -1) {
    headerRow = range.getRow() + 1;
  }
  
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const sourceSheetName = "_Data_" + sheetName + "_" + val;
  const sourceSheet = ss.getSheetByName(sourceSheetName);
  if (!sourceSheet) {
    Browser.msgBox("Warning", "Could not find data source tab: " + sourceSheetName, Browser.Buttons.OK);
    return;
  }
  
  const dataStartRow = headerRow + 1; // Start clearing and copying BELOW the "Teacher" header row
  const maxRows = sheet.getMaxRows();
  const maxCols = sheet.getMaxColumns();
  
  // 4. CLEAR ONLY data rows below the header (headerRow + 1 down to maxRows)
  // This clears all text values, cell colors, formatting, AND cell notes (black triangles)!
  if (maxRows >= dataStartRow) {
    const clearRange = sheet.getRange(dataStartRow, 1, maxRows - dataStartRow + 1, maxCols);
    clearRange.clear();
    clearRange.clearNote();
  }
  
  // 5. Copy data rows from hidden source sheet to viewport sheet
  const lastRow = sourceSheet.getLastRow();
  const lastCol = sourceSheet.getLastColumn();
  
  if (lastRow >= dataStartRow && lastCol >= 1) {
    const sourceRange = sourceSheet.getRange(dataStartRow, 1, lastRow - dataStartRow + 1, lastCol);
    const destRange = sheet.getRange(dataStartRow, 1, lastRow - dataStartRow + 1, lastCol);
    
    sourceRange.copyTo(destRange, SpreadsheetApp.CopyPasteType.PASTE_NORMAL, false);
  }
}
