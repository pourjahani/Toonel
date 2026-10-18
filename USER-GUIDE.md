# Toonel — User Guide / راهنمای کاربر

---

## English

### What is this?
Toonel is a VPN client that combines **V2Ray** servers (VLESS / VMess / Trojan / Shadowsocks)
with **Psiphon**. You can use either one alone, or chain them together in both directions.

### First run
1. Double-click `Toonel.exe`. Accept the UAC prompt (admin is needed for the VPN tunnel).
2. Go to **Settings → Binaries** and press **Download missing** once. This fetches the three
   engine programs (Xray, sing-box, Psiphon) automatically. You only do this once.
3. Pick your **Theme** (Light / Dark) and **Language** (English / فارسی) — both apply
   instantly and are remembered.

### Add your server
- **Import:** copy your server link (`vless://…`, `vmess://…`, `trojan://…`, `ss://…`,
  `hysteria2://…` / `hy2://…`, `tuic://…`, `wireguard://…`) — or a whole subscription URL —
  to the clipboard, then open **Profiles → Import from clipboard**. WireGuard configs
  (`.conf` files saved from a VPN provider) can be imported too — pick **Import from file**.
- **Subscriptions:** a pasted subscription URL keeps itself fresh — it re-checks on start
  and every so often, the Profiles page shows the server's **traffic used / expiry**, and
  an update only replaces that subscription's profiles. Clash YAML and sing-box JSON
  subscription formats are parsed as well.
- **Copy & paste:** select one or more profiles in the list (Ctrl/Shift+click for several),
  press **Ctrl+C** to copy them as share links, and **Ctrl+V** to import links from the
  clipboard. Duplicates are skipped automatically. Right-click also offers Copy/Paste.
- **QR code:** select a profile and press **QR** to show a scannable code — point your
  phone's VPN client (v2rayNG, Hiddify, …) at it to import the server instantly. The code
  is generated on your PC; nothing is uploaded. It contains the full server credentials,
  so only show it to people you trust.
- Click a profile so it's selected. **Ping** tests its reachability.

### Connect
1. On the **Dashboard**, pick a mode card:
   - **V2Ray** — use your server only (fastest, most common choice).
   - **Psiphon alone** — no server needed; everything goes through the Psiphon network.
   - **Psiphon after V2Ray** — your server first, Psiphon on top.
   - **Psiphon before V2Ray** — Psiphon first, your server on top.
   - **V2Ray over WARP** — your server, with its own connection going through a
     Cloudflare **WARP** hop first (select a WireGuard profile first, ideally Warp).
2. Keep **Whole system (TUN)** checked — this routes *everything* (browsers, terminals,
   git, pip, npm) through the VPN. You can also enable **System proxy** (lighter, some
   apps bypass it) and **Block QUIC**.
3. Press **Connect**. The status turns green and says CONNECTED.
4. While connected in a Psiphon mode, the dashboard and the compact bar show your
   **egress country with its flag emoji** (see below).
5. When finished, press **Disconnect** — or simply **close the window**. Closing always
   resets the system proxy and the tunnel back to normal.
6. Changed your mind while it is still connecting? **Disconnect stays available during a
   connect attempt** — press it (dashboard or compact bar) to abort immediately: the
   attempt is cancelled, engines already started are shut down, and the app returns to
   DISCONNECTED without an error dialog.

### DNS & leak check
- **Settings → DNS** — pick automatic / 1.1.1.1 / 8.8.8.8 / 9.9.9.9, each as plain UDP
  or the safer DoH variant. Both engines use it while connected.
- On the **Dashboard**, press **Check IP** to compare your direct IP with the proxied
  one — it flags a leak if your real address still shows while you are connected.

### Psiphon egress country
- Open **Psiphon → Egress country** and pick the country your traffic should exit from —
  the dropdown lists the countries Psiphon usually serves, each with its flag emoji.
- **Fastest (automatic)** is the default; Psiphon picks the best route itself.
- You can also type any two-letter country code (e.g. `EE`) even if it isn't in the list.
- While connected, the dashboard Psiphon row and the compact bar show the selected
  country's **flag emoji** next to the name. (Windows renders flag emoji as a plain
  two-letter code — e.g. `DE` — so it appears as text rather than a colored flag.)

