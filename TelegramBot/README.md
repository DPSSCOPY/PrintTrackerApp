# Telegram Bot for Google Sheets Print Log (Cambodian / English)

សៀវភៅណែនាំអំពីការដំឡើង និងដំណើរការ Telegram Bot ដើម្បីឆែកមើល Print Log ពី Google Sheets ២៤ម៉ោងលើ២៤ម៉ោង។
Setup and deployment guide for the 24/7 Telegram Bot querying Print Logs from Google Sheets.

---

## ជំហានទី ១៖ បង្កើត Telegram Bot (Create Telegram Bot)
1. បើកកម្មវិធី Telegram រួចស្វែងរកឈ្មោះ `@BotFather`។
2. ចុច **Start** រួចផ្ញើពាក្យ `/newbot`។
3. វាយបញ្ចូលឈ្មោះ Bot របស់លោកអ្នក (ឧទាហរណ៍៖ `Print Tracker Bot`)។
4. វាយបញ្ចូលឈ្មោះ username របស់ Bot (ត្រូវតែបញ្ចប់ដោយពាក្យ `bot` ឧទហរណ៍៖ `my_print_tracker_bot`)។
5. ចម្លងទុកនូវ **HTTP API Access Token** (ឧទាហរណ៍៖ `1234567890:ABCdefGhIJKlmNoPQRsTUVwxyZ`)។

---

## ជំហានទី ២៖ រៀបចំ Google Sheets ក្នុងកម្មវិធី Print Tracker
សូមប្រាកដថាលោកអ្នកបានកំណត់ Google Sheets Integration នៅក្នុងកម្មវិធី `PrintTrackerApp` រួចរាល់៖
1. បើកកម្មវិធី `PrintTrackerApp` ចូលទៅកាន់ **Settings**។
2. ដាក់បញ្ចូល **Print Log Spreadsheet ID (Bot Database)** (ប្រសិនបើមិនបានដាក់បញ្ចូលវាទេ វានឹងស្វែងរកតាម Google Spreadsheet ID ជំនួសវិញ) និង Upload ឯកសារ **google_credentials.json** (Service Account Key)។
3. រាល់ពេលមានឯកសារព្រីន កម្មវិធីនឹងបង្កើត Tab ឈ្មោះ `PrintLog_YYYY-MM-DD` នៅក្នុង Google Sheet នោះដោយស្វ័យប្រវត្ត។

---

## ជំហានទី ៣៖ សាកល្បងដំណើរការលើកុំព្យូទ័រផ្ទាល់ខ្លួន (Run Locally for Testing)
ដើម្បីសាកល្បង Bot នៅលើកុំព្យូទ័ររបស់លោកអ្នកផ្ទាល់៖
1. បើក Terminal / Command Prompt ចូលទៅកាន់ថតឯកសារ `TelegramBot`៖
   ```bash
   cd TelegramBot
   ```
2. ដំឡើង Dependencies៖
   ```bash
   pip install -r requirements.txt
   ```
3. ដំណើរការ Bot ភ្លាមៗ (ដោយសារតែកូដ Python បានភ្ជាប់ជាមួយកម្មវិធី Print Tracker រួចជាស្រេច វានឹងស្វែងរក Google Credentials, Spreadsheet ID និង Bot Token ពី App Settings ដោយស្វ័យប្រវត្ត)៖
   ```bash
   python bot.py
   ```
4. ចូលទៅកាន់ Telegram រួចចុច Start លើ Bot របស់លោកអ្នក ដើម្បីធ្វើតេស្តសួររកទិន្នន័យ។

*(បញ្ជាក់៖ ប្រសិនបើលោកអ្នកចង់ដំណើរការនៅលើប្រព័ន្ធប្រតិបត្តិការផ្សេង ឬចង់ដាក់តម្លៃផ្ទាល់ខ្លួន លោកអ្នកនៅតែអាចកំណត់ Environment Variables ដូចជា `TELEGRAM_BOT_TOKEN`, `SPREADSHEET_ID` និង `GOOGLE_CREDENTIALS_JSON` បានធម្មតា)*

---

## ជំហានទី ៤៖ ដំឡើងឱ្យរត់ ២៤ម៉ោង/៧ថ្ងៃ ហ្វ្រី (Deploy 24/7 for FREE on Render)
ដើម្បីឱ្យ Bot ដំណើរការរហូត ទោះបីជាបិទ PC ក៏ដោយ លោកអ្នកអាចប្រើប្រាស់សេវាកម្មហ្វ្រីរបស់ **Render** (ប្រភេទ Web Service) និងសេវាកម្ម Ping ផ្សេងទៀតដើម្បីការពារកុំឱ្យវា Sleep ៖

