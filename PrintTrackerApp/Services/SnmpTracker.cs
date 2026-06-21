using System;
using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace PrintTrackerApp.Services
{
    public class SnmpTracker
    {
        private readonly string _ipAddress;
        private readonly string _community;
        
        // standard printer OIDs
        private const string OidPageCount = "1.3.6.1.2.1.43.10.2.1.4.1.1";
        private const string OidPrinterStatus = "1.3.6.1.2.1.25.3.5.1.1";

        public SnmpTracker(string ipAddress, string community = "public")
        {
            _ipAddress = ipAddress;
            _community = community;
        }

        public int GetTotalPageCount()
        {
            try
            {
                var result = Messenger.Get(VersionCode.V2,
                    new IPEndPoint(IPAddress.Parse(_ipAddress), 161),
                    new OctetString(_community),
                    new List<Variable> { new Variable(new ObjectIdentifier(OidPageCount)) },
                    5000);

                if (result.Count > 0 && result[0].Data.ToString() != "NoSuchObject")
                {
                    if (int.TryParse(result[0].Data.ToString(), out int count))
                        return count;
                }
            }
            catch (Exception)
            {
                // handle timeout or snmp errors
            }
            return -1;
        }

        public string GetPrinterStatus()
        {
            try
            {
                var result = Messenger.Get(VersionCode.V2,
                    new IPEndPoint(IPAddress.Parse(_ipAddress), 161),
                    new OctetString(_community),
                    new List<Variable> { new Variable(new ObjectIdentifier(OidPrinterStatus)) },
                    2000);

                if (result.Count > 0)
                {
                    // 1=other, 2=unknown, 3=idle, 4=printing, 5=warmup
                    string statusVal = result[0].Data.ToString();
                    return statusVal switch
                    {
                        "1" => "Other",
                        "2" => "Unknown",
                        "3" => "Idle",
                        "4" => "Printing",
                        "5" => "Warmup",
                        _ => "Unknown"
                    };
                }
            }
            catch (Exception)
            {
                return "Offline";
            }
            return "Unknown";
        }
    }
}
