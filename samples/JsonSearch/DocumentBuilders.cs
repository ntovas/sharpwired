// Handwired source: src/json-search/documents.ts (the word lists, the builders of the 7 shapes, someKeys,
// sentence, time, hexId and person). Each builder makes its random choices in the order of documents.ts. It
// makes the members of an object in the order of the object literal, and a list item after item. `size` in 0..1
// scales the number of fields and items of a document.

using System.Globalization;

namespace JsonSearch;

internal static class DocumentBuilders
{
    private static readonly string[] FirstNames = ["Ada", "Grace", "Linus", "Margaret", "Dennis", "Barbara", "Ken", "Radia", "Zoë", "José", "Søren", "Chloé", "Yuki", "Amara", "Tomás", "美咲"];
    private static readonly string[] LastNames = ["Lovelace", "Hopper", "Hamilton", "Ritchie", "Liskov", "Perlman", "Okafor", "Souza", "Nakamura", "García", "Müller", "Kowalski", "O'Brien", "李"];
    private static readonly string[] Cities = ["Lisbon", "Osaka", "Tromsø", "Quito", "Hobart", "Accra", "Tallinn", "Muscat", "São Paulo", "Kraków", "Reykjavík", "東京"];
    private static readonly string[] Countries = ["PT", "JP", "NO", "EC", "AU", "GH", "EE", "OM", "BR", "PL", "IS"];
    private static readonly string[] Streets = ["Rua das Flores", "Harbor Rd", "Elm Street", "Nørregade", "Calle 8", "Station Lane", "Av. Paulista"];
    private static readonly string[] Words = "the harbor is quiet and an old ferry waits at the pier while gulls argue over something that washed up overnight a few early walkers pass the shuttered stalls on the way to an office or a train some stop to watch the tide".Split(' ');
    private static readonly string[] PackageWords = ["fast", "tiny", "json", "parse", "stream", "query", "cache", "router", "logger", "schema", "lint", "fetch", "queue", "state", "config", "glob"];
    private static readonly string[] Scopes = ["acme", "northwind", "harbor", "types", "tools"];
    private static readonly string[] Licenses = ["MIT", "Apache-2.0", "ISC", "BSD-3-Clause", "MPL-2.0"];
    private static readonly (string Name, string Command)[] Scripts =
        [("build", "tsc -p ."), ("test", "bun test"), ("lint", "oxlint --type-aware src"), ("start", "node dist/index.js"), ("dev", "bun --watch src/main.ts"), ("clean", "del-cli dist"), ("format", "prettier --write \"src/**/*.ts\"")];
    private static readonly string[] Roles = ["admin", "editor", "viewer", "billing", "support", "owner"];
    private static readonly string[] Levels = ["debug", "info", "warn", "error", "fatal"];
    private static readonly string[] Services = ["checkout", "auth", "search", "inventory", "mailer", "gateway", "thumbnailer"];
    private static readonly string[] Messages = ["request completed", "retrying after timeout", "cache miss for key \"user:42\"", "upstream returned 502", "payment declined: insufficient funds", "connection reset by peer", "slow query on table orders", "token expired, refreshing", "wrote 3 files to C:\\temp\\out", "line 1\nline 2"];
    private static readonly string[] Tags = ["urgent", "draft", "billing", "ops", "mobile", "web", "beta", "internal", "archived", "eu-west", "canary"];
    private static readonly string[] Themes = ["Solarized Dark", "Solarized Light", "Monokai", "GitHub Light", "Nord", "Gruvbox"];
    private static readonly string[] Products = ["Ceramic mug", "Linen apron", "Desk lamp", "Notebook, ruled", "Olive oil 500 ml", "USB-C cable", "Tea \"Earl Grey\"", "Café filter"];
    private static readonly string[] Currencies = ["EUR", "USD", "JPY", "BRL", "NOK"];
    private static readonly string[] Sensors = ["th-04", "co2-kitchen", "door-north", "pm25-roof", "soil-07"];
    private static readonly string[] Units = ["°C", "ppm", "open", "µg/m³", "%"];

