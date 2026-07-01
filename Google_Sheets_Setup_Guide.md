# ការណែនាំអំពីការភ្ជាប់ Google Sheets ជាមួយកម្មវិធី Print Tracker

ដើម្បីឲ្យកម្មវិធីអាចបញ្ជូនទិន្នន័យទៅកាន់ Google Sheets ដោយស្វ័យប្រវត្តិ លោកអ្នកត្រូវមានគណនី Google Cloud Service Account ព្រមទាំងកំណត់ Spreadsheet ID ក្នុង Settings។ សូមធ្វើតាមការណែនាំខាងក្រោមជាជំហានៗ៖

## ជំហានទី ១៖ បង្កើត Service Account នៅក្នុង Google Cloud
1. ចូលទៅកាន់ [Google Cloud Console](https://console.cloud.google.com/) រួចចូលគណនី Gmail របស់លោកអ្នក។
2. បង្កើត Project ថ្មីមួយ (ចុចលើតំណភ្ជាប់ **Select a project** នៅផ្នែកខាងលើ រួចចុច **New Project**)។
3. ដាក់ឈ្មោះ Project ណាមួយ (ឧ. Print Tracker) ហើយចុច **Create**។
4. នៅក្នុង Project ថ្មីនោះ សូមចូលទៅកាន់ **APIs & Services** > **Library**។
5. ស្វែងរកពាក្យថា **Google Sheets API** ជ្រើសរើសវា ហើយចុច **Enable**។
6. បន្ទាប់ពី Enable រួចរាល់ សូមចូលទៅកាន់ **APIs & Services** > **Credentials**។
7. ចុចលើប៊ូតុង **+ CREATE CREDENTIALS** នៅខាងលើ ហើយជ្រើសរើស **Service Account**។
8. បំពេញឈ្មោះ Service account (ឧ. print-tracker-bot) ហើយចុច **Create and Continue** រហូតដល់ចប់ ចុច **Done**។

## ជំហានទី ២៖ ទាញយកឯកសារ Credentials (google_credentials.json)
1. នៅក្នុងទំព័រ **Credentials** ត្រង់ផ្នែក **Service Accounts** សូមចុចលើ Email របស់ Service account ដែលទើបនឹងបង្កើត។
2. ចូលទៅកាន់ផ្ទាំង **KEYS**។
3. ចុចលើ **ADD KEY** > **Create new key**។
4. ជ្រើសរើសប្រភេទ **JSON** ហើយចុច **Create**។ វានឹងទាញយកឯកសារ JSON មួយមកកុំព្យូទ័រលោកអ្នក។
5. **ប្តូរឈ្មោះឯកសារនោះ** ទៅជា `google_credentials.json` (បើមិនប្តូរក៏បាន)។
6. **បើកកម្មវិធី Print Tracker ចូលទៅកាន់ Settings > Google Sheets Integration > ចុចប៊ូតុង "Upload Credentials JSON" ហើយជ្រើសរើសឯកសារ JSON ដែលទើបនឹងទាញយកនោះ។** (វានឹងត្រូវបានរក្សាទុកយ៉ាងមានសុវត្ថិភាពក្នុង AppData មិនជាប់ក្នុង Github ឡើយ)។

## ជំហានទី ៣៖ ការ Share ឯកសារ Google Sheet ទៅកាន់ Service Account
1. បើកឯកសារ `google_credentials.json` ដោយប្រើ Notepad ហើយចម្លងយកអាសយដ្ឋាន Email ដែលមានត្រង់អថេរ `"client_email"` (ឧទាហរណ៍៖ `print-tracker-bot@project-name.iam.gserviceaccount.com`)។
2. បើកឯកសារ Google Sheets របស់លោកអ្នក ដែលចង់ឲ្យកម្មវិធីបញ្ជូនទិន្នន័យចូល។
3. ចុចប៊ូតុង **Share** នៅខាងស្តាំផ្នែកខាងលើ។
4. Paste (បិទភ្ជាប់) អាសយដ្ឋាន Email នោះ ហើយជ្រើសរើសសិទ្ធិជា **Editor** រួចចុច Share។

## ជំហានទី ៤៖ កំណត់ Spreadsheet ID ក្នុងកម្មវិធី
1. មើលតំណភ្ជាប់ (URL) របស់ឯកសារ Google Sheets នោះ។ វាមានទម្រង់បែបនេះ៖ 
   `https://docs.google.com/spreadsheets/d/`**`1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgvE2upms`**`/edit`
2. **ចំលងយកលេខកូដវែងនោះ** (Spreadsheet ID)។
3. បើកកម្មវិធី Print Tracker ចូលទៅកាន់ **Settings** (ការកំណត់)។
4. នៅក្នុងផ្នែក "Google Sheets Integration" សូមបញ្ចូល Spreadsheet ID ដែលបានចំលង ហើយចុច **Save Settings**។

## រួចរាល់! 
ឥឡូវនេះ លោកអ្នកគ្រាន់តែចុចប៊ូតុង **Export to Google Sheets** នោះទិន្នន័យរបស់តារាង PT, FT និង KH នឹងរត់ចូលទៅកាន់ Google Sheets ភ្លាមៗ!
*(បញ្ជាក់៖ សូមប្រាកដថា Google Sheets របស់លោកអ្នកមិនទាន់មានទិន្នន័យសំខាន់ក្នុង Tab `FT`, `PT`, `KH` ទេ ព្រោះកម្មវិធីនឹងលុបទិន្នន័យចាស់ចេញ ហើយសរសេរទិន្នន័យថ្មីចូលជាន់ពីលើ។ ប្រសិនបើមិនទាន់មាន Tab ទាំងនោះទេ Google Sheets អាចនឹងទាមទារឲ្យបង្កើត Tab ឈ្មោះ `FT`, `PT`, `KH` ជាមុនសិន។)*
