namespace Toonel.App;

public enum AppLanguage { En, Fa }

/// <summary>
/// Minimal dictionary-based UI localization (English / Persian).
/// Persian switches the whole window to RTL flow.
/// </summary>
public static class Loc
{
    public static AppLanguage Language { get; set; } = AppLanguage.En;

    public static bool IsFa => Language == AppLanguage.Fa;

    public static event Action? Changed;

    public static void Set(AppLanguage lang)
    {
        if (Language == lang) return;
        Language = lang;
        Changed?.Invoke();
    }

    public static string T(string key) =>
        Language == AppLanguage.Fa && Fa.ContainsKey(key) ? Fa[key] : key;

    public static System.Windows.FlowDirection Flow => IsFa ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;

    // ---------------------------------------------------------------
    // Persian strings. Keys are the English source text.
    // ---------------------------------------------------------------
    private static readonly Dictionary<string, string> Fa = new()
    {
        // App
        ["Toonel"] = "تونل",
        ["XRAY + PSIPHON CLIENT"] = "کلاینت ایکس‌ری + سایفون",
        ["ENGINES"] = "موتورها",
        ["ROUTE"] = "مسیر",

        // Version / About
        ["About"] = "درباره",

        // Nav
        ["Dashboard"] = "پیشخوان",
        ["Profiles"] = "سرورها",
        ["Split Tunnel"] = "تقسیم ترافیک",
        ["Psiphon"] = "سایفون",
        ["Configs"] = "پیکربندی‌ها",
        ["Logs"] = "گزارش‌ها",
        ["Settings"] = "تنظیمات",

        // Status
        ["DISCONNECTED"] = "قطع",
        ["CONNECTED"] = "متصل",
        ["WORKING"] = "در حال اتصال",
        ["ERROR"] = "خطا",
        ["Not connected"] = "متصل نیستید",
        ["Working…"] = "در حال اتصال…",
        ["Something failed — see Logs"] = "خطایی رخ داد — گزارش‌ها را ببینید",
        ["Connected · V2Ray"] = "متصل · وی‌ری",
        ["Connected · Psiphon"] = "متصل · سایفون",
        ["Connected · Psiphon after V2Ray"] = "متصل · سایفون بعد از وی‌ری",
        ["Connected · Psiphon before V2Ray"] = "متصل · سایفون قبل از وی‌ری",
        ["Pick how traffic should travel, then press Connect."] = "نحوه عبور ترافیک را انتخاب کنید و دکمه اتصال را بزنید.",

        // Modes
        ["V2Ray"] = "وی‌ری",
        ["Your profile only. Fast, simple."] = "فقط سرور شما. سریع و ساده.",
        ["Psiphon alone"] = "فقط سایفون",
        ["No profile. Everything through Psiphon."] = "بدون سرور. همه چیز از سایفون.",
        ["Psiphon after V2Ray"] = "سایفون بعد از وی‌ری",
        ["Psiphon rides on your VLESS line."] = "سایفون روی خط وی‌لس شما سوار می‌شود.",
        ["Psiphon before V2Ray"] = "سایفون قبل از وی‌ری",
        ["VLESS rides through Psiphon first."] = "خط وی‌لس ابتدا از داخل سایفون عبور می‌کند.",
        ["Traffic goes straight through your profile."] = "ترافیک مستقیم از سرور شما عبور می‌کند.",
        ["Everything goes through the Psiphon network."] = "همه ترافیک از شبکه سایفون عبور می‌کند.",
        ["Psiphon dials out through your VLESS line."] = "سایفون از طریق خط وی‌لس شما خروجی می‌گیرد.",
        ["Your VLESS line dials through Psiphon first."] = "خط وی‌لس شما ابتدا از سایفون عبور می‌کند.",

        // Buttons
        ["Connect"] = "اتصال",
        ["Disconnect"] = "قطع",
        ["Import from clipboard"] = "ورود از کلیپ‌بورد",
        ["From file…"] = "از فایل…",
        ["Ping"] = "پینگ",
        ["Delete"] = "حذف",
        ["Clear"] = "پاک کردن",
        ["Refresh"] = "بازیابی",
        ["Download missing"] = "دانلود موتورهای ناقص",
        ["Apply proxy to future terminals"] = "اعمال پروکسی به ترمینال‌های جدید",
        ["Open runtime folder"] = "باز کردن پوشه اجرا",

        // Configs page
        ["MANUAL CONFIGS"] = "ویرایش دستی پیکربندی",
        ["Edit engine configs"] = "ویرایش پیکربندی موتورها",
        ["Generate fills the editor with the config Toonel would run right now — edit any part of it. Saved configs are used on the next connect; an invalid save is ignored at launch (the generated config runs instead)."]
            = "ساخت خودکار، پیکربندی‌ای را که تونل همین حالا اجرا می‌کند داخل ویرایشگر می‌ریزد — هر بخش از آن را می‌توانید ویرایش کنید. پیکربندی ذخیره‌شده در اتصال بعدی استفاده می‌شود؛ ذخیرهٔ نامعتبر در زمان اجرا نادیده گرفته می‌شود (پیکربندی ساخته‌شده اجرا می‌شود).",
        ["Generate"] = "ساخت",
        ["Validate"] = "بررسی",
        ["Save"] = "ذخیره",
        ["Use manual config"] = "استفاده از پیکربندی دستی",
        ["Valid ✓"] = "معتبر ✓",
        ["Not valid JSON: {0}"] = "JSON نامعتبر: {0}",
        ["Not valid JSON: {0}\n\nNothing was saved."] = "JSON نامعتبر: {0}\n\nهیچ چیز ذخیره نشد.",
        ["Saved ✓"] = "ذخیره شد ✓",
        ["Generated. Press Save to keep it."] = "ساخته شد. برای نگه‌داشتن آن، ذخیره را بزنید.",
        ["Saved. {0} will use it on the next connect."] = "ذخیره شد. {0} در اتصال بعدی از آن استفاده می‌کند.",
        ["DPI / TLS (baked into every generated Xray config)"] = "ضد فیلترینگ / TLS (در همه پیکربندی‌های ساخته‌شده ایکس‌ری اعمال می‌شود)",
        ["Fragment works on every engine version. Cipher suites apply to TLS 1.0–1.2 only (a uTLS fingerprint overrides them). FinalMask needs Xray-core v26.2.6+ — press Update Xray in Settings → Binaries if your cores folder still has the old engine."]
            = "تکه‌تکه کردن (Fragment) روی همه نسخه‌های موتور کار می‌کند. مجموعه رمزهای TLS فقط برای 1.0 تا 1.2 اعمال می‌شود (اثر انگشت uTLS آن‌ها را بازنویسی می‌کند). FinalMask با ایکس‌ری نسخه 26.2.6 به بالا کار می‌کند — قدرت‌های قدیمی پوشه cores، از تنظیمات → فایل‌های موتور دکمه به‌روزرسانی ایکس‌ری را بزنید.",
        ["Fragment"] = "تکه‌تکه کردن",
        ["packets"] = "بسته‌ها",
        ["length"] = "طول",
        ["interval ms"] = "فاصله (میلی‌ثانیه)",
        ["Cipher suites"] = "مجموعه رمزهای TLS",
        ["FinalMask (raw FinalMaskObject JSON, advanced)"] = "FinalMask (JSON خام FinalMaskObject — پیشرفته)",
        ["Update Xray"] = "به‌روزرسانی ایکس‌ری",
        ["Replace cores\\xray.exe with the pinned release ({0})? Stop the tunnel first."] = "جایگزینی cores\\xray.exe با نسخه ثابت ({0})؟ ابتدا اتصال را قطع کنید.",
        ["xray.exe updated to {0}."] = "xray.exe به نسخه {0} به‌روزرسانی شد.",
        ["xray.exe is running — disconnect first, then update."] = "xray.exe در حال اجراست — ابتدا قطع کنید، بعد به‌روزرسانی کنید.",
        ["Xray update failed."] = "به‌روزرسانی ایکس‌ری ناموفق بود.",

        // Engine rows
        ["Xray"] = "ایکس‌ری",
        ["Psiphon"] = "سایفون",
        ["TUN adapter"] = "آداپتور تونل",
        ["stopped"] = "متوقف",
        ["running"] = "در حال اجرا",
        ["tunnel up"] = "تونل برقرار",
        ["connecting…"] = "در حال اتصال…",
        ["active"] = "فعال",
        ["enabled"] = "فعال",
        ["off"] = "خاموش",

        // Options
        ["Whole system (TUN)"] = "کل سیستم (تونل)",
        ["System proxy"] = "پروکسی سیستم",
        ["Block QUIC"] = "مسدود کردن کوییک",

        // Split tunnel page
        ["PER-APP ROUTING"] = "مسیریابی هر برنامه",
        ["Which apps go through the tunnel?"] = "کدام برنامه‌ها از تونل عبور کنند؟",
        ["All apps tunnel"] = "همه برنامه‌ها از تونل",
        ["Only these apps"] = "فقط این برنامه‌ها",
        ["Everyone except these"] = "همه به جز این‌ها",
        ["APPS"] = "برنامه‌ها",
        ["One executable per line, e.g. chrome.exe"] = "هر خط یک فایل اجرایی، مثل chrome.exe",
        ["EXCEPTIONS"] = "استثناها",
        ["Domains, IPs/CIDRs, ports — e.g. bank.ir · 10.0.0.0/8 · 80"] = "دامنه، آی‌پی/CIDR، پورت — مثل bank.ir · 10.0.0.0/8 · 80",
        ["Never tunnel"] = "هرگز از تونل نرود",
        ["Always tunnel"] = "همیشه از تونل برود",
        ["Bypass LAN and private IPs"] = "عبور مستقیم شبکه داخلی و آی‌پی‌های خصوصی",
        ["include · {0} apps"] = "شامل · {0} برنامه",
        ["exclude · {0} apps"] = "به‌جز · {0} برنامه",
        ["system proxy"] = "پروکسی سیستم",
        ["split tunnel: off"] = "تقسیم ترافیک: خاموش",

        // Psiphon page
        ["PSIPHON"] = "سایفون",
        ["Network options"] = "تنظیمات شبکه",
        ["Egress country"] = "کشور خروجی",
        ["Sponsor ID"] = "شناسه حامی",
        ["Propagation channel ID"] = "شناسه کانال انتشار",
        ["Remote server list URL"] = "نشانی فهرست سرورها",
        ["ADVANCED"] = "پیشرفته",
        ["Custom config"] = "پیکربندی دلخواه",
        ["JSON merged into client.config, or a psiphon:// link (region, protocol, sponsor, upstream). Example: psiphon://?region=DE&protocol=QUIC-OSSH. UpstreamProxyURL is managed by chain modes."]
            = "JSON در client.config ادغام می‌شود، یا یک لینک psiphon:// (منطقه، پروتکل، حامی، پروکسی بالادست). مثال: psiphon://?region=DE&protocol=QUIC-OSSH. مقدار UpstreamProxyURL توسط حالت‌های زنجیره مدیریت می‌شود.",

        // Logs
        ["Follow"] = "دنبال کردن",

        // Settings
        ["SETTINGS"] = "تنظیمات",
        ["Behavior"] = "رفتار",
        ["Connect automatically at launch"] = "اتصال خودکار هنگام اجرا",
        ["Minimize to tray instead of quitting"] = "کمینه به سیستم‌تری به جای خروج",
        ["When off, closing the window quits and resets the proxy and TUN."] = "خاموش باشد، بستن پنجره برنامه را می‌بندد و پروکسی و تونل را بازنشانی می‌کند.",
        ["PORTS"] = "پورت‌ها",
        ["Main SOCKS"] = "ساکس اصلی",
        ["Main HTTP"] = "HTTP اصلی",
        ["Chain hop (anti-loop)"] = "پرش زنجیره (ضد حلقه)",
        ["Psiphon SOCKS"] = "ساکس سایفون",
        ["Psiphon HTTP"] = "HTTP سایفون",
        ["Binaries"] = "فایل‌های موتور",
        ["TERMINAL"] = "ترمینال",

        // Messages
        ["This mode needs a VLESS/VMess/Trojan profile. Add one in Profiles."] = "این حالت به یک پروفایل VLESS/VMess/Trojan نیاز دارد. در بخش سرورها اضافه کنید.",
        ["Clipboard is empty."] = "کلیپ‌بورد خالی است.",
        ["Import"] = "ورود",
        ["Imported {0} profile(s).{1}"] = "{0} پروفایل وارد شد.{1}",
        [" Skipped {0} duplicate(s)."] = " {0} مورد تکراری رد شد.",
        ["Some engine binaries are missing:"] = "برخی فایل‌های موتور موجود نیست:",
        ["\n\nDownload them now? (pinned official releases)"] = "\n\nاکنون دانلود شود؟ (نسخه‌های رسمی ثابت)",
        ["Core download failed: {0}"] = "دانلود موتور ناموفق: {0}",
        ["Download failed: {0}\n\nYou can also place binaries manually into the cores folder (see path below)."]
            = "دانلود ناموفق: {0}\n\nمی‌توانید فایل‌ها را دستی در پوشه cores قرار دهید (مسیر زیر را ببینید).",
        ["Set for future terminals (setx). Terminals already open need the paste-in snippet from the runtime folder."]
            = "برای ترمینال‌های جدید تنظیم شد (setx). ترمینال‌های باز فعلی به اسنیپت داخل پوشه اجرا نیاز دارند.",
        ["Connect failed"] = "اتصال ناموفق",
        ["Toonel is already running."] = "تونل از قبل در حال اجراست.",
        ["Flush network"] = "پاک‌سازی شبکه",
        ["This stops all engines, resets the system proxy, removes the terminal proxy variables and flushes DNS. Continue?"]
            = "همه موتورها متوقف، پروکسی سیستم بازنشانی، متغیرهای ترمینال حذف و DNS پاک می‌شود. ادامه؟",
        ["Network flushed successfully."] = "شبکه با موفقیت پاک‌سازی شد.",
        ["Network flushed with warnings:\n{0}"] = "شبکه با هشدار پاک‌سازی شد:\n{0}",
        ["{0} leftover engine process(es) killed."] = "{0} فرایند موتور باقی‌مانده حذف شد.",
        ["MAINTENANCE"] = "تعمیرات",
        ["Stops all engines, resets the system proxy and terminal variables, flushes DNS."]
            = "همه موتورها را متوقف، پروکسی سیستم و ترمینال را بازنشانی و DNS را پاک می‌کند.",
        ["Copy (Ctrl+C)"] = "کپی (Ctrl+C)",
        ["Paste (Ctrl+V)"] = "چسباندن (Ctrl+V)",
        ["Copy"] = "کپی",
        ["Paste"] = "چسباندن",
        ["No profile selected."] = "هیچ پروفایلی انتخاب نشده است.",
        ["Selected profiles cannot be copied."] = "پروفایل‌های انتخاب‌شده قابل کپی نیستند.",
        ["Copied {0} profile(s) to clipboard."] = "{0} پروفایل در کلیپ‌بورد کپی شد.",
        ["Pasted {0} profile(s).{1}"] = "{0} پروفایل چسبانده شد.{1}",
        ["No valid profiles found in clipboard."] = "پروفایل معتبری در کلیپ‌بورد نیست.",
        ["Copy failed: {0}"] = "کپی ناموفق: {0}",
        ["Ctrl+C copies selected profiles (one link per line). Ctrl+V pastes and skips duplicates."]
            = "Ctrl+C پروفایل‌های انتخاب‌شده را کپی می‌کند (یک لینک در هر خط). Ctrl+V چسباندن می‌کند و موارد تکراری را رد می‌کند.",
        ["exit"] = "خروجی",
        ["Uptime"] = "زمان اتصال",
        ["Scan with your phone's VPN client to import this server."] = "با اپ کلاینت VPN گوشی اسکن کنید تا سرور وارد شود.",
        ["Copy link"] = "کپی لینک",
        ["Link copied to clipboard."] = "لینک در کلیپ‌بورد کپی شد.",
        ["This profile type cannot be shared as a QR code."] = "این نوع پروفایل به صورت QR قابل اشتراک نیست.",
        ["Paste failed: {0}"] = "چسباندن ناموفق: {0}",
        ["Paste vless:// · vmess:// · trojan:// · ss:// links or a subscription URL via Import from clipboard. Ctrl+C copies selected profiles (one link per line). Ctrl+V pastes and skips duplicates."
        ] = "پا ست کردن لینک‌های vless:// · vmess:// · trojan:// · ss:// یا آدرس اشتراک از کلیپ‌بورد با ورود از کلیپ‌بورد. Ctrl+C پروفایل‌های انتخابی را کپی می‌کند (یک لینک در هر خط). Ctrl+V می‌چسباند و تکراری‌ها را رد می‌کند.",
    };

    // Format helper for parameterized strings.
    public static string F(string key, params object[] args) =>
        string.Format(T(key), args);
}