    private static readonly (string Key, Func<DocumentRandom, JsonValue> Make)[] SettingEntries =
    [
        ("editor.fontSize", random => random.Integer(11, 18)),
        ("editor.tabSize", random => random.Pick<double>([2, 4, 8])),
        ("editor.wordWrap", random => random.Pick(["on", "off", "bounded"])),
        ("editor.formatOnSave", random => random.Chance(0.5)),
        ("editor.rulers", random => random.Pick<JsonValue>([new JsonArray(80), new JsonArray(100, 120), new JsonArray()])),
        ("editor.lineHeight", random => random.Pick([1.4, 1.5, 1.6, 0])),
        ("workbench.colorTheme", random => random.Pick(Themes)),
        ("window.zoomLevel", random => random.Integer(-2, 2)),
        ("files.exclude", random => new JsonObject().Set("**/.git", true).Set("**/node_modules", random.Chance(0.7)).Set("**/*.log", random.Chance(0.5))),
        ("files.autoSave", random => random.Pick(["afterDelay", "onFocusChange", "off"])),
        ("terminal.shell", random => random.Pick(["/bin/zsh", "/usr/bin/fish", "C:\\Windows\\System32\\cmd.exe"])),
        ("telemetry.enabled", _ => false),
        ("search.followSymlinks", random => random.Chance(0.5)),
        ("git.autofetchPeriod", random => random.Pick<JsonValue>([180, 300, JsonNull.Instance])),
        ("http.proxy", random => random.Pick<JsonValue>([JsonNull.Instance, "http://proxy.example:8080"])),
    ];

    public static JsonValue Build(DocumentShape shape, DocumentRandom random, double size) => shape switch
    {
        DocumentShape.PackageManifest => PackageManifest(random, size),
        DocumentShape.UserRecord => UserRecord(random, size),
        DocumentShape.LogEvent => LogEvent(random, size),
        DocumentShape.Settings => Settings(random, size),
        DocumentShape.Order => Order(random, size),
        DocumentShape.MapFeature => MapFeature(random, size),
        _ => SensorReadings(random, size),
    };

    // Math.round of JavaScript: the nearest integer, and the integer above at a half.
    private static double Round(double value)
    {
        var floor = Math.Floor(value);
        return value - floor >= 0.5 ? floor + 1 : floor;
    }

    private static int RoundCount(double value) => (int)Round(value);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Two(int value) => value.ToString("00", CultureInfo.InvariantCulture);

    private static string Sentence(DocumentRandom random, int wordCount) =>
        string.Join(' ', Enumerable.Range(0, wordCount).Select(_ => random.Pick(Words)).ToArray());

    private static string Time(DocumentRandom random)
    {
        var month = Two(random.Integer(1, 12));
        var day = Two(random.Integer(1, 28));
        var hour = Two(random.Integer(0, 23));
        var minute = Two(random.Integer(0, 59));
        var second = Two(random.Integer(0, 59));
        return $"2026-{month}-{day}T{hour}:{minute}:{second}Z";
    }

    private static string HexId(DocumentRandom random, int digits) =>
        string.Concat(Enumerable.Range(0, digits).Select(_ => "0123456789abcdef"[random.Integer(0, 15)]).ToArray());

    private static string Person(DocumentRandom random)
    {
        var first = random.Pick(FirstNames);
        return $"{first} {random.Pick(LastNames)}";
    }

    private static JsonArray Repeat(int count, Func<JsonValue> make)
    {
        var array = new JsonArray();
        for (var item = 0; item < count; item++) array.Items.Add(make());
        return array;
    }

    // Removes some keys after the first `keep`, so the documents of one shape do not all have the same keys.
    private static JsonObject SomeKeys(DocumentRandom random, JsonObject record, int keep, double size)
    {
        var result = new JsonObject();
        for (var index = 0; index < record.Members.Count; index++)
        {
            if (index < keep || random.Chance(0.35 + (0.65 * size))) result.Set(record.Members[index].Key, record.Members[index].Value);
        }
        return result;
    }

    private static JsonObject Dependencies(DocumentRandom random, int count)
    {
        var dependencies = new JsonObject();
        for (var item = 0; item < count; item++)
        {
            var key = random.Chance(0.3) ? $"@{random.Pick(Scopes)}/{random.Pick(PackageWords)}" : $"{random.Pick(PackageWords)}-{random.Pick(PackageWords)}";
            var prefix = random.Pick(["^", "~", ""]);
            var major = Text(random.Integer(0, 9));
            var minor = Text(random.Integer(0, 30));
            var patch = Text(random.Integer(0, 9));
            dependencies.Set(key, $"{prefix}{major}.{minor}.{patch}");
        }
        return dependencies;
    }

