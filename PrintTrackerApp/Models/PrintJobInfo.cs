using System;
using System.ComponentModel;

namespace PrintTrackerApp.Models
{
    public class PrintJobInfo : INotifyPropertyChanged
    {
        public string JobId { get; set; } = string.Empty;
        public string Timestamp { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        private string _documentName = string.Empty;
        public string DocumentName 
        { 
            get => _documentName; 
            set
            {
                if (_documentName != value)
                {
                    _documentName = value;
                    OnPropertyChanged(nameof(DocumentName));
                }
            }
        }
        private int _totalPages;
        public int TotalPages 
        { 
            get => _totalPages; 
            set
            {
                if (_totalPages != value)
                {
                    _totalPages = value;
                    OnPropertyChanged(nameof(TotalPages));
                }
            }
        }
        public string Owner { get; set; } = string.Empty;
        public string MachineName { get; set; } = Environment.MachineName;
        public string PrinterName { get; set; } = string.Empty;
        public int WebJobId { get; set; } = -1;
        public bool IsPdfPageCountAccurate { get; set; } = false;
        public bool IsInPrintPhase { get; set; } = false;
        public DateTime? SpoolerDeletedTime { get; set; } = null;

        private int _copies = 1;
        public int Copies 
        { 
            get => _copies; 
            set
            {
                if (_copies != value)
                {
                    _copies = value;
                    OnPropertyChanged(nameof(Copies));
                }
            }
        }

        private string _webFileName = string.Empty;
        public string WebFileName 
        { 
            get => _webFileName; 
            set
            {
                if (_webFileName != value)
                {
                    _webFileName = value;
                    OnPropertyChanged(nameof(WebFileName));
                }
            }
        }

        private string _ricohUserId = string.Empty;
        public string RicohUserId 
        { 
            get => _ricohUserId; 
            set
            {
                if (_ricohUserId != value)
                {
                    _ricohUserId = value;
                    OnPropertyChanged(nameof(RicohUserId));
                }
            }
        }
        
        private string _status = "Spooling...";
        public string Status 
        { 
            get => _status; 
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
