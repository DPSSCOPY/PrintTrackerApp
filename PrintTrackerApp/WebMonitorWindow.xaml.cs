using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace PrintTrackerApp
{
    public partial class WebMonitorWindow : Window
    {
        // TODO: Replace with actual username and password provided by the user
        private const string Username = "admin";
        private const string Password = "";
        private string _printerIp;
        private readonly DispatcherTimer _pollTimer;

        public event EventHandler<string>? OnScrapedStatusReceived;

        public WebMonitorWindow(string printerIp, int refreshIntervalSeconds = 3)
        {
            InitializeComponent();
            _printerIp = printerIp;
            int seconds = refreshIntervalSeconds > 0 ? refreshIntervalSeconds : 3;
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            _pollTimer.Tick += PollTimer_Tick;
            InitializeAsync();
        }

        private async void PollTimer_Tick(object? sender, EventArgs e)
        {
            if (webView.CoreWebView2 != null)
            {
                string scrapeScript = @"
                    (function() {
                        function getDocument(win) {
                            try { return win.document; } catch(e) { return null; }
                        }
                        
                        function scrapeWindow(win) {
                            var doc = getDocument(win);
                            if (!doc) return '';
                            
                            try {
                                var trs = doc.querySelectorAll('tr');
                                var results = [];
                                for (var i = 0; i < trs.length; i++) {
                                    var tds = trs[i].querySelectorAll('td');
                                    // The job history table has 7 columns: ID, User Name, User ID, File Name, Status, Created At, Page(s)
                                    if (tds.length >= 6) {
                                        var idText = tds[0].innerText.trim();
                                        var userIdText = tds[2].innerText.trim();
                                        var fileNameText = tds[3].innerText.trim();
                                        var statusText = tds[4].innerText.trim();
                                        var createdAtText = tds[5] ? tds[5].innerText.trim() : '';
                                        var pagesText = tds[6] ? tds[6].innerText.trim() : '0';
                                        // Check if ID is a valid number to confirm it's a data row
                                        if (!isNaN(parseInt(idText)) && statusText) {
                                            results.push(idText + '|' + fileNameText + '|' + statusText + '|' + userIdText + '|' + pagesText + '|' + createdAtText);
                                        }
                                    }
                                }
                                
                                if (results.length > 0) {
                                    var dataString = results.join(';');
                                    
                                    // 2. Try to maximize 'Display Items' dropdown to show more jobs (Ricoh usually has 5 to 20)
                                    var selects = doc.querySelectorAll('select');
                                    var changedDropdown = false;
                                    for (var k = 0; k < selects.length; k++) {
                                        var opts = selects[k].options;
                                        if (opts && opts.length >= 2) {
                                            var hasTen = false;
                                            var hasTwenty = false;
                                            for(var o = 0; o < opts.length; o++) {
                                                if (opts[o].text.includes('10')) hasTen = true;
                                                if (opts[o].text.includes('20')) hasTwenty = true;
                                            }
                                            if (hasTen && hasTwenty) {
                                                var lastIdx = opts.length - 1;
                                                if (selects[k].selectedIndex !== lastIdx) {
                                                    selects[k].selectedIndex = lastIdx;
                                                    selects[k].dispatchEvent(new Event('change'));
                                                    if (typeof selects[k].onchange === 'function') selects[k].onchange();
                                                    changedDropdown = true;
                                                }
                                                break;
                                            }
                                        }
                                    }

                                    // 3. Handle Pagination & Refresh if dropdown was not changed
                                    if (!changedDropdown) {
                                        var elements = doc.querySelectorAll('a, input, button, img');
                                        var nextBtn = null;
                                        var firstBtn = null;
                                        var refreshBtn = null;
                                        
                                        for (var k = 0; k < elements.length; k++) {
                                            var el = elements[k];
                                            var title = (el.title || el.alt || '').toLowerCase();
                                            var src = (el.src || '').toLowerCase();
                                            var text = (el.innerText || el.value || '').toLowerCase();
                                            var isDisabled = src.includes('_d.gif') || src.includes('dis.gif') || src.includes('disable') || el.disabled;
                                            
                                            // Find the actual clickable container (A, BUTTON, or INPUT) by walking up the DOM tree
                                            var clickableEl = el;
                                            var curr = el;
                                            while (curr && curr.tagName !== 'BODY') {
                                                if (curr.tagName === 'A' || curr.tagName === 'BUTTON' || curr.tagName === 'INPUT' || typeof curr.onclick === 'function') {
                                                    clickableEl = curr;
                                                    break;
                                                }
                                                curr = curr.parentElement;
                                            }
                                            
                                            if (!isDisabled && clickableEl) {
                                                if (title.includes('next') || src.includes('next') || src.includes('forward') || title.includes('forward') || text === '>') nextBtn = clickableEl;
                                                if (title.includes('first') || src.includes('first') || src.includes('top') || title.includes('top') || text === '|<<') firstBtn = clickableEl;
                                                if (text.includes('refresh') || title.includes('refresh') || src.includes('refresh')) refreshBtn = clickableEl;
                                            }
                                        }
                                        
                                        function triggerClick(btn) {
                                            if (!btn) return;
                                            if (btn.href && btn.href.toLowerCase().indexOf('javascript:') === 0) {
                                                var code = btn.href.substring(11);
                                                try { win.eval(code); } catch(e) { btn.click(); }
                                            } else {
                                                btn.click();
                                            }
                                        }

                                        if (nextBtn) {
                                            triggerClick(nextBtn);
                                        } else if (firstBtn) {
                                            triggerClick(firstBtn);
                                        } else if (refreshBtn) {
                                            triggerClick(refreshBtn);
                                        }
                                    }
                                    return dataString; // Successfully found and processed the data frame
                                }
                            } catch(e) {}
                            
                            try {
                                for (var i = 0; i < win.frames.length; i++) {
                                    var res = scrapeWindow(win.frames[i]);
                                    if (res) return res;
                                }
                            } catch(e) {}
                            
                            return '';
                        }
                        
                        return scrapeWindow(window);
                    })();
                ";
                
                try 
                {
                    string result = await webView.CoreWebView2.ExecuteScriptAsync(scrapeScript);
                    
                    if (!string.IsNullOrEmpty(result) && result != "null" && result != "\"\"")
                    {
                        result = result.Trim('\"');
                        if (!string.IsNullOrEmpty(result))
                        {
                            OnScrapedStatusReceived?.Invoke(this, result);
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore errors silently for stability
                }
            }
        }

        private async void InitializeAsync()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PrintTrackerWebView2"));
                await webView.EnsureCoreWebView2Async(env);
                
                webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                webView.CoreWebView2.Navigate($"http://{_printerIp}/");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to initialize WebView2: " + ex.Message);
            }
        }

        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                // We are on a page. Depending on the URL or DOM, we can inject login scripts or scraping scripts.
                // NOTE: The exact DOM element IDs depend on Ricoh's Web Image Monitor structure.
                // We will attempt to login automatically if we find the login form.
                
                string currentUrl = webView.Source.ToString();
                Debug.WriteLine($"Navigated to: {currentUrl}");

                // Basic example of injecting login (IDs like 'userid' and 'password' might need adjustment)
                string loginScript = $@"
                    if (document.getElementById('userid') && document.getElementById('password')) {{
                        document.getElementById('userid').value = '{Username}';
                        document.getElementById('password').value = '{Password}';
                        // Add auto submit if button exists: document.getElementById('loginBtn').click();
                    }}
                ";
                await webView.CoreWebView2.ExecuteScriptAsync(loginScript);

                // Start polling status
                _pollTimer.Start();
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Navigate($"http://{_printerIp}/");
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide(); // Hide instead of close so we keep background session
        }
    }
}
