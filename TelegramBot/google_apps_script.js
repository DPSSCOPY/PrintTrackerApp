/**
 * Telegram Bot for Google Sheets Print Log (Google Apps Script Web App version)
 * 
 * Instructions:
 * 1. Open your Google Sheet.
 * 2. Click Extensions > Apps Script.
 * 3. Delete any default code and paste this script.
 * 4. Replace the TOKEN variable below (or ensure your "BotConfig" tab contains the token).
 * 5. Deploy as Web App (New Deployment > Web App, Execute as: Me, Who has access: Anyone).
 * 6. Copy the Web App URL.
 * 7. In the Apps Script editor, run the `setupWebhook` function after pasting the Web App URL in it.
 */

// Global configuration (fallback if BotConfig tab does not exist)
const DEFAULT_TELEGRAM_TOKEN = ""; 

/**
 * Handle incoming Telegram requests (Webhook)
 */
function doPost(e) {
  try {
    if (!e || !e.postData || !e.postData.contents) {
      return HtmlService.createHtmlOutput("No data received.");
    }
    
    const update = JSON.parse(e.postData.contents);
    if (!update.message) {
      return HtmlService.createHtmlOutput("No message object found.");
    }
    
    const message = update.message;
    const chatId = message.chat.id;
    const text = message.text ? message.text.trim() : "";
    
    handleMessage(chatId, text);
  } catch (err) {
    Logger.log("Error in doPost: " + err);
  }
  return HtmlService.createHtmlOutput("ok");
}

/**
 * Fetch the Telegram Bot Token from the Google Sheets BotConfig tab (with caching)
 */
function getBotToken() {
  const cache = CacheService.getScriptCache();
  const cachedToken = cache.get("telegram_bot_token");
  if (cachedToken) {
    return cachedToken;
  }

  try {
    const ss = SpreadsheetApp.getActiveSpreadsheet();
    const sheet = ss.getSheetByName("BotConfig");
    if (!sheet) {
      return DEFAULT_TELEGRAM_TOKEN || null;
    }
    const data = sheet.getDataRange().getValues();
    let tokenMap = {};
    for (let i = 0; i < data.length; i++) {
      if (data[i][0]) {
        tokenMap[data[i][0].toString().trim()] = data[i][1] ? data[i][1].toString().trim() : "";
      }
    }
    const token = tokenMap["TelegramTrackingBotToken"] || tokenMap["TelegramBotToken"] || DEFAULT_TELEGRAM_TOKEN || null;
    if (token) {
      // Cache the token for 6 hours (21600 seconds)
      cache.put("telegram_bot_token", token, 21600);
    }
    return token;
  } catch (e) {
    Logger.log("Error reading BotConfig: " + e);
    return DEFAULT_TELEGRAM_TOKEN || null;
  }
}

/**
 * Sends a request to the Telegram Bot API
 */
function sendTelegram(method, payload) {
  const token = getBotToken();
  if (!token) {
    Logger.log("Error: Bot token not configured.");
    return null;
  }
  const url = "https://api.telegram.org/bot" + token + "/" + method;
  const options = {
    method: "post",
    contentType: "application/json",
    payload: JSON.stringify(payload),
    muteHttpExceptions: true
  };
  try {
    const response = UrlFetchApp.fetch(url, options);
    return JSON.parse(response.getContentText());
  } catch (e) {
    Logger.log("Error sending to Telegram API: " + e);
    return null;
  }
}

/**
 * Get the current state of a user from Cache
 */
function getUserState(chatId) {
  const cache = CacheService.getScriptCache();
  const data = cache.get("state_" + chatId);
  if (data) {
    return JSON.parse(data);
  }
  return { state: 'WAITING_FOR_DATE', date: null };
}

/**
 * Set the state of a user in Cache
 */
function setUserState(chatId, stateObj) {
  const cache = CacheService.getScriptCache();
  cache.put("state_" + chatId, JSON.stringify(stateObj), 1800); // Expires after 30 minutes
}

