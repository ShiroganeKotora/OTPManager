namespace OtpManager;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // The self test opens no window and touches no stored data, so it must not be blocked by
        // the single-instance guard - otherwise it silently does nothing while the app is running.
        if(args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            SelfTest();
            return;
        }

        // A second instance would show stale codes beside the first and fight over the store.
        using var single = new Mutex(true, @"Local\OtpManager.SingleInstance", out var isFirst);
        if(!isFirst) return;

        Theme.Load(Settings.Current.Theme);

        ApplicationConfiguration.Initialize();

        // --tray starts minimised, which is what a Startup shortcut wants.
        var startHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        Application.Run(new TrayContext(startHidden));
    }

    /// <summary>Checks the generator against the RFC 6238 appendix B vectors.</summary>
    private static void SelfTest()
    {
        var secret = Base32.Decode("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
        (long Time, string Expected)[] vectors =
        [
            (59, "94287082"),
            (1111111109, "07081804"),
            (1111111111, "14050471"),
            (1234567890, "89005924"),
            (2000000000, "69279037"),
            (20000000000, "65353130"),
        ];

        var failed = 0;
        foreach(var (time, expected) in vectors)
        {
            var actual = Totp.Generate(secret, time, 30, 8, "SHA1");
            var ok = actual == expected;
            if(!ok) failed++;
            Console.WriteLine($"{(ok ? "OK  " : "FAIL")} T={time,-12} expected={expected} actual={actual}");
        }
        failed += QrRoundTrip();
        failed += MigrationParse();
        failed += DataUriRead();
        failed += GlyphRenders();
        failed += LayoutRepair();
        failed += BackupRoundTrip();

        Console.WriteLine(failed == 0 ? "all checks passed" : $"{failed} check(s) failed");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Feeds a deliberately broken list order through the repair pass: a member that drifted away
    /// from its heading, a heading that is missing entirely, and a token for an account that is gone.
    /// </summary>
    private static int LayoutRepair()
    {
        Account Make(string id, string group) => new() { Id = id, Name = id, Group = group };

        var accounts = new List<Account> { Make("a1", ""), Make("b1", "B"), Make("b2", "B"), Make("c1", "C") };
        var groups = new List<AccountGroup> { new() { Name = "B" }, new() { Name = "C" } };

        var broken = new List<string>
        {
            ListOrder.ForAccount(accounts[1]),   // a member ahead of its own heading
            ListOrder.ForGroup("B"),
            ListOrder.ForAccount(accounts[0]),   // an ungrouped account sitting between groups
            ListOrder.ForAccount(accounts[3]),   // a member of a heading that has not appeared yet
            "a:gone",                            // an account that no longer exists
        };

        var repaired = ListOrder.Repair(broken, accounts, groups);
        var expected = new List<string> { "g:B", "a:b1", "a:b2", "a:a1", "g:C", "a:c1" };

        var good = repaired.SequenceEqual(expected);
        Console.WriteLine(good ? "OK   layout repair" : $"FAIL layout repair: {string.Join(" ", repaired)}");
        return good ? 0 : 1;
    }

    /// <summary>Writes an encrypted backup to a temporary file and reads it back, then checks that
    /// the wrong passphrase is rejected rather than quietly returning something.</summary>
    private static int BackupRoundTrip()
    {
        var payload = new Backup.Payload
        {
            Accounts = [new Account { Id = "x1", Issuer = "Example", Name = "test", Secret = "JBSWY3DPEHPK3PXP", Group = "G" }],
            Groups = [new AccountGroup { Name = "G" }],
            Layout = ["g:G", "a:x1"],
        };

        var path = Path.Combine(Path.GetTempPath(), $"otpmanager-selftest-{Guid.NewGuid():N}.otpbak");
        try
        {
            Backup.Export(path, "correct horse battery", payload);
            var restored = Backup.Import(path, "correct horse battery");

            var good = restored.Accounts.Count == 1
                    && restored.Accounts[0].Secret == "JBSWY3DPEHPK3PXP"
                    && restored.Accounts[0].Group == "G"
                    && restored.Groups.Count == 1
                    && restored.Layout.SequenceEqual(payload.Layout);

            var rejected = false;
            try { Backup.Import(path, "wrong passphrase"); }
            catch(System.Security.Cryptography.CryptographicException) { rejected = true; }

            Console.WriteLine(good && rejected ? "OK   backup round trip"
                : good ? "FAIL backup: a wrong passphrase was accepted"
                : "FAIL backup: contents did not survive the round trip");
            return good && rejected ? 0 : 1;
        }
        catch(Exception ex)
        {
            Console.WriteLine($"FAIL backup: {ex.Message}");
            return 1;
        }
        finally
        {
            try { File.Delete(path); } catch(Exception) { }
        }
    }

    /// <summary>Encodes an otpauth URI to a QR image and reads it straight back.</summary>
    private static int QrRoundTrip()
    {
        const string uri = "otpauth://totp/Example:test@example.com?secret=JBSWY3DPEHPK3PXP"
                         + "&issuer=Example&algorithm=SHA1&digits=6&period=30";
        using var image = QrCode.Encode(uri, 320);
        var decoded = QrCode.Decode(image);

        if(decoded != uri)
        {
            Console.WriteLine($"FAIL qr round trip: {decoded ?? "(no code found)"}");
            return 1;
        }
        if(!Account.TryParseUri(decoded, out var account, out var error))
        {
            Console.WriteLine($"FAIL qr uri parse: {error}");
            return 1;
        }
        var good = account.Issuer == "Example" && account.Name == "test@example.com"
                && account.Digits == 6 && account.Period == 30 && account.Algorithm == "SHA1";
        Console.WriteLine(good ? "OK   qr round trip" : $"FAIL qr fields: {account.Issuer} / {account.Name}");
        return good ? 0 : 1;
    }

    /// <summary>Draws every embedded glyph and checks that each one actually puts ink down.</summary>
    private static int GlyphRenders()
    {
        if(!MaterialSymbols.Available || !OriGlyphs.Available)
        {
            Console.WriteLine("FAIL glyph font: an embedded font did not load");
            return 1;
        }

        var failed = 0;
        var glyphs = new[]
        {
            ("qr_code_2_add", MaterialSymbols.QrCodeAdd, MaterialSymbols.Get(32f)),
            ("qr_code", MaterialSymbols.QrCode, MaterialSymbols.Get(32f)),
            ("toc", MaterialSymbols.Toc, MaterialSymbols.Get(32f)),
            ("ad_group", MaterialSymbols.AdGroup, MaterialSymbols.Get(32f)),
            ("quick_reference", MaterialSymbols.QuickReference, MaterialSymbols.Get(32f)),
            ("settings_applications", MaterialSymbols.SettingsApplications, MaterialSymbols.Get(32f)),
            ("qr_code_2_edit", OriGlyphs.QrCodeEdit, OriGlyphs.Get(32f)),
        };
        foreach(var (name, glyph, font) in glyphs)
        {
            using var bitmap = new Bitmap(48, 48);
            using(var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                // GDI cannot see fonts added through PrivateFontCollection, so glyphs go through GDI+.
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph, font, Brushes.Black, 4, 4);
            }

            var inked = 0;
            for(var x = 0; x < bitmap.Width; x++)
                for(var y = 0; y < bitmap.Height; y++)
                    if(bitmap.GetPixel(x, y).R < 128) inked++;

            // A blank draw or a "missing glyph" box would both fall outside this range.
            var good = inked is > 60 and < 1600;
            Console.WriteLine(good ? $"OK   glyph {name} ({inked} px)" : $"FAIL glyph {name}: {inked} px");
            if(!good) failed++;
        }
        return failed;
    }

    /// <summary>Feeds a QR image back in as a data: URI, the way one arrives pasted as text.</summary>
    private static int DataUriRead()
    {
        const string uri = "otpauth://totp/Example:data@example.com?secret=JBSWY3DPEHPK3PXP&digits=6&period=30";

        string dataUri;
        using(var source = QrCode.Encode(uri, 320))
        using(var stream = new MemoryStream())
        {
            source.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            dataUri = "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }

        if(!QrCode.TryDecodeDataUri(dataUri, out var image))
        {
            Console.WriteLine("FAIL data uri: not recognised");
            return 1;
        }
        using(image)
        {
            var good = QrCode.Decode(image) == uri;
            Console.WriteLine(good ? "OK   data uri image" : "FAIL data uri: QR did not match");
            return good ? 0 : 1;
        }
    }

    /// <summary>Builds an otpauth-migration payload by hand and parses it back.</summary>
    private static int MigrationParse()
    {
        var parameters = new List<byte>();
        AddBytes(parameters, 1, Base32.Decode("JBSWY3DPEHPK3PXP"));
        AddBytes(parameters, 2, System.Text.Encoding.UTF8.GetBytes("test@example.com"));
        AddBytes(parameters, 3, System.Text.Encoding.UTF8.GetBytes("Example"));
        AddVarint(parameters, 4, 1);   // SHA1
        AddVarint(parameters, 5, 1);   // six digits
        AddVarint(parameters, 6, 2);   // TOTP

        var payload = new List<byte>();
        AddBytes(payload, 1, [.. parameters]);
        AddVarint(payload, 3, 2);      // batch size
        AddVarint(payload, 4, 0);      // batch index

        var uri = "otpauth-migration://offline?data=" + Uri.EscapeDataString(Convert.ToBase64String([.. payload]));
        var result = OtpMigration.Parse(uri);

        var account = result.Accounts.SingleOrDefault();
        var good = account != null
                && account.Secret == "JBSWY3DPEHPK3PXP"
                && account.Issuer == "Example"
                && account.Name == "test@example.com"
                && account.Digits == 6 && account.Period == 30 && account.Algorithm == "SHA1"
                && result.BatchCount == 2 && result.Batch == 1;
        Console.WriteLine(good ? "OK   migration payload" : "FAIL migration payload");
        return good ? 0 : 1;
    }

    private static void AddVarint(List<byte> buffer, int field, ulong value)
    {
        WriteVarint(buffer, (ulong)(field << 3));
        WriteVarint(buffer, value);
    }

    private static void AddBytes(List<byte> buffer, int field, byte[] value)
    {
        WriteVarint(buffer, (ulong)((field << 3) | 2));
        WriteVarint(buffer, (ulong)value.Length);
        buffer.AddRange(value);
    }

    private static void WriteVarint(List<byte> buffer, ulong value)
    {
        while(value >= 0x80)
        {
            buffer.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        buffer.Add((byte)value);
    }
}