    private static JsonValue PackageManifest(DocumentRandom random, double size)
    {
        var name = $"{random.Pick(PackageWords)}-{random.Pick(PackageWords)}";
        var record = new JsonObject();
        record.Set("name", random.Chance(0.4) ? $"@{random.Pick(Scopes)}/{name}" : name);
        var major = Text(random.Integer(0, 9));
        var minor = Text(random.Integer(0, 30));
        record.Set("version", $"{major}.{minor}.{Text(random.Integer(0, 99))}");
        record.Set("description", Sentence(random, 2 + RoundCount(size * 6)));
        record.Set("type", "module");
        record.Set("main", "dist/index.js");
        var scripts = new JsonObject();
        foreach (var (scriptName, command) in Scripts.Where(_ => random.Chance(0.25 + (0.5 * size))).ToArray()) scripts.Set(scriptName, command);
        record.Set("scripts", scripts);
        record.Set("dependencies", Dependencies(random, RoundCount(size * 5)));
        record.Set("devDependencies", Dependencies(random, RoundCount(size * 3)));
        record.Set("keywords", Repeat(RoundCount(size * 4), () => random.Pick(PackageWords)));
        record.Set("license", random.Pick(Licenses));
        record.Set("private", random.Chance(0.4));
        return SomeKeys(random, record, 2, size);
    }

    private static JsonValue UserRecord(DocumentRandom random, double size)
    {
        var name = Person(random);
        var record = new JsonObject();
        record.Set("id", random.Integer(1, 99_999));
        record.Set("name", name);
        record.Set("email", $"{name.Split(' ')[0].ToLowerInvariant()}.{Text(random.Integer(1, 99))}@example.com");
        record.Set("age", random.Integer(18, 90));
        record.Set("active", random.Chance(0.8));
        record.Set("roles", Repeat(random.Integer(0, 1 + RoundCount(size * 3)), () => random.Pick(Roles)));
        var address = new JsonObject();
        address.Set("street", $"{Text(random.Integer(1, 400))} {random.Pick(Streets)}");
        address.Set("city", random.Pick(Cities));
        address.Set("zip", Text(random.Integer(10_000, 99_999)));
        address.Set("country", random.Pick(Countries));
        record.Set("address", address);
        record.Set("lastLogin", random.Chance(0.85) ? Time(random) : JsonNull.Instance);
        record.Set("balance", Round(((random.Next() * 4000) - 1000) * 100) / 100);
        record.Set("manager", random.Chance(0.3) ? JsonNull.Instance : random.Integer(1, 99_999));
        record.Set("bio", Sentence(random, 3 + RoundCount(size * 14)));
        return SomeKeys(random, record, 3, size);
    }

    private static JsonValue LogEvent(DocumentRandom random, double size)
    {
        var level = random.Pick(Levels);
        var record = new JsonObject();
        record.Set("ts", Time(random));
        record.Set("level", level);
        record.Set("service", random.Pick(Services));
        record.Set("msg", random.Pick(Messages));
        record.Set("durationMs", Round(random.Next() * 250_000) / 100);
        record.Set("status", random.Pick<double>([200, 201, 204, 301, 400, 404, 429, 500, 502]));
        record.Set("requestId", HexId(random, 16));
        var service = random.Pick(Services);
        record.Set("host", $"{service}-{Text(random.Integer(1, 12))}.internal");
        record.Set("tags", Repeat(RoundCount(size * 4), () => random.Pick(Tags)));
        if (level is "error" or "fatal")
        {
            var error = new JsonObject();
            error.Set("code", random.Pick(["ETIMEDOUT", "ECONNRESET", "E_VALIDATION"]));
            error.Set("retryable", random.Chance(0.5));
            error.Set("attempts", random.Integer(1, 5));
            record.Set("error", error);
        }
        else
        {
            record.Set("error", JsonNull.Instance);
        }
        record.Set("sampleRate", random.Pick([1, 0.5, 0.01, 1e-4, 2.5e-7]));
        var context = new JsonObject();
        context.Set("region", random.Pick(Tags));
        context.Set("build", HexId(random, 7));
        context.Set("detail", Sentence(random, RoundCount(size * 10)));
        record.Set("context", context);
        return SomeKeys(random, record, 4, size);
    }