### Only some apps through the VPN (split tunnel)
1. Open **Split Tunnel**.
2. Pick **Only these apps** (include) or **Everyone except these** (exclude).
3. Type one program name per line, e.g. `chrome.exe` or `Telegram.exe`.
4. Use the **Exceptions** boxes to always exclude (banking sites, LAN) or always proxy
   specific domains, IP ranges or ports.
5. Reconnect to apply.

### Small always-on-top bar
- Press the **—** button (top of the dashboard) to shrink Toonel to a one-line bar that
  stays on top of other windows: status, egress country + flag emoji while connected,
  Connect/Disconnect, and a button to reopen the dashboard. Enable
  **Start with the compact bar** in Settings to always begin there.

### System tray
- Toonel lives in the tray while running. Hover the icon for a live tooltip:
  connection state, mode, egress country and **uptime**. Right-click for
  Connect / Disconnect / Dashboard / Exit.

### One instance only
- Launching `Toonel.exe` while it is already running doesn't open a second copy.
  Instead a small toast says **"Toonel is already running"** and the existing window
  is brought to the front. The second copy exits without touching your VPN session.

### Flush network (reset everything)
- **Settings → Maintenance → Flush network** stops all engines, kills leftover engine
  processes from a crashed run, resets the system proxy (only if Toonel set it),
  removes the terminal proxy variables, and flushes the DNS cache.
- Use it if networking feels broken after an abnormal exit. It does **not** delete your
  profiles, settings or downloaded engines.
- There is also **Apply proxy to future terminals** (sets `HTTP_PROXY` etc. via `setx`)
  and **Open runtime folder** (where the generated engine configs and logs live).

### Appearance
- **Settings → Theme** → **Light** or **Dark**. The whole app switches instantly —
  dashboard, compact bar, dialogs and toasts. Both themes keep proper contrast:
  filled buttons always use reversed text (white-on-ink in light, ink-on-light in dark).
- **Settings → Language** → English or **فارسی**. Persian switches the whole app to RTL.

### App icon
- The Toonel mark is used for the exe, taskbar, window titles and the tray icon.

### Troubleshooting
- **Nothing happens when I start the exe** — check `data\crash.log` in the app folder
  (the folder you keep `Toonel.exe` in)
  for the cause, and make sure no hidden `Toonel.exe` is left in Task Manager.
- **Connect failed** — read the **Logs** page (source: app / xray / psiphon / sing-box).
  Missing binaries are offered for download automatically before connecting.
- **Internet broken after a crash** — run **Settings → Maintenance → Flush network**,
  or reboot.
- **TUN needs admin** — the published exe already requests admin (UAC at start);
  in a Debug build sing-box asks via its own UAC prompt when you enable TUN.

---

## فارسی

### این برنامه چیست؟
تونل یک کلاینت VPN است که سرورهای **وی‌ری** (VLESS / VMess / Trojan / Shadowsocks) را با
**سایفون** ترکیب می‌کند؛ هر کدام به‌تنهایی یا به‌صورت زنجیره‌ای در هر دو جهت.

### اجرای اول
1. روی `Toonel.exe` دوبار کلیک کنید و پنجره UAC را تأیید کنید.
2. در **تنظیمات → فایل‌های موتور** یک‌بار **دانلود موتورهای ناقص** را بزنید (فقط همان یک بار).
3. **پوسته** (روشن/تاریک) و **زبان** (فارسی/English) را انتخاب کنید — هر دو فوراً اعمال و
   به خاطر سپرده می‌شوند.

### اضافه کردن سرور
- **ورود:** لینک سرور (`vless://…`، `vmess://…`، `trojan://…`، `ss://…`، `hysteria2://…`
  یا `hy2://…`، `tuic://…`، `wireguard://…`) یا نشانی اشتراک را کپی کنید و در بخش
  **سرورها** روی **ورود از کلیپ‌بورد** بزنید. فایل‌های WireGuard با پسوند `‎.conf` هم
  با **ورود از فایل** قابل ورود هستند.
- **اشتراک‌ها:** اگر نشانی اشتراک وارد کنید خودش به‌روز می‌ماند — در شروع برنامه و هر چند
  ساعت یک‌بار دوباره بررسی می‌شود، صفحه سرورها **مصرف ترافیک / انقضا** را نشان می‌دهد و
  هر به‌روزرسانی فقط پروفایل‌های همان اشتراک را جایگزین می‌کند. فرمت‌های Clash YAML و
  JSON سینگ‌باکس هم پارس می‌شوند.
