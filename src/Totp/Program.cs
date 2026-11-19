using Totp;

try
{
    return Run(args);
}
catch (Exception ex) when (ex is FormatException or ArgumentException or UriFormatException or System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static int Run(string[] args)
{
    if (ConfigStore.EnsureExists())
        Console.Error.WriteLine($"Created config file: {ConfigStore.Path}");

    if (args.Length == 0) return Help(Console.Out, 0);

    return args[0] switch
    {
        "list" => List(args[1..]),
        "get" => Get(args[1..]),
        "add" => Add(args[1..]),
        "remove" => Remove(args[1..]),
        _ => UnknownCommand(args[0]),
    };
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.\n");
    return Help(Console.Error, 2);
}

/// <summary>Finds an app by name (exact, then unique prefix); reports not-found/ambiguous on stderr.</summary>
static AppEntry? Resolve(TotpConfig config, string name)
{
    var app = config.Find(name);
    if (app is null)
    {
        var matches = config.Sorted().Where(a => a.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).Select(a => a.Name).ToList();
        Console.Error.WriteLine(matches.Count > 1
            ? $"'{name}' is ambiguous: {string.Join(", ", matches)}"
            : $"App '{name}' not found. Run 'totp list' to see configured apps.");
    }
    return app;
}

static string CurrentCode(AppEntry app) =>
    TotpGenerator.Generate(Base32.Decode(app.Secret), DateTimeOffset.UtcNow, app.Period, app.Digits, app.Algorithm);

static int PrintCode(AppEntry app)
{
    var code = CurrentCode(app);
    var remaining = TotpGenerator.SecondsRemaining(DateTimeOffset.UtcNow, app.Period);

    // Code goes to stdout, extra info to stderr.
    Console.WriteLine(code);
    if (!Console.IsErrorRedirected)
        Console.Error.WriteLine($"{app.Name} · valid for {remaining}s");
    return 0;
}

static int AddFromQr(string input, string? nameOverride)
{
    var texts = input.StartsWith("otpauth", StringComparison.OrdinalIgnoreCase)
        ? [input]
        : QrReader.ReadAll(input);
    if (texts.Count == 0)
    {
        Console.Error.WriteLine($"No QR code found in '{input}'.");
        return 1;
    }

    var accounts = new List<ParsedAccount>();
    foreach (var text in texts)
    {
        try { accounts.AddRange(OtpAuthParser.Parse(text)); }
        catch (FormatException ex) when (texts.Count > 1) { Console.Error.WriteLine($"Skipped a QR code: {ex.Message}"); }
    }
    if (accounts.Count == 0) return 1;

    if (nameOverride is not null && accounts.Count > 1)
        return Usage("a custom name can only be given when the QR code has a single account");

    var config = ConfigStore.Load();
    int added = 0;
    foreach (var acc in accounts)
    {
        var label = acc.Account is null ? acc.DisplayName : $"{acc.DisplayName} ({acc.Account})";
        try { TotpGenerator.Generate(Base32.Decode(acc.Secret), DateTimeOffset.UtcNow, acc.Period, acc.Digits, acc.Algorithm); }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            Console.Error.WriteLine($"Skipped {label}: {ex.Message}");
            continue;
        }

        var duplicate = config.Apps.FirstOrDefault(a => a.Secret.Equals(acc.Secret, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            Console.Error.WriteLine($"Skipped {label}: already stored as '{duplicate.Name}'.");
            continue;
        }

        string name;
        if (nameOverride is not null)
        {
            if (config.NameTaken(nameOverride))
            {
                Console.Error.WriteLine($"App '{nameOverride}' already exists. Choose another name.");
                return 1;
            }
            name = nameOverride;
        }
        else
        {
            name = UniqueName(config, acc);
        }

        config.Apps.Add(new AppEntry
        {
            Name = name,
            Account = acc.Account,
            Secret = acc.Secret,
            Digits = acc.Digits,
            Period = acc.Period,
            Algorithm = acc.Algorithm,
        });
        added++;
        Console.WriteLine(acc.Account is null ? $"Added '{name}'." : $"Added '{name}' ({acc.Account}).");
    }

    if (added > 0) ConfigStore.Save(config);
    return added > 0 ? 0 : 1;
}

// "GitHub" → "GitHub:alice" → "GitHub 2"... so a second account for the same issuer doesn't clash.
static string UniqueName(TotpConfig config, ParsedAccount acc)
{
    var baseName = acc.DisplayName;
    if (!config.NameTaken(baseName)) return baseName;

    if (acc.Issuer is not null && acc.Account is not null)
    {
        var withAccount = $"{acc.Issuer}:{acc.Account}";
        if (!config.NameTaken(withAccount)) return withAccount;
    }

    for (int n = 2; ; n++)
        if (!config.NameTaken($"{baseName} {n}")) return $"{baseName} {n}";
}

static int Get(string[] args)
{
    var config = ConfigStore.Load();
    if (config.Apps.Count == 0)
    {
        Console.Error.WriteLine("No apps configured. Add one with: totp add <qr-image>");
        return 1;
    }

    var apps = config.Sorted();

    // Script mode: `totp get <app|index>` prints ONLY the code, e.g. `login --otp "$(totp get github)"`.
    // Words are joined so `totp get GitHub 2` works without quotes.
    if (args.Length > 0)
    {
        var choice = string.Join(' ', args);
        var app = config.Find(choice);
        if (app is null && int.TryParse(choice, out var index) && index >= 1 && index <= apps.Count)
            app = apps[index - 1];
        app ??= Resolve(config, choice); // reports not-found / ambiguous on stderr
        if (app is null) return 1;

        var code = CurrentCode(app);
        // No trailing newline when piped, so `totp get github | pbcopy` copies exactly the digits.
        if (Console.IsOutputRedirected) Console.Write(code);
        else Console.WriteLine(code);
        return 0;
    }

    int width = apps.Count.ToString().Length;
    for (int i = 0; i < apps.Count; i++)
    {
        var a = apps[i];
        var account = string.IsNullOrEmpty(a.Account) ? "" : $"  ({a.Account})";
        Console.Error.WriteLine($"  {(i + 1).ToString().PadLeft(width)}) {a.Name}{account}");
    }

    while (true)
    {
        Console.Error.Write($"Choose [1-{apps.Count}]: ");
        var line = Console.ReadLine();
        if (line is null || line.Trim() is "" or "q" or "quit") return 1; // EOF / Ctrl+D / empty = cancel

        if (Pick(line.Trim()) is { } chosen) return PrintCode(chosen);
    }

    AppEntry? Pick(string choice)
    {
        if (int.TryParse(choice, out var index))
        {
            if (index >= 1 && index <= apps.Count) return apps[index - 1];
            Console.Error.WriteLine($"Please enter a number between 1 and {apps.Count}.");
            return null;
        }

        var match = config.Find(choice);
        if (match is null) Console.Error.WriteLine($"No app matches '{choice}'.");
        return match;
    }
}

static int List(string[] args)
{
    bool show = args is ["--show"];
    if (args.Length > 0 && !show) return Usage("totp list [--show]");

    var config = ConfigStore.Load();
    if (config.Apps.Count == 0)
    {
        Console.WriteLine($"No apps configured. Add one with: totp add <qr-image>  or  totp add <name> <secret>\nConfig: {ConfigStore.Path}");
        return 0;
    }

    var now = DateTimeOffset.UtcNow;
    var apps = config.Sorted();

    // # matches the index accepted by `totp get <index>`.
    var rows = apps.Select((app, i) =>
    {
        string code = "";
        if (show)
        {
            try { code = TotpGenerator.Generate(Base32.Decode(app.Secret), now, app.Period, app.Digits, app.Algorithm); }
            catch (Exception ex) when (ex is FormatException or ArgumentException) { code = "invalid"; }
        }
        return new[] { (i + 1).ToString(), app.Name, app.Account ?? "", code, $"{TotpGenerator.SecondsRemaining(now, app.Period)}s" };
    }).ToList();

    string[] header = ["#", "NAME", "ACCOUNT", "CODE", "EXPIRES"];
    // Codes are hidden unless --show, so `totp list` is safe to run with someone looking at the screen.
    var columns = new List<int> { 0, 1 };
    if (rows.Any(r => r[2].Length > 0)) columns.Add(2);
    if (show) columns.AddRange([3, 4]);
    bool[] rightAlign = [true, false, false, false, true];
    var widths = Enumerable.Range(0, header.Length)
        .Select(c => rows.Select(r => r[c].Length).Append(header[c].Length).Max()).ToArray();

    string Format(string[] cells) => string.Join("  ",
        columns.Select(c => rightAlign[c] ? cells[c].PadLeft(widths[c]) : cells[c].PadRight(widths[c]))).TrimEnd();

    Console.WriteLine(Format(header));
    foreach (var row in rows) Console.WriteLine(Format(row));
    return 0;
}

static int Add(string[] args)
{
    const string usage = """
        totp add <qr-image | otpauth-uri> [name]
               totp add <name> <base32-secret> [--digits 6] [--period 30] [--algorithm SHA1|SHA256|SHA512] [--force]
        """;
    var positional = new List<string>();
    var entry = new AppEntry();
    bool hasOptions = false;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--digits" or "-d" when i + 1 < args.Length:
                hasOptions = true;
                entry.Digits = int.Parse(args[++i]);
                break;
            case "--period" or "-p" when i + 1 < args.Length:
                hasOptions = true;
                entry.Period = int.Parse(args[++i]);
                break;
            case "--algorithm" or "-a" when i + 1 < args.Length:
                hasOptions = true;
                entry.Algorithm = args[++i].ToUpperInvariant();
                break;
            case "--force" or "-f":
                hasOptions = true;
                positional.Add("--force");
                break;
            default:
                positional.Add(args[i]);
                break;
        }
    }

    bool force = positional.Remove("--force");

    // `add <image|uri> [name]` vs `add <name> <secret>`: one argument, an otpauth URI,
    // or an existing file means QR import.
    bool isQr = positional.Count == 1
        || (positional.Count == 2 && (positional[0].StartsWith("otpauth", StringComparison.OrdinalIgnoreCase) || File.Exists(positional[0])));
    if (isQr)
    {
        if (hasOptions)
        {
            Console.Error.WriteLine("Error: --digits/--period/--algorithm/--force only apply to `totp add <name> <secret>`; QR codes carry their own settings.");
            return 2;
        }
        return AddFromQr(positional[0], positional.Count == 2 ? positional[1] : null);
    }
    if (positional.Count != 2)
        return Usage(usage);

    entry.Name = positional[0];
    entry.Secret = positional[1].Replace(" ", "").ToUpperInvariant();

    if (entry.Digits is < 6 or > 10) throw new ArgumentException("Digits must be between 6 and 10.");
    if (entry.Period <= 0) throw new ArgumentException("Period must be positive.");
    // Validate secret + algorithm by generating once.
    TotpGenerator.Generate(Base32.Decode(entry.Secret), DateTimeOffset.UtcNow, entry.Period, entry.Digits, entry.Algorithm);

    var config = ConfigStore.Load();
    var existing = config.Apps.FirstOrDefault(a => a.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
    if (existing is not null)
    {
        if (!force)
        {
            Console.Error.WriteLine($"App '{existing.Name}' already exists. Use --force to overwrite.");
            return 1;
        }
        config.Apps.Remove(existing);
    }

    config.Apps.Add(entry);
    ConfigStore.Save(config);
    Console.WriteLine($"Added '{entry.Name}'.");
    return 0;
}

