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
  
  // 3. Locate the table start row dynamically by searching for the "Teacher" header down column A
  let startRow = -1;
  const colIndex = range.getColumn(); // Column A is 1
  const maxRowsSearch = sheet.getLastRow();
  
  // Look up to 5 rows below the edited dropdown cell
  for (let r = range.getRow() + 1; r <= Math.min(range.getRow() + 5, maxRowsSearch); r++) {
    const valAtCell = sheet.getRange(r, colIndex).getValue();
    if (valAtCell && valAtCell.toString().trim() === "Teacher") {
      startRow = r;
      break;
    }
  }
  
  // Fallback to 1 row below the dropdown if the "Teacher" header wasn't found
  if (startRow === -1) {
    startRow = range.getRow() + 1;
  }
  
  const startCol = colIndex;
  
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const sourceSheetName = "_Data_" + sheetName + "_" + val;
  const sourceSheet = ss.getSheetByName(sourceSheetName);
  if (!sourceSheet) {
    Browser.msgBox("Warning", "Could not find data source tab: " + sourceSheetName, Browser.Buttons.OK);
    return;
  }
  
  // 4. Copy data (values, notes/comments, formatting) from hidden sheet to viewport sheet
  const lastRow = sourceSheet.getLastRow();
  const lastCol = sourceSheet.getLastColumn();
  
  const destLastRow = sheet.getLastRow();
  const destLastCol = sheet.getLastColumn();
  
  // Clear old destination viewport area completely (below the dropdown cell)
  if (destLastRow >= startRow && destLastCol >= startCol) {
    sheet.getRange(startRow, startCol, destLastRow - startRow + 1, destLastCol - startCol + 1).clear();
  }
  
  if (lastRow >= startRow && lastCol >= startCol) {
    const sourceRange = sourceSheet.getRange(startRow, startCol, lastRow - startRow + 1, lastCol - startCol + 1);
    const destRange = sheet.getRange(startRow, startCol, lastRow - startRow + 1, lastCol - startCol + 1);
    
    // Copy NORMAL (values, formatting, notes/comments)
    sourceRange.copyTo(destRange, SpreadsheetApp.CopyPasteType.PASTE_NORMAL, false);
  }
}
