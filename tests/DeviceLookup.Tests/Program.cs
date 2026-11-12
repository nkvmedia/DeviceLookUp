using System.Net;
using System.Text;
using DeviceLookup.Core.Models;
using DeviceLookup.Core.Services;
using DeviceLookup.Core.ViewModels;

// Tiny test runner: no packages needed, so it also runs where nuget.org is blocked.
var failures = 0;
var passed = 0;
async Task Test(string name, Func<Task> body)
{
    try { await body(); passed++; Console.WriteLine($"  ok   {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"  FAIL {name}\n       {ex.Message}"); }
}
void True(bool cond, string msg) { if (!cond) throw new Exception(msg); }
void Eq<T>(T expected, T actual, string what = "") { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what} expected <{expected}> but got <{actual}>"); }

AppLogger.DataFolder = Path.Combine(Path.GetTempPath(), "dl-tests-" + Guid.NewGuid().ToString("N"));
Clock.Today = () => new DateOnly(2026, 11, 11);

// ---------- CSV ----------
Console.WriteLine("CSV");
await Test("semicolon file is detected", () =>
{
    var text = "Serial;Name\nPF4Q9WYF;Anna\nPC1VLN7L;Boris\n";
    Eq(';', CsvService.DetectDelimiter(text));
    var t = CsvService.Parse(text, ';');
    Eq(3, t.Count); Eq("PC1VLN7L", t[2][0]);
    return Task.CompletedTask;
});
await Test("quoted fields, doubled quotes and line breaks", () =>
{
    var t = CsvService.Parse("Serial,Note\nABC12345,\"He said \"\"hi\"\"\nsecond line\"\n", ',');
    Eq(2, t.Count); Eq("He said \"hi\"\nsecond line", t[1][1]);
    return Task.CompletedTask;
});
await Test("serial column found by several names", () =>
{
    Eq(2, CsvService.FindSerialColumn(new[] { "Name", "Dept", "Service Tag" }));
    Eq(0, CsvService.FindSerialColumn(new[] { "\uFEFFSerial Number" }.Select(h => h.TrimStart('\uFEFF')).ToArray()));
    Eq(-1, CsvService.FindSerialColumn(new[] { "Name", "Dept" }));
    return Task.CompletedTask;
});
await Test("import keeps original columns and export round-trips them", () =>
{
    var dir = Directory.CreateTempSubdirectory().FullName;
    var f = Path.Combine(dir, "in.csv");
    File.WriteAllText(f, "Employee;Serial\nAnna;pf4q9wyf\nBoris;PC1VLN7L\n;\n", new UTF8Encoding(true));
    var imp = CsvService.Import(f);
    Eq(2, imp.Rows.Count); Eq("PF4Q9WYF", imp.Rows[0].SerialNumber); Eq("Anna", imp.Rows[0].OriginalColumns["Employee"]);
    imp.Rows[0].Manufacturer = "Lenovo"; imp.Rows[0].Status = LookupStatus.Found;
    var outFile = Path.Combine(dir, "out.csv");
    CsvService.Export(imp.Rows, outFile);
    var text = File.ReadAllText(outFile);
    True(text.Contains("Employee"), "export should include the original Employee column");
    True(text.Contains("Anna"), "export should include original values");
    return Task.CompletedTask;
});
await Test("Windows-1251 file is read correctly", () =>
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var f = Path.Combine(Directory.CreateTempSubdirectory().FullName, "bg.csv");
    File.WriteAllBytes(f, Encoding.GetEncoding(1251).GetBytes("Serial;Име\nPF4Q9WYF;Иван\n"));
    var imp = CsvService.Import(f);
    Eq("Иван", imp.Rows[0].OriginalColumns["Име"]);
    return Task.CompletedTask;
});
await Test("export neutralises spreadsheet formulas", () =>
{
    var r = new DeviceResult { SerialNumber = "ABC12345", Product = "=HYPERLINK(\"x\")" };
    var f = Path.Combine(Directory.CreateTempSubdirectory().FullName, "o.csv");
    CsvService.Export(new[] { r }, f);
    True(File.ReadAllText(f).Contains("'=HYPERLINK"), "formula should be prefixed with a quote");
    return Task.CompletedTask;
});

// ---------- warranty maths ----------
Console.WriteLine("Warranty");
await Test("remaining text", () =>
{
    var today = new DateOnly(2026, 11, 11);
    Eq("1 year, 1 month (416 days)", DeviceResult.FormatRemaining(today, today.AddDays(416)));
    Eq("12 days", DeviceResult.FormatRemaining(today, today.AddDays(12)));
    Eq("Expired 10 days ago", DeviceResult.FormatRemaining(today, today.AddDays(-10)));
    return Task.CompletedTask;
});
await Test("state thresholds", () =>
{
    DeviceResult R(int d) => new() { WarrantyEnd = Clock.Today().AddDays(d) };
    Eq(WarrantyState.Active, R(200).WarrantyState); Eq(WarrantyState.ExpiringSoon, R(90).WarrantyState);
    Eq(WarrantyState.Expired, R(-1).WarrantyState); Eq(WarrantyState.Unknown, new DeviceResult().WarrantyState);
    Eq("Expires in 47 days", R(47).WarrantyLabel);
    return Task.CompletedTask;
});

// ---------- Lenovo ----------
Console.WriteLine("Lenovo");
const string ProductJson = "[{\"Id\":\"LAPTOPS-AND-NETBOOKS/THINKPAD-T-SERIES-LAPTOPS/THINKPAD-T14-GEN-3-TYPE-21AH-21AJ/21CF/21CF003TUS/PF4Q9WYF\",\"Name\":\"ThinkPad T14 Gen 3 (Type 21CF)\",\"Image\":\"//download.lenovo.com/img/t14.png\"}]";
const string WarrantyHtml = "<script>var x=1; window.ds_warranties = window.ds_warranties || {\"BaseWarranties\":[{\"Name\":\"1Y; Depot {base}\",\"Start\":\"2023-12-27\",\"End\":\"2024-12-26\"}],\"ContractWarranties\":[{\"Name\":\"3Y Depot, 9x5 2BD Warranty\",\"Start\":\"2023-12-27\",\"End\":\"2026-12-26\"}]}; var y=2;</script>";

await Test("brace-matched extraction survives ; and { } inside strings", () =>
{
    var json = LenovoProvider.ExtractJson(WarrantyHtml, "ds_warranties");
    True(json != null && json.EndsWith("}") && json.Contains("1Y; Depot {base}"), "json was cut wrongly: " + json);
    System.Text.Json.JsonDocument.Parse(json!);
    return Task.CompletedTask;
});
await Test("full lookup parses product, model, image and best warranty", async () =>
{
    var h = new FakeHandler(req =>
        req.RequestUri!.AbsolutePath.Contains("getproducts") ? Reply(ProductJson) : Reply(WarrantyHtml));
    var r = await new LenovoProvider(h, TimeSpan.FromMilliseconds(1)).LookupAsync("PF4Q9WYF", default);
    Eq(LookupStatus.Found, r.Status); Eq("ThinkPad T14 Gen 3", r.Product);
    Eq("21CF", r.Model); Eq("21CF003TUS", r.Mtm);
    Eq("https://download.lenovo.com/img/t14.png", r.ImageUrl);
    Eq(new DateOnly(2026, 12, 26), r.WarrantyEnd!.Value); Eq("3Y Depot, 9x5 2BD Warranty", r.WarrantyName);
});
await Test("retries a 503 and then succeeds", async () =>
{
    var calls = 0;
    var h = new FakeHandler(req =>
    {
        if (!req.RequestUri!.AbsolutePath.Contains("getproducts")) return Reply(WarrantyHtml);
        return ++calls < 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Reply(ProductJson);
    });
    var r = await new LenovoProvider(h, TimeSpan.FromMilliseconds(1)).LookupAsync("PF4Q9WYF", default);
    Eq(LookupStatus.Found, r.Status); Eq(3, calls, "calls");
});
await Test("empty list means not found, network failure means failed", async () =>
{
    var empty = new FakeHandler(_ => Reply("[]"));
    Eq(LookupStatus.NotFound, (await new LenovoProvider(empty, TimeSpan.FromMilliseconds(1)).LookupAsync("ZZZZ9999", default)).Status);
    var down = new FakeHandler(_ => throw new HttpRequestException("boom"));
    var r = await new LenovoProvider(down, TimeSpan.FromMilliseconds(1)).LookupAsync("PF4Q9WYF", default);
    Eq(LookupStatus.Failed, r.Status); True(r.Error.Length > 0, "should explain the failure");
});
await Test("warranty failure keeps the device as found", async () =>
{
    var h = new FakeHandler(req => req.RequestUri!.AbsolutePath.Contains("getproducts") ? Reply(ProductJson) : new HttpResponseMessage(HttpStatusCode.NotFound));
    var r = await new LenovoProvider(h, TimeSpan.FromMilliseconds(1)).LookupAsync("PF4Q9WYF", default);
    Eq(LookupStatus.Found, r.Status); Eq("Unavailable", r.WarrantyNote);
});
await Test("user cancel is not swallowed", async () =>
{
    using var cts = new CancellationTokenSource(); cts.Cancel();
    var h = new FakeHandler(_ => Reply(ProductJson));
    try { await new LenovoProvider(h).LookupAsync("PF4Q9WYF", cts.Token); throw new Exception("no exception"); }
    catch (OperationCanceledException) { }
});

// ---------- Dell ----------
Console.WriteLine("Dell");
const string DellJson = "[{\"serviceTag\":\"7K2M9P3\",\"productLineDescription\":\"LATITUDE 5440\",\"systemDescription\":\"Latitude 5440\",\"invalid\":false,\"entitlements\":[{\"startDate\":\"2023-05-02T00:00:00.000Z\",\"endDate\":\"2024-05-02T23:59:59.000Z\",\"serviceLevelDescription\":\"Basic Onsite\"},{\"startDate\":\"2024-05-03T00:00:00.000Z\",\"endDate\":\"2027-05-02T23:59:59.000Z\",\"serviceLevelDescription\":\"ProSupport Next Business Day\"}]}]";
var dellCreds = new AppSettings { DellClientId = "id", DellClientSecret = "secret" };
await Test("parses entitlements and picks the longest", () =>
{
    var r = new DeviceResult { SerialNumber = "7K2M9P3" };
    DellProvider.Parse(DellJson, r);
    Eq(LookupStatus.Found, r.Status); Eq("Latitude 5440", r.Product);
    Eq(new DateOnly(2027, 5, 2), r.WarrantyEnd!.Value); Eq("ProSupport Next Business Day", r.WarrantyName);
    return Task.CompletedTask;
});
await Test("invalid tag is not found", () =>
{
    var r = new DeviceResult();
    DellProvider.Parse("[{\"serviceTag\":\"XXXXXXX\",\"invalid\":true,\"entitlements\":[]}]", r);
    Eq(LookupStatus.NotFound, r.Status);
    return Task.CompletedTask;
});
await Test("token is requested once and reused", async () =>
{
    var tokenCalls = 0;
    var h = new FakeHandler(req =>
    {
        if (req.RequestUri!.AbsolutePath.Contains("oauth")) { tokenCalls++; return Reply("{\"access_token\":\"tok\",\"expires_in\":3600}"); }
        True(req.Headers.Authorization?.Parameter == "tok", "bearer token missing");
        return Reply(DellJson);
    });
    var p = new DellProvider(() => dellCreds, h, TimeSpan.FromMilliseconds(1));
    Eq(LookupStatus.Found, (await p.LookupAsync("7K2M9P3", default)).Status);
    Eq(LookupStatus.Found, (await p.LookupAsync("7K2M9P3", default)).Status);
    Eq(1, tokenCalls, "token calls");
});
await Test("bad credentials give a clear message", async () =>
{
    var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
    var r = await new DellProvider(() => dellCreds, h, TimeSpan.FromMilliseconds(1)).LookupAsync("7K2M9P3", default);
    Eq(LookupStatus.Failed, r.Status); True(r.Error.Contains("Client ID"), r.Error);
});
await Test("no credentials: clear failure, no network call", async () =>
{
    var h = new FakeHandler(_ => throw new Exception("must not be called"));
    var r = await new DellProvider(() => new AppSettings(), h).LookupAsync("7K2M9P3", default);
    Eq(LookupStatus.Failed, r.Status); True(r.Error.Contains("credentials"), r.Error);
});

// ---------- router ----------
Console.WriteLine("Router");
DeviceLookupService Router(FakeProvider l, FakeProvider d, AppSettings s, ResultCache? c = null) => new(l, d, () => s, c);
await Test("Auto: 7-char tag tries Dell first, 8-char tries Lenovo first", async () =>
{
    var l = new FakeProvider("Lenovo", LookupStatus.Found); var d = new FakeProvider("Dell", LookupStatus.Found);
    var s = new AppSettings { DellClientId = "a", DellClientSecret = "b" };
    Eq("Dell", (await Router(l, d, s).LookupAsync("7K2M9P3", VendorChoice.Auto, default)).Manufacturer);
    Eq("Lenovo", (await Router(l, d, s).LookupAsync("PF4Q9WYF", VendorChoice.Auto, default)).Manufacturer);
});
await Test("Auto falls back to the other vendor when the first says not found", async () =>
{
    var l = new FakeProvider("Lenovo", LookupStatus.NotFound); var d = new FakeProvider("Dell", LookupStatus.Found);
    var s = new AppSettings { DellClientId = "a", DellClientSecret = "b" };
    Eq("Dell", (await Router(l, d, s).LookupAsync("PF4Q9WYF", VendorChoice.Auto, default)).Manufacturer);
});
await Test("Dell tag without credentials explains what to do", async () =>
{
    var l = new FakeProvider("Lenovo", LookupStatus.NotFound); var d = new FakeProvider("Dell", LookupStatus.Found, configured: false);
    var r = await Router(l, d, new AppSettings()).LookupAsync("7K2M9P3", VendorChoice.Auto, default);
    Eq(LookupStatus.NotFound, r.Status); True(r.Error.Contains("Dell API credentials"), r.Error);
});
await Test("explicit Dell without credentials fails clearly", async () =>
{
    var r = await Router(new FakeProvider("Lenovo", LookupStatus.Found), new FakeProvider("Dell", LookupStatus.Found, configured: false), new AppSettings())
        .LookupAsync("7K2M9P3", VendorChoice.Dell, default);
    Eq(LookupStatus.Failed, r.Status);
});
await Test("invalid serial is rejected without a lookup", async () =>
{
    var l = new FakeProvider("Lenovo", LookupStatus.Found);
    var r = await Router(l, new FakeProvider("Dell", LookupStatus.Found), new AppSettings()).LookupAsync("  ", VendorChoice.Auto, default);
    Eq(LookupStatus.Failed, r.Status); Eq(0, l.Calls);
});
await Test("second lookup comes from the cache", async () =>
{
    var l = new FakeProvider("Lenovo", LookupStatus.Found);
    var cache = new ResultCache(Path.Combine(Directory.CreateTempSubdirectory().FullName, "c.json"));
    var svc = Router(l, new FakeProvider("Dell", LookupStatus.Found), new AppSettings(), cache);
    await svc.LookupAsync("PF4Q9WYF", VendorChoice.Auto, default);
    var again = await svc.LookupAsync("pf4q9wyf", VendorChoice.Auto, default);
    Eq(1, l.Calls, "provider calls"); Eq(LookupStatus.Found, again.Status);
    svc.FlushCache();
    var reloaded = new ResultCache(Path.Combine(Path.GetDirectoryName(Path.Combine(Directory.CreateTempSubdirectory().FullName, "x"))!, "none.json"));
    True(!reloaded.TryGet("PF4Q9WYF", 7, out _), "different file starts empty");
});

// ---------- view models ----------
Console.WriteLine("View models");
AppServices Services(ILookupService lookup, AppSettings? s = null) => new()
{
    Settings = s ?? new AppSettings(), Lookup = lookup, Platform = new FakePlatform(),
    Store = new SettingsStore(Path.Combine(Directory.CreateTempSubdirectory().FullName, "s.json"), new PlainProtector()),
    Updates = new NoUpdateService()
};
await Test("bulk run processes everything, counts and respects concurrency", async () =>
{
    var svc = new FakeLookup(delayMs: 20);
    var s = Services(svc, new AppSettings { ConcurrentLookups = 3 });
    var vm = new BulkViewModel(s);
    var rows = Enumerable.Range(1, 12).Select(i => new DeviceResult { SerialNumber = $"SN{i:000000}" }).ToList();
    rows[3].SerialNumber = "FAIL0004"; rows[5].SerialNumber = "NONE0006";
    vm.LoadRows(rows.Select(r => { r.Status = LookupStatus.Pending; return r; }), "test.csv");
    vm.StartCommand.Execute(null);
    for (var i = 0; i < 200 && (vm.IsRunning || vm.Progress < 100); i++) await Task.Delay(25);
    Eq(12, vm.FoundCount + vm.FailedCount + vm.NotFoundCount, "total"); Eq(1, vm.FailedCount, "failed"); Eq(1, vm.NotFoundCount, "not found");
    True(svc.MaxParallel <= 3, $"parallelism was {svc.MaxParallel}");
    vm.FilterIndex = 2; Eq(1, vm.VisibleRows.Count, "failed filter");
});
await Test("bulk: Retry Failed only reruns failed rows", async () =>
{
    var svc = new FakeLookup(delayMs: 1);
    var vm = new BulkViewModel(Services(svc));
    var rows = new[] { new DeviceResult { SerialNumber = "FAIL0001", Status = LookupStatus.Failed }, new DeviceResult { SerialNumber = "OKAY0002", Status = LookupStatus.Found } };
    vm.LoadRows(rows, "t.csv");
    svc.Calls = 0; svc.FailSerials.Clear();
    vm.RetryCommand.Execute(null);
    for (var i = 0; i < 100 && svc.Calls < 1; i++) await Task.Delay(10);
    await Task.Delay(100);
    Eq(1, svc.Calls, "calls"); Eq(LookupStatus.Found, rows[0].Status);
});
await Test("scan: duplicates ignored, counters update", async () =>
{
    var vm = new ScanViewModel(Services(new FakeLookup(1)));
    foreach (var code in new[] { "PF4Q9WYF", "PF4Q9WYF", "NONE0001", "OKAY0003" }) { vm.ScanText = code; vm.SubmitCommand.Execute(null); }
    for (var i = 0; i < 100 && vm.Scans.Any(r => r.Status == LookupStatus.Looking); i++) await Task.Delay(10);
    Eq(3, vm.ScannedCount, "scanned"); Eq(2, vm.FoundCount, "found"); Eq(1, vm.NotFoundCount, "not found");
    Eq("", vm.ScanText, "box cleared");
});
await Test("single lookup shows a result and toasts", async () =>
{
    var s = Services(new FakeLookup(1));
    var vm = new SingleLookupViewModel(s) { Serial = "pf4q9wyf" };
    vm.LookupCommand.Execute(null);
    for (var i = 0; i < 100 && vm.Result == null; i++) await Task.Delay(10);
    True(vm.HasResult && vm.Result!.Status == LookupStatus.Found, "no result");
    True(s.Toasts.Items.Count > 0, "no toast");
});
await Test("settings save secret encrypted, never in plain text", async () =>
{
    var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "s.json");
    var store = new SettingsStore(path, new PlainProtector());
    store.Save(new AppSettings { DellClientId = "id", DellClientSecret = "supersecret" });
    var raw = File.ReadAllText(path);
    True(!raw.Contains("supersecret"), "secret leaked into settings.json");
    Eq("supersecret", store.Load().DellClientSecret);
    await Task.CompletedTask;
});

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;