static int Remove(string[] args)
{
    if (args.Length != 1) return Usage("totp remove <name>");

    var config = ConfigStore.Load();
    var removed = config.Apps.RemoveAll(a => a.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));
    if (removed == 0)
    {
        Console.Error.WriteLine($"App '{args[0]}' not found.");
        return 1;
    }

    ConfigStore.Save(config);
    Console.WriteLine($"Removed '{args[0]}'.");
    return 0;
}

static int Usage(string usage)
{
    Console.Error.WriteLine($"Usage: {usage}");
    return 2;
}

static int Help(TextWriter output, int exitCode)
{
    output.WriteLine($"""
        totp - time-based one-time password generator

        Usage:
          totp list                          List apps (number, name, account)
          totp list --show                   Also show each app's current code and expiry
          totp get                           Pick an app from a numbered menu and print its code
          totp get <app|index>               Print ONLY the code (for scripts: "$(totp get github)")
                                             <app> is case-insensitive; a unique prefix is enough
          totp add <qr-image> [name]         Add app(s) from a QR code image (PNG/JPEG/WebP/...)
                                             Also accepts an otpauth:// URI, and Google Authenticator
                                             export QR codes (otpauth-migration://) with many accounts
          totp add <app> <secret> [options]  Add an app from a Base32 secret
              --digits, -d <n>               Code length (default 6)
              --period, -p <sec>             Time step in seconds (default 30)
              --algorithm, -a <alg>          SHA1 | SHA256 | SHA512 (default SHA1)
              --force, -f                    Overwrite an existing app
          totp remove <app>                  Remove an app

        Config file: {ConfigStore.Path}
        Override with the TOTP_CONFIG environment variable.
        """);
    return exitCode;
}