/**
 * Get the Spreadsheet timezone with caching
 */
function getSpreadsheetTimezone() {
  const cache = CacheService.getScriptCache();
  const cachedTimezone = cache.get("spreadsheet_timezone");
  if (cachedTimezone) {
    return cachedTimezone;
  }
  
  try {
    const ss = SpreadsheetApp.getActiveSpreadsheet();
    const timezone = ss.getSpreadsheetTimeZone();
    if (timezone) {
      // Cache the timezone for 6 hours
      cache.put("spreadsheet_timezone", timezone, 21600);
      return timezone;
    }
  } catch (e) {
    Logger.log("Error getting timezone: " + e);
  }
  return "Asia/Phnom_Penh"; // Default fallback timezone for Cambodia
}

/**
 * Get formatted date based on timezone of the spreadsheet
 */
function getFormattedDate(offsetDays) {
  const date = new Date();
  if (offsetDays) {
    date.setDate(date.getDate() + offsetDays);
  }
  const timezone = getSpreadsheetTimezone();
  return Utilities.formatDate(date, timezone, "yyyy-MM-dd");
}

/**
 * Keyboards
 */
function getMainKeyboard() {
  return {
    keyboard: [
      [{ text: "📅 ថ្ងៃនេះ (Today)" }, { text: "📅 ម្សិលមិញ (Yesterday)" }],
      [{ text: "📅 បញ្ចូលថ្ងៃផ្សេង (Other Date)" }],
      [{ text: "🔄 ចាប់ផ្ដើមឡើងវិញ (Restart Bot)" }]
    ],
    resize_keyboard: true,
    one_time_keyboard: true
  };
}

function getCancelKeyboard() {
  return {
    keyboard: [
      [{ text: "❌ បោះបង់ (Cancel)" }, { text: "🔄 ចាប់ផ្ដើមឡើងវិញ (Restart Bot)" }]
    ],
    resize_keyboard: true,
    one_time_keyboard: true
  };
}

/**
 * Escape Special Markdown Characters
 */
function escapeMarkdown(text) {
  if (!text) return "";
  let escaped = text.toString();
  const chars = ['_', '*', '`', '['];
  for (let i = 0; i < chars.length; i++) {
    escaped = escaped.split(chars[i]).join('\\' + chars[i]);
  }
  return escaped;
}

/**
 * Get Status Emoji
 */
function getStatusEmoji(status) {
  const statusLower = status.toLowerCase();
  if (statusLower.includes("print complete") || statusLower.includes("completed") || statusLower.includes("success") || statusLower.includes("printed")) return "✅";
  if (statusLower.includes("sent to printer")) return "📤";
  if (statusLower.includes("storing complete")) return "📥";
  if (statusLower.includes("printing")) return "🖨️";
  if (statusLower.includes("spool") || statusLower.includes("process")) return "🔄";
  if (statusLower.includes("wait") || statusLower.includes("pending")) return "⏳";
  if (statusLower.includes("hold") || statusLower.includes("held")) return "⏸️";
  if (statusLower.includes("cancel") || statusLower.includes("delete") || statusLower.includes("abort")) return "🚫";
  if (statusLower.includes("error") || statusLower.includes("fail")) return "❌";
  return "📄";
}

/**
 * Search the PrintLog Sheet
 */