// ---------- helpers ----------
static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_fn(request));
    }
}

sealed class FakeProvider : ILookupProvider
{
    private readonly LookupStatus _status;
    public FakeProvider(string vendor, LookupStatus status, bool configured = true) { Vendor = vendor; _status = status; IsConfigured = configured; }
    public string Vendor { get; }
    public bool IsConfigured { get; }
    public int Calls;
    public Task<DeviceResult> LookupAsync(string serial, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(new DeviceResult { SerialNumber = serial, Manufacturer = Vendor, Product = "X", Status = _status });
    }
}

sealed class FakeLookup : ILookupService
{
    private readonly int _delay;
    private int _active;
    public int MaxParallel, Calls;
    public HashSet<string> FailSerials = new() { "FAIL0004", "FAIL0001" };
    public FakeLookup(int delayMs) => _delay = delayMs;
    public void FlushCache() { }
    public async Task<DeviceResult> LookupAsync(string serial, VendorChoice v, CancellationToken ct)
    {
        var now = Interlocked.Increment(ref _active);
        lock (this) MaxParallel = Math.Max(MaxParallel, now);
        Interlocked.Increment(ref Calls);
        try { await Task.Delay(_delay, ct); }
        finally { Interlocked.Decrement(ref _active); }
        var s = SerialNormalizer.Normalize(serial);
        var status = FailSerials.Contains(s) ? LookupStatus.Failed : s.StartsWith("NONE") ? LookupStatus.NotFound : LookupStatus.Found;
        return new DeviceResult { SerialNumber = s, Manufacturer = "Lenovo", Product = "Test", Status = status, Error = status == LookupStatus.Failed ? "boom" : "" };
    }
}

sealed class FakePlatform : IPlatform
{
    public string? PickOpenFile(string filter) => null;
    public string? PickSaveFile(string defaultName, string filter) => null;
    public void SetClipboard(string text) { }
    public void OpenPath(string pathOrUrl) { }
}

sealed class PlainProtector : ISecretProtector
{
    public string Protect(string plain) => Convert.ToBase64String(Encoding.UTF8.GetBytes("enc:" + plain));
    public string Unprotect(string t) => Encoding.UTF8.GetString(Convert.FromBase64String(t))[4..];
}