- **کپی و چسباندن:** یک یا چند پروفایل را در فهرست انتخاب کنید (با Ctrl/Shift+کلیک)،
  **Ctrl+C** برای کپی به شکل لینک اشتراک و **Ctrl+V** برای ورود از کلیپ‌بورد.
  موارد تکراری خودکار رد می‌شوند. راست‌کلیک هم کپی/چسباندن دارد.
- **کد QR:** پروفایل را انتخاب و دکمه **QR** را بزنید؛ کد قابل اسکن نمایش داده می‌شود —
  اپ کلاینت VPN گوشی (مثل v2rayNG) را روی آن بگیرید تا سرور فوراً وارد شود. کد روی
  کامپیوتر شما ساخته می‌شود و چیزی جایی ارسال نمی‌شود. این کد شامل مشخصات کامل سرور است،
  پس فقط به افراد مورد اعتماد نشان دهید.
- روی سرور کلیک کنید تا انتخاب شود. **پینگ** وضعیت دسترسی را می‌سنجد.

### اتصال
1. در **پیشخوان** یک حالت انتخاب کنید:
   - **وی‌ری** — فقط سرور شما (سریع‌ترین و رایج‌ترین حالت).
   - **فقط سایفون** — بدون نیاز به سرور.
   - **سایفون بعد از وی‌ری** — ابتدا سرور شما، سپس سایفون.
   - **سایفون قبل از وی‌ری** — ابتدا سایفون، سپس سرور شما.
   - **وی‌ری روی وارپ** — سرور شما، اما اتصال خود سرور ابتدا از یک هاپ
     **وارپ (Cloudflare WARP)** عبور می‌کند (ابتدا یک پروفایل WireGuard — ترجیحاً Warp — انتخاب کنید).
2. گزینه **کل سیستم (تونل)** فعال بماند تا همه برنامه‌ها و ترمینال‌ها هم از VPN عبور کنند.
   گزینه‌های **پروکسی سیستم** و **مسدود کردن کوییک** هم در دسترس هستند.
3. **اتصال** را بزنید؛ وضعیت سبز می‌شود.
4. در حالت‌های سایفون، پیشخوان و نوار کوچک **کشور خروجی را همراه ایموجی پرچم** نشان می‌دهند.
5. برای پایان، **قطع** را بزنید یا فقط پنجره را ببندید — بستن پنجره همیشه پروکسی و تونل را
   به حالت عادی برمی‌گرداند.
6. پشیمان شدید در حالی که هنوز در حال اتصال است؟ دکمه **قطع در حین تلاش برای اتصال هم فعال
   می‌ماند** — آن را بزنید (پیشخوان یا نوار کوچک) تا تلاش فوراً لغو شود: موتورهای روشن‌شده
   خاموش می‌شوند و برنامه بدون پیام خطا به حالت DISCONNECTED برمی‌گردد.

### دی‌ان‌اس و بررسی نشت
- **تنظیمات → DNS** — بین خودکار / 1.1.1.1 / 8.8.8.8 / 9.9.9.9 انتخاب کنید؛ هر کدام به
  دو شکل UDP یا وسیع‌تر DoH. هنگام اتصال هر دو موتور از آن استفاده می‌کنند.
- در **پیشخوان** دکمه **Check IP** را بزنید تا آی‌پی مستقیم با آی‌پی مسیر پروکسی مقایسه
  شود — اگر آدرس واقعی شما هنگام اتصال دیده شود، نشت اعلام می‌گردد.

### کشور خروجی سایفون
- در بخش **سایفون → کشور خروجی** کشور مورد نظر را از فهرست (همراه ایموجی پرچم هر کشور) انتخاب کنید.
- پیش‌فرض **سریع‌ترین (خودکار)** است؛ سایفون خودش بهترین مسیر را انتخاب می‌کند.
- می‌توانید کد دو حرفی هر کشوری (مثل `EE`) را حتی اگر در فهرست نیست تایپ کنید.
- هنگام اتصال، ردیف سایفون پیشخوان و نوار کوچک **ایموجی پرچم** آن کشور را کنار نام نشان می‌دهند.

