using DeviceLookup.Core.Models;

namespace DeviceLookup.Core.ViewModels;

/// <summary>Sample data for the screenshot mode, so every screen can be compared to the design.</summary>
public static class DemoData
{
    public static DeviceResult T14() => new()
    {
        SerialNumber = "PF4Q9WYF", Manufacturer = "Lenovo", Product = "ThinkPad T14 Gen 3",
        Model = "21CF", Mtm = "21CF003TUS", WarrantyName = "3Y Depot, 9x5 2BD Warranty",
        WarrantyStart = Clock.Today().AddYears(-2).AddMonths(-11), WarrantyEnd = Clock.Today().AddYears(1).AddMonths(1),
        Status = LookupStatus.Found, LookupTime = DateTime.Now, Duration = TimeSpan.FromSeconds(1.8)
    };

    public static List<DeviceResult> BulkRows()
    {
        DeviceResult Row(string s, string m, string p, string mod, string mtm, int days, LookupStatus st = LookupStatus.Found) => new()
        {
            SerialNumber = s, Manufacturer = m, Product = p, Model = mod, Mtm = mtm, Status = st,
            WarrantyEnd = st == LookupStatus.Found ? Clock.Today().AddDays(days) : null,
            Error = st == LookupStatus.NotFound ? "Service tag not found." : ""
        };
        return new List<DeviceResult>
        {
            Row("PF4Q9WYF", "Lenovo", "ThinkPad T14 Gen 3", "21CF", "21CF003TUS", 416),
            Row("PC1VLN7L", "Lenovo", "ThinkPad T14s Gen 2", "20T1", "20T1SSAG00", -120),
            Row("PF42TF4Y", "Lenovo", "ThinkPad T14 Gen 3", "21AJ", "21AJS3T400", -45),
            Row("B2CCYK4", "Dell", "", "", "", 0, LookupStatus.NotFound),
            Row("8BSOF1028", "Dell", "OptiPlex 7010", "OptiPlex 7010", "", 200),
            Row("PF39SP5B", "Lenovo", "ThinkPad T14 Gen 1", "20S1", "20S1SHEE00", -400),
            Row("PF39SVG8", "Lenovo", "ThinkPad T14s Gen 1", "20T1", "20T1SHEE00", 47),
            Row("PC201P0X", "Lenovo", "ThinkPad T14 Gen 1", "20S1", "20S1SHEE00", -300),
        };
    }

    public static List<DeviceResult> ScanRows()
    {
        var now = DateTime.Now;
        var list = BulkRows();
        for (var i = 0; i < list.Count; i++) list[i].LookupTime = now.AddMinutes(-i);
        return list;
    }
}
