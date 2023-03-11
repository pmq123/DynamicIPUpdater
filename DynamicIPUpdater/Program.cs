using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

const string rootApi = @"https://api.cloudflare.com/client/v4";
const string checkingIpSite = "https://api.ipify.org";

string domain = "";
string zoneApi = "";
string email = "";
string key = "";
int interval = 600000;

string strExeFilePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
string rootDir = System.IO.Path.GetDirectoryName(strExeFilePath);
string configFilePath = Path.Combine(rootDir, "config.json");
string savedIpFilePath = Path.Combine(rootDir, "saved-ip");
string logPath = Path.Combine(rootDir, "logs");

Log($@"Current Dir: {rootDir}" );

while (true)
{
    Log("Starting...");
    try
    {
        var config = GetConfig();
        if(config != null)
        {
            InitConfig(config);
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("X-Auth-Email", email);
                client.DefaultRequestHeaders.Add("X-Auth-Key", key);

                var needRetry = false;
                var currentIp = await GetExternalIpAsync();
                Log($@"currentIp: {currentIp}");
                IPAddress address;
                if (!string.IsNullOrWhiteSpace(currentIp) && IPAddress.TryParse(currentIp, out address))
                {
                    var isIpChanged = IsIpHasChanged(currentIp);
                    if (isIpChanged)
                    {
                        var zoneId = await GetZoneIdAsync(client);
                        var listDnsRecordToUpdate = await GetDnsRecordsByZoneIdAsync(client, zoneId);
                        foreach (var dnsRecord in listDnsRecordToUpdate)
                        {
                            Log($@"updated DNS record:{dnsRecord["name"]} from IP {dnsRecord["content"]} to IP {currentIp}");
                            var result = await UpdateDnsRecordAsync(client, dnsRecord, zoneId, currentIp);
                            if (result.StatusCode != HttpStatusCode.OK)
                            {
                                needRetry = true;
                                var erorMessage = await result.Content.ReadAsStringAsync();
                                Log("Error: " + erorMessage);
                            }
                        }
                        if (!needRetry)
                        {
                            SaveNewIp(currentIp);
                            Log("Update completed");
                        }
                        else
                        {
                            //Todo implement retry
                        }
                    }
                    else
                    {
                        Log("Nothing to do");
                    }
                }
                else
                {
                    Log($@"Can't get external ip address, look like something was wrong");
                }
            }
        }
        else
        {
            Log("Error: Missing config file");
        }
    }
    catch (Exception ex)
    {
        Log(ex.ToString());
    }
    Log("End !!!");
    Thread.Sleep(interval);
}

void InitConfig(JsonNode? config)
{
    domain = config["domain"].ToString();
    zoneApi = $@"https://api.cloudflare.com/client/v4/zones?name={domain}";
    email = config["email"].ToString();
    key = config["key"].ToString();
    interval = int.Parse(config["interval"].ToString());
}

JsonNode? GetConfig()
{
    if (!File.Exists(configFilePath))
        return null;
    var configText = File.ReadAllText(configFilePath);
    if (string.IsNullOrEmpty(configText))
        return null;
    var configJson = JsonSerializer.Deserialize<JsonNode>(configText);
    if (configJson == null || configJson["domain"] == null || configJson["email"] == null || configJson["key"] == null || configJson["interval"] == null)
        return null;
    return configJson;

}

async Task<HttpResponseMessage> UpdateDnsRecordAsync(HttpClient client, JsonNode? dnsRecord, string zoneId, string externalIp)
{
    var dnsId = dnsRecord?["id"];
    var updateDNSUrl = $@"{rootApi}/zones/{zoneId}/dns_records/{dnsId}";
    dnsRecord["content"] = externalIp;
    var json = JsonSerializer.Serialize(dnsRecord);
    var submitData = new StringContent(json, Encoding.UTF8, "application/json");
    var result = await client.PutAsync(updateDNSUrl, submitData);
    return result;
}

async Task<string> GetExternalIpAsync()
{
    var externalIp = "";
    using (var client = new HttpClient())
    {
        externalIp = await client.GetStringAsync(checkingIpSite);
    }
    return externalIp;
}

async Task<string> GetZoneIdAsync(HttpClient client)
{
    var zoneString = await client.GetAsync(zoneApi).Result.Content.ReadAsStringAsync();
    var zoneObject = JsonSerializer.Deserialize<JsonObject>(zoneString);
    //check success for fail later
    var zoneId = zoneObject?["result"]?.AsArray()?.First()?["id"];
    return zoneId.ToString();
}

async Task<List<JsonNode?>> GetDnsRecordsByZoneIdAsync(HttpClient client, string zoneId)
{
    var getListDNSRecordUrl = $@"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records";
    var listDNSString = await client.GetAsync(getListDNSRecordUrl).Result.Content.ReadAsStringAsync();
    var listDNS = JsonSerializer.Deserialize<JsonObject>(listDNSString);
    var primaryDnsRecord = listDNS?["result"]?.AsArray()?.First(x => x["name"].ToString() == domain);
    var oldIp = primaryDnsRecord["content"];
    var dnsRecords = listDNS?["result"]?.AsArray()?.Where(x => x["content"].ToString() == oldIp.ToString()).ToList();
    return dnsRecords;
}

bool IsIpHasChanged(string currentIp)
{
    var isIpChanged = true;
    if (File.Exists(savedIpFilePath))
    {
        var savedIp = File.ReadLines(savedIpFilePath).ToList().First();
        Log($@"last saved ip: {savedIp}");
        if (!string.IsNullOrWhiteSpace(savedIp))
        {
            if (savedIp == currentIp)
            {
                isIpChanged = false;
            }
        }
    }
    return isIpChanged;
}

void SaveNewIp(string currentIp)
{
    File.WriteAllText(savedIpFilePath, currentIp);
}

void Log(string message)
{
    Directory.CreateDirectory(logPath);
    var fileName = DateTime.Today.ToString("yyyy-MM-dd") + ".txt";
    var filePath = Path.Combine(logPath, fileName);
    File.AppendAllLines(filePath, new List<string>() { DateTime.Now + " - " + message });
}