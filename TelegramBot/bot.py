import os
import json
import re
import time
import threading
from datetime import datetime, timedelta
from http.server import BaseHTTPRequestHandler, HTTPServer
import telebot
from telebot import types
from google.oauth2 import service_account
from googleapiclient.discovery import build
from googleapiclient.errors import HttpError

# Load configurations
def load_local_spreadsheet_id():
    """Attempts to load Spreadsheet ID from local C# appsettings.json if running on Windows."""
    try:
        appdata = os.environ.get("APPDATA")
        if appdata:
            settings_path = os.path.join(appdata, "PrintTrackerApp", "appsettings.json")
            if os.path.exists(settings_path):
                with open(settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    # Check PrintLogSpreadsheetId first, then fallback to GoogleSpreadsheetId
                    sheet_id = settings.get("PrintLogSpreadsheetId") or settings.get("GoogleSpreadsheetId")
                    if sheet_id:
                        print(f"Loaded Spreadsheet ID from local C# settings: {sheet_id}")
                        return sheet_id
    except Exception as e:
        print(f"Could not load Spreadsheet ID from local C# settings: {e}")
    return None

SPREADSHEET_ID = os.environ.get("SPREADSHEET_ID") or load_local_spreadsheet_id()

if not SPREADSHEET_ID:
    print("WARNING: SPREADSHEET_ID is not configured. The bot will wait for environment variables or local settings.")

# Global state for bot tracking
current_bot = None
current_token = None
bot_thread = None
is_running = True

# In-memory storage for user states
# Format: { chat_id: { 'state': 'WAITING_FOR_DATE' | 'WAITING_FOR_FILENAME', 'date': 'YYYY-MM-DD' } }
user_states = {}

# Simple cache for Google Sheets log data to prevent API quota limits
# Format: { date_str: (timestamp, list_of_rows) }
sheet_cache = {}
CACHE_DURATION_SECONDS = 60  # Cache duration in seconds

def get_sheet_data_cached(spreadsheet_id, range_name, date_str):
    """Fetches sheet data, caching it in memory to handle concurrent queries without hitting quotas."""
    now = time.time()
    if date_str in sheet_cache:
        cached_time, cached_data = sheet_cache[date_str]
        if now - cached_time < CACHE_DURATION_SECONDS:
            print(f"Cache hit for date: {date_str}")
            return cached_data
            
    # Fetch from Google Sheets API
    service = get_sheets_service()
    result = service.spreadsheets().values().get(
        spreadsheetId=spreadsheet_id, 
        range=range_name
    ).execute()
    rows = result.get('values', [])
    
    # Store in cache
    sheet_cache[date_str] = (now, rows)
    print(f"Cache updated for date: {date_str}")
    return rows

def get_sheets_service():
    """Initializes Google Sheets Service using environment credentials or local files."""
    scopes = ["https://www.googleapis.com/auth/spreadsheets.readonly"]
    creds_json = os.environ.get("GOOGLE_CREDENTIALS_JSON")
    
    if creds_json:
        try:
            creds_dict = json.loads(creds_json)
            creds = service_account.Credentials.from_service_account_info(creds_dict, scopes=scopes)
            return build("sheets", "v4", credentials=creds)
        except Exception as e:
            print(f"Error parsing GOOGLE_CREDENTIALS_JSON environment variable: {e}")
    
    # Fallback to local file in current directory
    if os.path.exists("google_credentials.json"):
        creds = service_account.Credentials.from_service_account_file("google_credentials.json", scopes=scopes)
        return build("sheets", "v4", credentials=creds)
        
    # Fallback to local C# app credentials folder
    appdata = os.environ.get("APPDATA")
    if appdata:
        local_creds = os.path.join(appdata, "PrintTrackerApp", "google_credentials.json")
        if os.path.exists(local_creds):
            creds = service_account.Credentials.from_service_account_file(local_creds, scopes=scopes)
            return build("sheets", "v4", credentials=creds)
            
    raise FileNotFoundError("Google Credentials could not be loaded. Please set GOOGLE_CREDENTIALS_JSON env variable or place 'google_credentials.json' in the directory.")

def get_bot_token_from_sheet():
    """Fetches the Telegram Bot Token directly from the Google Sheets BotConfig tab."""
    if not SPREADSHEET_ID:
        return None
    try:
        service = get_sheets_service()
        result = service.spreadsheets().values().get(
            spreadsheetId=SPREADSHEET_ID, 
            range="BotConfig!A:B"
        ).execute()
        rows = result.get('values', [])
        
        token_map = {}
        for row in rows:
            if len(row) >= 2:
                token_map[row[0].strip()] = row[1].strip()
                
        # Prioritize dedicated Tracking Bot Token, fallback to general Bot Token
        tracking_token = token_map.get("TelegramTrackingBotToken")
        if tracking_token:
            return tracking_token
            
        general_token = token_map.get("TelegramBotToken")
        if general_token:
            return general_token
            
    except Exception as e:
        print(f"Could not read BotConfig from Google Sheet: {e}")
    return None

def get_bot_token_from_local_settings():
    """Attempts to load Telegram Tracking Bot Token or general Bot Token from local appsettings.json."""
    try:
        appdata = os.environ.get("APPDATA")
        if appdata:
            settings_path = os.path.join(appdata, "PrintTrackerApp", "appsettings.json")
            if os.path.exists(settings_path):
                with open(settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    token = settings.get("TelegramTrackingBotToken") or settings.get("TelegramBotToken")
                    if token:
                        print(f"Loaded Telegram Bot Token from local C# settings: {token[:8]}...")
                        return token
    except Exception as e:
        print(f"Could not load Telegram Bot Token from local C# settings: {e}")
    return None

def get_main_keyboard():
    """Generates the main custom keyboard for date selection."""
    markup = types.ReplyKeyboardMarkup(resize_keyboard=True, one_time_keyboard=True)
    btn_today = types.KeyboardButton("📅 ថ្ងៃនេះ (Today)")
    btn_yesterday = types.KeyboardButton("📅 ម្សិលមិញ (Yesterday)")
    btn_custom = types.KeyboardButton("📅 បញ្ចូលថ្ងៃផ្សេង (Other Date)")
    markup.row(btn_today, btn_yesterday)
    markup.row(btn_custom)
    return markup

def get_cancel_keyboard():
    """Generates a keyboard with a cancel button."""
    markup = types.ReplyKeyboardMarkup(resize_keyboard=True, one_time_keyboard=True)
    markup.add(types.KeyboardButton("❌ បោះបង់ (Cancel)"))
    return markup

def register_handlers(bot_instance):
    """Registers all conversation handlers to the given telebot instance."""
    
    @bot_instance.message_handler(commands=['start', 'help'])
    def send_welcome(message):
        chat_id = message.chat.id
        user_states[chat_id] = {'state': 'WAITING_FOR_DATE', 'date': None}
        
        welcome_text = (
            "👋 សួស្តី! ខ្ញុំជា Bot សម្រាប់ឆែកមើល Status នៃឯកសារព្រីននៅក្នុង Print Log (Google Sheets)។\n"
            "Hello! I am a Telegram Bot to check print job statuses from the Google Sheets Print Log.\n\n"
            "សូមជ្រើសរើសថ្ងៃខែដែលលោកអ្នកចង់ពិនិត្យ៖\n"
            "Please select the date to check:"
        )
        bot_instance.send_message(chat_id, welcome_text, reply_markup=get_main_keyboard())

    @bot_instance.message_handler(func=lambda msg: user_states.get(msg.chat.id, {}).get('state') == 'WAITING_FOR_DATE')
    def handle_date_input(message):
        chat_id = message.chat.id
        text = message.text.strip()
        
        if text == "❌ បោះបង់ (Cancel)":
            user_states[chat_id] = {'state': 'WAITING_FOR_DATE', 'date': None}
            bot_instance.send_message(chat_id, "បានបោះបង់ការស្វែងរក។", reply_markup=get_main_keyboard())
            return

        selected_date = None
        
        if text == "📅 ថ្ងៃនេះ (Today)":
            selected_date = datetime.now().strftime("%Y-%m-%d")
        elif text == "📅 ម្សិលមិញ (Yesterday)":
            selected_date = (datetime.now() - timedelta(days=1)).strftime("%Y-%m-%d")
        elif text == "📅 បញ្ចូលថ្ងៃផ្សេង (Other Date)":
            bot_instance.send_message(
                chat_id, 
                "សូមវាយបញ្ចូលថ្ងៃខែក្នុងទម្រង់ **YYYY-MM-DD** (ឧទាហរណ៍៖ `2026-07-16`)៖\n"
                "Please enter the date in YYYY-MM-DD format:", 
                parse_mode='Markdown',
                reply_markup=get_cancel_keyboard()
            )
            return
        else:
            if re.match(r'^\d{4}-\d{2}-\d{2}$', text):
                try:
                    datetime.strptime(text, "%Y-%m-%d")
                    selected_date = text
                except ValueError:
                    bot_instance.send_message(chat_id, "⚠️ ថ្ងៃខែមិនត្រឹមត្រូវឡើយ។ សូមព្យាយាមឡើងវិញ៖\nInvalid date. Please try again:")
                    return
            else:
                bot_instance.send_message(
                    chat_id, 
                    "⚠️ សូមជ្រើសរើសប៊ូតុងខាងក្រោម ឬបញ្ចូលថ្ងៃខែក្នុងទម្រង់ `YYYY-MM-DD`៖\n"
                    "Please choose a button below or enter date in `YYYY-MM-DD`:",
                    reply_markup=get_main_keyboard()
                )
                return

        user_states[chat_id] = {'state': 'WAITING_FOR_FILENAME', 'date': selected_date}
        prompt_text = (
            f"📅 ថ្ងៃខែដែលបានជ្រើសរើស៖ `{selected_date}`\n\n"
            "សូមវាយបញ្ចូលឈ្មោះឯកសារ (File Name) ដែលចង់ស្វែងរក៖\n"
            "Please enter the document/file name to search:"
        )
        bot_instance.send_message(chat_id, prompt_text, parse_mode='Markdown', reply_markup=get_cancel_keyboard())

    @bot_instance.message_handler(func=lambda msg: user_states.get(msg.chat.id, {}).get('state') == 'WAITING_FOR_FILENAME')
    def handle_filename_input(message):
        chat_id = message.chat.id
        search_term = message.text.strip()
        
        if search_term == "❌ បោះបង់ (Cancel)":
            user_states[chat_id] = {'state': 'WAITING_FOR_DATE', 'date': None}
            bot_instance.send_message(chat_id, "បានបោះបង់ការស្វែងរក។", reply_markup=get_main_keyboard())
            return
            
        date_str = user_states[chat_id]['date']
        
        if not SPREADSHEET_ID:
            bot_instance.send_message(chat_id, "❌ Error: Google Spreadsheet ID is not configured on the server. Please contact administrator.")
            return
            
        loading_msg = bot_instance.send_message(chat_id, "🔍 កំពុងស្វែងរកក្នុង Google Sheets... សូមរង់ចាំមួយភ្លែត។\nSearching in Google Sheets... please wait.")
        
        try:
            sheet_name = f"PrintLog_{date_str}"
            range_name = f"{sheet_name}!A:J"
            rows = get_sheet_data_cached(SPREADSHEET_ID, range_name, date_str)
            
            if not rows:
                bot_instance.edit_message_text(
                    chat_id=chat_id,
                    message_id=loading_msg.message_id,
                    text=f"❌ មិនមានទិន្នន័យនៅក្នុង Tab `{sheet_name}` ឡើយ។"
                )
                user_states[chat_id] = {'state': 'WAITING_FOR_DATE', 'date': None}
                bot_instance.send_message(chat_id, "តើអ្នកចង់ឆែកថ្ងៃខែផ្សេងទៀតទេ?", reply_markup=get_main_keyboard())
                return
                
            header = rows[0]
            def find_index(col_name, default):
                try:
                    return next(i for i, h in enumerate(header) if col_name.lower() in h.lower())
                except StopIteration:
                    return default
                    
            doc_idx = find_index("Document Name", 1)
            web_idx = find_index("Hold Print Name", 2)
            time_idx = find_index("Time", 0)
            user_idx = find_index("User", 6)
            userid_idx = find_index("User ID", 3)
            pages_idx = find_index("Pages", 4)
            copies_idx = find_index("Copies", 5)
            status_idx = find_index("Status", 8)
            
            matches = []
            search_term_lower = search_term.lower()
            
            for row in rows[1:]:
                padded_row = row + [""] * (max(doc_idx, web_idx, time_idx, user_idx, userid_idx, pages_idx, copies_idx, status_idx) + 1 - len(row))
                
                doc_name = padded_row[doc_idx]
                web_name = padded_row[web_idx]
                
                if search_term_lower in doc_name.lower() or search_term_lower in web_name.lower():
                    matches.append({
                        'time': padded_row[time_idx],
                        'doc_name': doc_name if doc_name else web_name,
                        'user': padded_row[user_idx],
                        'user_id': padded_row[userid_idx],
                        'pages': padded_row[pages_idx],
                        'copies': padded_row[copies_idx],
                        'status': padded_row[status_idx]
                    })
                    
            if len(matches) > 0:
                try:
                    matches.sort(key=lambda m: m['time'], reverse=True)
                except Exception:
                    pass
                    
                response_text = (
                    f"🔍 *លទ្ធផលស្វែងរកសម្រាប់ថ្ងៃទី {date_str}៖*\n"
                    f"រកឃើញឯកសារទាក់ទងចំនួន៖ *{len(matches)}*\n\n"
                )
                
                show_limit = min(len(matches), 15)
                for idx, match in enumerate(matches[:show_limit]):
                    status = match['status']
                    status_emoji = "✅"
                    if "error" in status.lower() or "fail" in status.lower():
                        status_emoji = "❌"
                    elif "spool" in status.lower() or "wait" in status.lower() or "process" in status.lower():
                        status_emoji = "⏳"
                        
                    response_text += (
                        f"{idx+1}. 📄 *ឈ្មោះ៖* {match['doc_name']}\n"
                        f"   • *ម៉ោង៖* {match['time']}\n"
                        f"   • *អ្នកព្រីន៖* {match['user']} ({match['user_id']})\n"
                        f"   • *ទំព័រ៖* {match['pages']} (ច្បាប់៖ {match['copies']})\n"
                        f"   • *Status៖* {status_emoji} `{status}`\n\n"
                    )
                    
                if len(matches) > show_limit:
                    response_text += f"⚠️ _បង្ហាញតែ ១៥ ឯកសារចុងក្រោយគេប៉ុណ្ណោះ (សរុប {len(matches)})_"
                    
                bot_instance.edit_message_text(
                    chat_id=chat_id,
                    message_id=loading_msg.message_id,
                    text=response_text,
                    parse_mode='Markdown'
                )
            else:
                bot_instance.edit_message_text(
                    chat_id=chat_id,
                    message_id=loading_msg.message_id,
                    text=f"🔍 មិនមានឯកសារណាដែលមានឈ្មោះ `{search_term}` ក្នុងថ្ងៃទី `{date_str}` ឡើយ।"
                )
                
        except HttpError as err:
            error_details = ""
            try:
                error_details = json.loads(err.content.decode('utf-8'))
                message = error_details.get('error', {}).get('message', '')
            except Exception:
                message = str(err)
                
            if "quota" in message.lower() or "rate limit" in message.lower():
                bot_instance.edit_message_text(
                    chat_id=chat_id,
                    message_id=loading_msg.message_id,
                    text=f"⚠️ *ដែនកំណត់ស្កេនរបស់ Google API ត្រូវបានប្រើប្រាស់អស់ហើយ។* សូមរង់ចាំ ១ នាទី រួចសាកល្បងម្ដងទៀត។\n"
                         f"Google API Quota exceeded. (Details: {message})",
                    parse_mode='Markdown'
                )
            elif "requested entity was not found" in message.lower() or "bad request" in message.lower() or "not found" in message.lower() or "unable to parse range" in message.lower():
                bot_instance.edit_message_text(
                    chat_id=chat_id,
                    message_id=loading_msg.message_id,
                    text=f"❌ មិនមានទិន្នន័យ Print Log សម្រាប់ថ្ងៃទី `{date_str}` ឡើយ。\n(សូមប្រាកដថា PC បានបើក និង Sync ទិន្នន័យរួចហើយ)\n\n*(Details: {message})*"
                )
            else:
                bot_instance.edit_message_text(
                    chat_id=chat_id,
                    message_id=loading_msg.message_id,
                    text=f"⚠️ Error accessing Google Sheets API: {message}"
                )
        except Exception as e:
            bot_instance.edit_message_text(
                chat_id=chat_id,
                message_id=loading_msg.message_id,
                text=f"⚠️ Error: {str(e)}"
            )
            
        user_states[chat_id] = {'state': 'WAITING_FOR_DATE', 'date': None}

        bot_instance.send_message(chat_id, "តើអ្នកចង់ឆែកថ្ងៃខែផ្សេងទៀតទេ?", reply_markup=get_main_keyboard())

def bot_polling_target(bot_instance):
    print("Bot polling thread started.")
    try:
        bot_instance.infinity_polling(timeout=20, long_polling_timeout=10)
    except Exception as ex:
        print(f"Bot polling exception (this is normal when stopping): {ex}")
    print("Bot polling thread stopped.")

def monitor_and_run_bot():
    """Background manager loop checking spreadsheet config and launching the Telegram Bot."""
    global current_bot, current_token, bot_thread, is_running
    
    print("Bot manager loop started. Checking Google Sheets configuration...")
    
    while is_running:
        try:
            # 1. Fetch token from Sheet (with local settings/environment fallback)
            token = get_bot_token_from_sheet()
            if not token:
                token = get_bot_token_from_local_settings()
            if not token:
                token = os.environ.get("TELEGRAM_BOT_TOKEN")
                
            if not token:
                print("No Telegram Bot Token found in Google Sheet (BotConfig tab), local settings, or environment variables. Waiting 5m...")
                time.sleep(300)
                continue
                
            # 2. Check if token has changed or if it's the first run
            if token != current_token:
                print(f"Telegram Bot Token changed/initialized. Restarting bot with new token...")
                
                # Stop existing polling if active
                if current_bot:
                    print("Stopping active bot polling...")
                    current_bot.stop_all_polling()
                    if bot_thread and bot_thread.is_alive():
                        bot_thread.join(timeout=5)
                
                # Setup new bot instance
                current_token = token
                current_bot = telebot.TeleBot(token)
                
                # Register handlers
                register_handlers(current_bot)
                
                # Start polling in a background daemon thread
                bot_thread = threading.Thread(target=bot_polling_target, args=(current_bot,))
                bot_thread.daemon = True
                bot_thread.start()
                print("New Telegram Bot successfully started.")
                
        except Exception as e:
            print(f"Error in bot manager loop: {e}")
            
        time.sleep(300)

class HealthCheckHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header("Content-type", "text/plain; charset=utf-8")
        self.end_headers()
        self.wfile.write(b"Telegram Bot is running!")

    def log_message(self, format, *args):
        # Suppress request logs to prevent console clutter
        return

def start_health_check_server():
    port = int(os.environ.get("PORT", 8080))
    server = HTTPServer(("0.0.0.0", port), HealthCheckHandler)
    print(f"Starting health check web server on port {port}...")
    try:
        server.serve_forever()
    except Exception as e:
        print(f"Health check web server stopped: {e}")

if __name__ == '__main__':
    # Start health check server if PORT or RENDER environment variable is present (needed for Render Free Web Service)
    if os.environ.get("PORT") or os.environ.get("RENDER"):
        web_thread = threading.Thread(target=start_health_check_server)
        web_thread.daemon = True
        web_thread.start()

    try:
        monitor_and_run_bot()
    except KeyboardInterrupt:
        print("Stopping bot manager...")
        is_running = False
        if current_bot:
            current_bot.stop_all_polling()