function searchPrintLog(dateStr, searchTerm) {
  const sheetName = "PrintLog_" + dateStr;
  
  // Try to load cached sheet data to save Apps Script execution time
  const cache = CacheService.getScriptCache();
  const cacheKey = "sheet_data_" + dateStr;
  const cachedData = cache.get(cacheKey);
  
  let data;
  if (cachedData) {
    try {
      data = JSON.parse(cachedData);
    } catch (e) {
      Logger.log("Error parsing cached sheet data: " + e);
    }
  }
  
  if (!data) {
    const ss = SpreadsheetApp.getActiveSpreadsheet();
    const sheet = ss.getSheetByName(sheetName);
    
    if (!sheet) {
      return { 
        success: false, 
        message: "❌ មិនមានទិន្នន័យ Print Log សម្រាប់ថ្ងៃទី `" + dateStr + "` ឡើយ。\n(សូមប្រាកដថា PC បានបើក និង Sync ទិន្នន័យរួចហើយ)" 
      };
    }
    
    const lastRow = sheet.getLastRow();
    if (lastRow <= 1) {
      return { 
        success: false, 
        message: "❌ មិនមានទិន្នន័យនៅក្នុង Tab `" + sheetName + "` ឡើយ។" 
      };
    }
    
    // Read only columns A to J (10 columns) up to lastRow to avoid fetching empty rows/columns
    data = sheet.getRange(1, 1, lastRow, 10).getValues();
    
    // Cache the sheet data for 30 seconds (safe for size limits if rows < 1500)
    try {
      cache.put(cacheKey, JSON.stringify(data), 30);
    } catch (e) {
      Logger.log("Could not cache sheet data: " + e);
    }
  }
  
  const header = data[0];
  
  function findIndex(colName, defaultVal) {
    for (let i = 0; i < header.length; i++) {
      if (header[i].toString().toLowerCase().indexOf(colName.toLowerCase()) !== -1) {
        return i;
      }
    }
    return defaultVal;
  }
  
  const docIdx = findIndex("Document Name", 1);
  const webIdx = findIndex("Hold Print Name", 2);
  const timeIdx = findIndex("Time", 0);
  const userIdx = findIndex("User", 6);
  const useridIdx = findIndex("User ID", 3);
  const pagesIdx = findIndex("Pages", 4);
  const copiesIdx = findIndex("Copies", 5);
  const statusIdx = findIndex("Status", 8);
  
  let matches = [];
  const searchTermLower = searchTerm.toLowerCase();
  
  for (let r = 1; r < data.length; r++) {
    const row = data[r];
    const docName = row[docIdx] ? row[docIdx].toString() : "";
    const webName = row[webIdx] ? row[webIdx].toString() : "";
    
    if (docName.toLowerCase().indexOf(searchTermLower) !== -1 || webName.toLowerCase().indexOf(searchTermLower) !== -1) {
      matches.push({
        time: row[timeIdx] ? row[timeIdx].toString().split(" GMT")[0] : "",
        doc_name: docName || webName,
        user: row[userIdx] ? row[userIdx].toString() : "",
        user_id: row[useridIdx] ? row[useridIdx].toString() : "",
        pages: row[pagesIdx] ? row[pagesIdx].toString() : "",
        copies: row[copiesIdx] ? row[copiesIdx].toString() : "",
        status: row[statusIdx] ? row[statusIdx].toString() : ""
      });
    }
  }
  
  return { success: true, matches: matches };
}

/**
 * Handle Conversation States
 */