    private static JsonValue Settings(DocumentRandom random, double size)
    {
        var count = 2 + RoundCount(size * (SettingEntries.Length - 2));
        var first = random.Integer(0, SettingEntries.Length - 1);
        var settings = new JsonObject();
        for (var offset = 0; offset < count; offset++)
        {
            var (key, make) = SettingEntries[(first + offset) % SettingEntries.Length];
            settings.Set(key, make(random));
        }
        return settings;
    }

    private static JsonValue Order(DocumentRandom random, double size)
    {
        var items = Repeat(1 + RoundCount(size * 4), () =>
        {
            var item = new JsonObject();
            var prefix = random.Pick(["MG", "AP", "LM", "NB", "OO"]);
            item.Set("sku", $"{prefix}-{Text(random.Integer(100, 999))}");
            item.Set("title", random.Pick(Products));
            item.Set("qty", random.Integer(1, 6));
            item.Set("price", Round((random.Next() * 9000) + 100) / 100);
            return item;
        });
        var record = new JsonObject();
        record.Set("orderId", $"A-{Text(random.Integer(10_000, 99_999))}");
        record.Set("items", items);
        record.Set("total", Round(random.Next() * 50_000) / 100);
        record.Set("currency", random.Pick(Currencies));
        record.Set("paid", random.Chance(0.9));
        var customer = new JsonObject();
        customer.Set("name", Person(random));
        customer.Set("city", random.Pick(Cities));
        record.Set("customer", customer);
        record.Set("shippedAt", random.Chance(0.6) ? Time(random) : JsonNull.Instance);
        record.Set("notes", random.Pick(["", "leave at the door", "gift wrap, please", "call \"Ana\" on arrival", "fragile\thandle with care"]));
        record.Set("discount", random.Chance(0.3) ? -Round(random.Next() * 2000) / 100 : 0);
        return SomeKeys(random, record, 3, size);
    }

    private static JsonValue MapFeature(DocumentRandom random, double size)
    {
        JsonValue Coordinate()
        {
            var longitude = Round(((random.Next() * 360) - 180) * 1e5) / 1e5;
            var latitude = Round(((random.Next() * 170) - 85) * 1e5) / 1e5;
            return new JsonArray(longitude, latitude);
        }
        var line = random.Chance(0.4);
        var feature = new JsonObject();
        feature.Set("type", "Feature");
        var geometry = new JsonObject();
        if (line)
        {
            geometry.Set("type", "LineString");
            geometry.Set("coordinates", Repeat(2 + RoundCount(size * 6), Coordinate));
        }
        else
        {
            geometry.Set("type", "Point");
            geometry.Set("coordinates", Coordinate());
        }
        feature.Set("geometry", geometry);
        var properties = new JsonObject();
        properties.Set("name", random.Pick(Cities));
        properties.Set("population", random.Integer(900, 14_000_000));
        properties.Set("elevation", Round((random.Next() * 30_000) - 400) / 10);
        properties.Set("area", random.Pick([1.5e3, 8.25e2, 6.02e23, 3e-9]));
        properties.Set("capital", random.Chance(0.2));
        properties.Set("tags", Repeat(RoundCount(size * 3), () => random.Pick(Tags)));
        properties.Set("note", Sentence(random, 2 + RoundCount(size * 8)));
        feature.Set("properties", SomeKeys(random, properties, 1, size));
        return feature;
    }

    private static JsonValue SensorReadings(DocumentRandom random, double size)
    {
        var sensor = random.Integer(0, Sensors.Length - 1);
        var start = 1_790_000_000 + random.Integer(0, 9_000_000);
        var readings = new JsonArray();
        for (var reading = 0; reading < 1 + RoundCount(size * 7); reading++)
        {
            var record = new JsonObject();
            record.Set("sensor", Sensors[sensor]);
            record.Set("t", start + (reading * 60));
            record.Set("v", Sensors[sensor] == "door-north" ? random.Chance(0.5) : Round((random.Next() * 5000) - 500) / 100);
            record.Set("unit", Units[sensor]);
            if (random.Chance(0.15)) record.Set("gap", JsonNull.Instance);
            readings.Items.Add(record);
        }
        return readings;
    }
}