### ១. ការរៀបចំដំឡើងនៅលើ Render
1. បង្កើត Project លើ GitHub រួច Push ឯកសារនៅក្នុងថត `TelegramBot` នេះចូលទៅកាន់ GitHub របស់លោកអ្នក (⚠️ **ហាមដាច់ខាត៖** កុំបញ្ចូលឯកសារ `google_credentials.json` ទៅក្នុង GitHub ដើម្បីសុវត្ថិភាព)។
2. ចូលទៅកាន់ [Render.com](https://render.com/) រួចចុះឈ្មោះប្រើប្រាស់ (Log in ជាមួយគណនី GitHub)។
3. ចុចប៊ូតុង **New +** ជ្រើសរើសយក **Web Service** (ដោយសារ Render ឈប់ផ្តល់សេវាកម្ម Background Worker ឥតគិតថ្លៃទៀតហើយ យើងត្រូវប្រើ Web Service ជំនួសវិញ។ កូដ Python ត្រូវបានបន្ថែម Web Server តូចមួយរួចជាស្រេច ដើម្បីឆ្លើយតបទៅនឹង Render)។
4. ភ្ជាប់ជាមួយ GitHub Repository ដែលទើបនឹងបង្កើតនោះ។
5. កំណត់ Settings ដូចខាងក្រោម៖
   * **Name**: `print-tracker-telegram-bot`
   * **Runtime**: `Python`
   * **Build Command**: `pip install -r requirements.txt`
   * **Start Command**: `python bot.py`
   * **Instance Type**: `Free`
6. ចុចលើផ្ទាំង **Environment** (ឬ Advanced) រួចបញ្ចូលអថេរខាងក្រោម (Environment Variables)៖
   * `TELEGRAM_BOT_TOKEN` : (តម្លៃ Token របស់ Telegram Bot ពី BotFather - មិនចាំបាច់បញ្ចូលទេ ប្រសិនបើលោកអ្នកបានកំណត់ "Tracking Bot Token" ក្នុង Settings រួចហើយ)
   * `SPREADSHEET_ID` : (តម្លៃ Google Spreadsheet ID)
   * `GOOGLE_CREDENTIALS_JSON` : (បើកឯកសារ `google_credentials.json` លើកុំព្យូទ័រលោកអ្នក រួចចម្លងកូដ JSON ទាំងស្រុងមក Paste ចូលទីនេះ)
7. ចុច **Deploy Web Service**។ វានឹងដំណើរការបង្កើត និងដាក់ឱ្យដំណើរការ Bot របស់លោកអ្នក។

### ២. ការកំណត់ដើម្បីការពារកុំឱ្យ Bot គេង (Prevent Sleep / Keep Alive)
ដោយសារតែ Render Web Service (Free Tier) នឹងចូលទៅគេង (Sleep/Spin down) បន្ទាប់ពីគ្មានសកម្មភាពរយៈពេល ១៥ នាទី លោកអ្នកត្រូវដំឡើងកម្មវិធី Ping ឥតគិតថ្លៃដើម្បីឱ្យវាភ្ញាក់រហូត ២៤ម៉ោង/៧ថ្ងៃ៖
1. បន្ទាប់ពីការ Deploy នៅលើ Render បានជោគជ័យ លោកអ្នកនឹងទទួលបាន **URL** របស់ Web Service នោះ (ឧទហរណ៍៖ `https://print-tracker-telegram-bot.onrender.com`)។
2. ចូលទៅកាន់សេវាកម្មឥតគិតថ្លៃណាមួយ ដូចជា [cron-job.org](https://cron-job.org/) ឬ [UptimeRobot](https://uptimerobot.com/)។
3. បង្កើតគណនី រួចបង្កើត **Cronjob** ឬ **Monitor** ថ្មី៖
   * **Type**: `HTTP` (ឬ `HTTPS`)
   * **URL**: ដាក់អាសយដ្ឋាន URL របស់ Render ខាងលើ (ឧទាហរណ៍៖ `https://print-tracker-telegram-bot.onrender.com`)
   * **Interval / Schedule**: កំណត់ឱ្យរត់រៀងរាល់ **១០ នាទី** ម្តង (`Every 10 minutes`)
4. រក្សាទុក (Save)។ សេវាកម្មនេះនឹងធ្វើការផ្ញើសំណើទៅកាន់ Bot របស់លោកអ្នករៀងរាល់ ១០ នាទីម្តង ដើម្បីកុំឱ្យវា Sleep ធានាថា Bot ដំណើរការបាន ២៤ម៉ោងលើ២៤ម៉ោងជានិច្ច!

---

## ជំហានទី ៥៖ ដំឡើងតាម Google Apps Script (Stable ជាង និងហ្វ្រី ១០០%)

ប្រសិនបើលោកអ្នកជួបបញ្ហាគាំង ឬយឺតពេលប្រើ Render លោកអ្នកអាចប្រើប្រាស់ **Google Apps Script** ជំនួសវិញ។ វាដំណើរការនៅលើ Cloud របស់ Google ផ្ទាល់ ឆ្លើយតបលឿន និងមិនចេះគេង (Sleep) ឡើយ៖

### ១. ការបញ្ចូលកូដទៅក្នុង Google Sheet
1. បើក Google Sheet របស់លោកអ្នក។
2. ចូលទៅកាន់ **Extensions** (ផ្នែកបន្ថែម) -> ជ្រើសរើស **Apps Script**។
3. លុបកូដលំនាំដើមទាំងអស់ចោល រួចចម្លងកូដនៅក្នុងឯកសារ [google_apps_script.js](file:///e:/Code/Tracking_Print/TelegramBot/google_apps_script.js) មក Paste ចូល។
4. ចុចប៊ូតុង **Save** (រូបថាសម៉ាញ៉េទិច)។

### ២. ដាក់ឱ្យដំណើរការជា Web App (Deploy)
1. នៅជ្រុងខាងស្តាំផ្នែកខាងលើ ចុចលើប៊ូតុង **Deploy** -> ជ្រើសរើស **New deployment**។
2. ចុចលើរូបកង់ធ្មេញ (Select type) -> ជ្រើសរើសយក **Web app**។
3. កំណត់ការកំណត់ដូចខាងក្រោម៖
   * **Description**: `Print Tracker Bot Webhook`
   * **Execute as**: `Me` (គណនី Google របស់អ្នក)
   * **Who has access**: `Anyone` (អ្នកណាក៏ដោយ - ⚠️ **សំខាន់ណាស់៖** ត្រូវតែជ្រើសរើស Anyone ដើម្បីឱ្យ Telegram Webhook ផ្ញើសារចូលបាន)
4. ចុចប៊ូតុង **Deploy**។
5. ប្រព័ន្ធនឹងសួរការអនុញ្ញាត (Authorize Access) សូមចុច **Authorize Access** រួចជ្រើសរើសគណនី Google របស់អ្នក ចុច **Advanced** -> ចុច **Go to Untitled project (unsafe)** -> រួចចុច **Allow**។
6. បន្ទាប់ពី Deploy រួចរាល់ លោកអ្នកនឹងទទួលបាន **Web app URL** (ឧទាហរណ៍៖ `https://script.google.com/macros/s/.../exec`)។ សូមចម្លង (Copy) URL នោះទុក។

### ៣. ភ្ជាប់ Telegram Bot ទៅកាន់ Web App (Webhook Setup)
1. ត្រលប់មកផ្ទាំង Apps Script editor វិញ។
2. ស្វែងរកមុខងារ (Function) ឈ្មោះ `setupWebhook` (នៅផ្នែកខាងក្រោមបង្អស់នៃកូដ)។
3. ជំនួសពាក្យ `"YOUR_WEB_APP_URL_HERE"` ទៅជា **Web app URL** ដែលបានចម្លងទុកពីចំណុចមុន។
4. នៅរបារឧបករណ៍ខាងលើ ជ្រើសរើសយក `setupWebhook` រួចចុចប៊ូតុង **Run**។
5. ពិនិត្យមើលផ្ទាំង Execution log នៅខាងក្រោម៖
   * ប្រសិនបើឃើញពាក្យ `"description": "Webhook was set"` មានន័យថា Bot របស់លោកអ្នកត្រូវបានភ្ជាប់ជោគជ័យ និងដំណើរការជាផ្លូវការហើយ!

*(បញ្ជាក់៖ Apps Script នេះនឹងទាញយក Telegram Bot Token ពី Tab `BotConfig` ដោយស្វ័យប្រវត្ត។ ដូចនេះ លោកអ្នកមិនបាច់បារម្ភពីការលេចធ្លាយ Token ឡើយ)*