function handleMessage(chatId, text) {
  let userState = getUserState(chatId);
  
  // Restart commands
  if (text === "/restart" || text === "🔄 ចាប់ផ្ដើមឡើងវិញ (Restart Bot)" || text === "/start" || text === "/help") {
    userState = { state: 'WAITING_FOR_DATE', date: null };
    setUserState(chatId, userState);
    sendTelegram("sendMessage", {
      chat_id: chatId,
      text: "👋 សួស្តី! ខ្ញុំជា Bot សម្រាប់ឆែកមើល Status នៃឯកសារព្រីននៅក្នុង Print Log (Google Sheets)។\n\n" +
            "សូមជ្រើសរើសថ្ងៃខែដែលលោកអ្នកចង់ពិនិត្យ៖\n" +
            "Please select the date to check:",
      reply_markup: getMainKeyboard()
    });
    return;
  }
  
  if (userState.state === 'WAITING_FOR_DATE') {
    if (text === "❌ បោះបង់ (Cancel)") {
      userState = { state: 'WAITING_FOR_DATE', date: null };
      setUserState(chatId, userState);
      sendTelegram("sendMessage", {
        chat_id: chatId,
        text: "បានបោះបង់ការស្វែងរក។",
        reply_markup: getMainKeyboard()
      });
      return;
    }
    
    let selectedDate = null;
    if (text === "📅 ថ្ងៃនេះ (Today)") {
      selectedDate = getFormattedDate(0);
    } else if (text === "📅 ម្សិលមិញ (Yesterday)") {
      selectedDate = getFormattedDate(-1);
    } else if (text === "📅 បញ្ចូលថ្ងៃផ្សេង (Other Date)") {
      sendTelegram("sendMessage", {
        chat_id: chatId,
        text: "សូមវាយបញ្ចូលថ្ងៃខែក្នុងទម្រង់ **YYYY-MM-DD** (ឧទាហរណ៍៖ `2026-07-16`)៖\n" +
              "Please enter the date in YYYY-MM-DD format:",
        parse_mode: 'Markdown',
        reply_markup: getCancelKeyboard()
      });
      return;
    } else {
      // Regex check YYYY-MM-DD
      const dateRegex = /^\d{4}-\d{2}-\d{2}$/;
      if (dateRegex.test(text)) {
        // Validate date
        const parts = text.split("-");
        const y = parseInt(parts[0], 10);
        const m = parseInt(parts[1], 10) - 1;
        const d = parseInt(parts[2], 10);
        const dateObj = new Date(y, m, d);
        if (dateObj.getFullYear() === y && dateObj.getMonth() === m && dateObj.getDate() === d) {
          selectedDate = text;
        } else {
          sendTelegram("sendMessage", {
            chat_id: chatId,
            text: "⚠️ ថ្ងៃខែមិនត្រឹមត្រូវឡើយ។ សូមព្យាយាមឡើងវិញ៖\nInvalid date. Please try again:"
          });
          return;
        }
      } else {
        sendTelegram("sendMessage", {
          chat_id: chatId,
          text: "⚠️ សូមជ្រើសរើសប៊ូតុងខាងក្រោម ឬបញ្ចូលថ្ងៃខែក្នុងទម្រង់ `YYYY-MM-DD`៖\n" +
                "Please choose a button below or enter date in `YYYY-MM-DD`:",
          reply_markup: getMainKeyboard()
        });
        return;
      }
    }
    
    // Update state to WAITING_FOR_FILENAME
    userState = { state: 'WAITING_FOR_FILENAME', date: selectedDate };
    setUserState(chatId, userState);
    sendTelegram("sendMessage", {
      chat_id: chatId,
      text: "📅 ថ្ងៃខែដែលបានជ្រើសរើស៖ `" + selectedDate + "`\n\n" +
            "សូមវាយបញ្ចូលឈ្មោះឯកសារ (File Name) ដែលចង់ស្វែងរក៖\n" +
            "Please enter the document/file name to search:",
      parse_mode: 'Markdown',
      reply_markup: getCancelKeyboard()
    });
    
  } else if (userState.state === 'WAITING_FOR_FILENAME') {
    if (text === "❌ បោះបង់ (Cancel)") {
      userState = { state: 'WAITING_FOR_DATE', date: null };
      setUserState(chatId, userState);
      sendTelegram("sendMessage", {
        chat_id: chatId,
        text: "បានបោះបង់ការស្វែងរក។",
        reply_markup: getMainKeyboard()
      });
      return;
    }
    
    const dateStr = userState.date;
    
    // Query data directly (no slow loading message to save API call latency)
    const searchRes = searchPrintLog(dateStr, text);
    
    let responseText = "";
    if (searchRes.success) {
      const matches = searchRes.matches;
      if (matches.length > 0) {
        // Sort matches descending by time
        try {
          matches.sort(function(a, b) {
            return b.time.localeCompare(a.time);
          });
        } catch (e) {}
        
        responseText = "🔍 *លទ្ធផលស្វែងរកសម្រាប់ថ្ងៃទី " + dateStr + "៖*\n" +
                       "រកឃើញឯកសារទាក់ទងចំនួន៖ *" + matches.length + "*\n\n";
        
        const showLimit = Math.min(matches.length, 15);
        for (let i = 0; i < showLimit; i++) {
          const match = matches[i];
          const emoji = getStatusEmoji(match.status);
          const nameEsc = escapeMarkdown(match.doc_name);
          responseText += (i + 1) + ". 📄 *ឈ្មោះ៖* " + nameEsc + "\n" +
                          "   • *ម៉ោង៖* " + match.time + "\n" +
                          "   • *ទំព័រ៖* " + match.pages + " (ច្បាប់៖ " + match.copies + ")\n" +
                          "   • *Status៖* " + emoji + " `" + match.status + "`\n\n";
        }
        
        if (matches.length > showLimit) {
          responseText += "⚠️ _បង្ហាញតែ ១៥ ឯកសារចុងក្រោយគេប៉ុណ្ណោះ (សរុប " + matches.length + ")_";
        }
      } else {
        responseText = "🔍 មិនមានឯកសារណាដែលមានឈ្មោះ `" + text + "` ក្នុងថ្ងៃទី `" + dateStr + "` ឡើយ។";
      }
    } else {
      responseText = searchRes.message;
    }
    
    // Reset state to initial date prompt
    userState = { state: 'WAITING_FOR_DATE', date: null };
    setUserState(chatId, userState);
    
    // Send single message with results and the main menu keyboard directly
    sendTelegram("sendMessage", {
      chat_id: chatId,
      text: responseText + "\n\n👉 *តើអ្នកចង់ឆែកថ្ងៃខែផ្សេងទៀតទេ?* (Would you like to check another date?)",
      parse_mode: 'Markdown',
      reply_markup: getMainKeyboard()
    });
  }
}