### تقسیم ترافیک (فقط برخی برنامه‌ها)
1. بخش **تقسیم ترافیک** را باز کنید.
2. «فقط این برنامه‌ها» یا «همه به جز این‌ها» را انتخاب کنید.
3. نام برنامه‌ها را هر خط یکی بنویسید، مثل `chrome.exe`.
4. در کادرهای **استثناها** می‌توانید دامنه‌ها، بازه‌های آی‌پی یا پورت‌های همیشه‌مستقیم یا
   همیشه‌پروکسی را مشخص کنید.
5. یک‌بار قطع و دوباره وصل شوید.

### نوار کوچک همیشه‌روان
- دکمه **—** بالای پیشخوان برنامه را به یک نوار یک‌خطی همیشه‌روان تبدیل می‌کند:
  وضعیت، کشور خروجی با ایموجی پرچم هنگام اتصال، اتصال/قطع، و دکمه بازگشت به پنجره کامل.
  در تنظیمات می‌توانید فعال کنید که برنامه همیشه با همین نوار شروع شود.

### سیستم‌تری (پایین گوشه صفحه)
- تونل هنگام اجرا در تری سیستم است. نشانگر را روی آیکن نگه دارید تا راهنمای زنده ببینید:
  وضعیت اتصال، حالت، کشور خروجی و **مدت اتصال**. راست‌کلیک: اتصال / قطع / پیشخوان / خروج.

### فقط یک نسخه از برنامه
- اجرای دوباره `Toonel.exe` نسخه دوم باز نمی‌کند؛ یک اعلان کوچک می‌گوید
  **«تونل از قبل در حال اجراست»** و پنجره باز به جلو می‌آید. نسخه دوم بدون دست زدن به
  جلسه VPN بسته می‌شود.

### پاک‌سازی شبکه (بازنشانی همه چیز)
- **تنظیمات → تعمیرات → پاک‌سازی شبکه** همه موتورها را متوقف، فرایندهای باقی‌مانده را حذف،
  پروکسی سیستم را (فقط اگر تونل تنظیمش کرده باشد) بازنشانی، متغیرهای ترمینال را پاک و
  کش DNS را خالی می‌کند.
- اگر بعد از خروج غیرعادی اینترنت خراب شد از آن استفاده کنید. پروفایل‌ها، تنظیمات و
  موتورهای دانلودشده **پاک نمی‌شوند**.
- گزینه‌های **اعمال پروکسی به ترمینال‌های جدید** و **باز کردن پوشه اجرا** هم همین‌جاست.

### ظاهر برنامه
- **تنظیمات → پوسته** → **روشن** یا **تاریک**. کل برنامه فوراً عوض می‌شود — پیشخوان،
  نوار کوچک، پنجره‌ها و اعلان‌ها. در هر دو پوسته کنتراست درست حفظ می‌شود: دکمه‌های توپر
  همیشه متن معکوس دارند (سفید روی مشکی در پوسته روشن، برعکس در تاریک).
- **تنظیمات → زبان** → English یا **فارسی**.

### آیکن برنامه
- نشان تونل برای فایل اجرایی، نوار وظیفه، عنوان پنجره‌ها و تری استفاده می‌شود.

### عیب‌یابی
- **اجرا می‌شود اما هیچ چیزی نشان داده نمی‌شود** — فایل   `data\crash.log` در همان پوشه برنامه را ببینید و مطمئن شوید `Toonel.exe` پنهانی در
  Task Manager باقی نمانده است.
- **اتصال ناموفق** — صفحه **گزارش‌ها** را بخوانید (منبع: app / xray / psiphon / sing-box).
  اگر موتوری ناقص باشد، قبل از اتصال پیشنهاد دانلود داده می‌شود.
- **اینترنت بعد از خرابی برنامه قطع شد** — **تنظیمات → تعمیرات → پاک‌سازی شبکه** را اجرا
  کنید یا سیستم را ری‌استارت کنید.
- **تونل به دسترسی مدیر نیاز دارد** — فایل منتشرشده خودش UAC می‌خواهد؛ در بیلد Debug،
  sing-box هنگام فعال‌بودن تونل جداگانه UAC می‌گیرد.
