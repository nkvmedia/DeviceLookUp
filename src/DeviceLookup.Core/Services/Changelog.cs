namespace DeviceLookup.Core.Services;

public static class Changelog
{
    public const string Text =
@"1.1.0
 • Full redesign: sidebar, header, cards and notifications
 • Dell support through the TechDirect API (add credentials in Settings)
 • Product picture on results, cached on disk
 • Live progress while bulk lookups run, with a Stop button
 • Retries with back-off for timeouts and busy servers
 • CSV import now handles semicolon files, quoted line breaks and Windows-1251
 • CSV export keeps your original columns
 • Dell secret stored encrypted
 • Portable build with automatic updates

1.0.1
 • WPF version with Lenovo lookup, bulk CSV and scan mode";
}