/**
 * Run this function from the Apps Script editor to setup the Telegram Webhook
 * 
 * INSTRUCTIONS:
 * 1. Deploy the script as a Web App (New Deployment > Web App, Who has access: Anyone)
 * 2. Copy the Web App URL.
 * 3. Replace the placeholder URL in `webAppUrl` below with your URL.
 * 4. Select `setupWebhook` in the toolbar and click "Run".
 */
function setupWebhook() {
  // Clear cache to ensure we get the latest token from the sheet
  const cache = CacheService.getScriptCache();
  cache.remove("telegram_bot_token");
  cache.remove("spreadsheet_timezone");

  const webAppUrl = "YOUR_WEB_APP_URL_HERE"; // <-- PUT YOUR DEPLOYED WEB APP URL HERE
  
  if (webAppUrl === "" || webAppUrl.indexOf("script.google.com") === -1) {
    Logger.log("ERROR: Please replace the placeholder with your actual deployed Web App URL (containing script.google.com).");
    return;
  }
  
  const token = getBotToken();
  if (!token) {
    Logger.log("ERROR: Bot token not found in BotConfig tab. Please configure your Telegram token first.");
    return;
  }
  
  const url = "https://api.telegram.org/bot" + token + "/setWebhook?url=" + encodeURIComponent(webAppUrl);
  const response = UrlFetchApp.fetch(url);
  Logger.log("Set Webhook Response: " + response.getContentText());
}

/**
 * Check the current webhook status
 */
function checkWebhook() {
  // Clear cache to ensure we get the latest token from the sheet
  const cache = CacheService.getScriptCache();
  cache.remove("telegram_bot_token");
  cache.remove("spreadsheet_timezone");

  const token = getBotToken();
  if (!token) {
    Logger.log("ERROR: Bot token not configured.");
    return;
  }
  const url = "https://api.telegram.org/bot" + token + "/getWebhookInfo";
  const response = UrlFetchApp.fetch(url);
  Logger.log("Webhook Info: " + response.getContentText());
}

