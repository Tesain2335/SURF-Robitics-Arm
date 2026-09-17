using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class LauncherConfig
{
    public string Unity { get; set; }
    public string Project { get; set; }
    public string Vive { get; set; }
    public string SteamVR { get; set; }
    public string Usbipd { get; set; }
    public string Distro { get; set; }
    public string Backend { get; set; }
    public string WslHome { get; set; }
}

public class Result { public int Code; public string Output; public string Error; }

public static class Runner
{
    public static string Q(string text)
    {
        // Paths used here never end with a backslash or contain embedded quotes.
        if (text.Contains("\"") || text.EndsWith("\\")) throw new ArgumentException("Invalid argument");
        // This installed WSL treats unnecessary quotes around -d literally.
        if (!text.Any(char.IsWhiteSpace)) return text;
        return "\"" + text + "\"";
    }
    public static Result Run(string exe, string args, int seconds)
    {
        using (var p = new Process())
        {
            p.StartInfo = new ProcessStartInfo(exe, args) {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            p.Start();
            Task<string> output = p.StandardOutput.ReadToEndAsync();
            Task<string> error = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(seconds * 1000))
            {
                p.Kill();
                throw new TimeoutException("操作超时，请查看日志后重试。后台状态将重新检查。");
            }
            Task.WaitAll(output, error);
            return new Result { Code = p.ExitCode, Output = output.Result, Error = error.Result };
        }
    }
}

public sealed class LauncherForm : Form
{
    readonly LauncherConfig cfg;
    readonly string baseDir;
    readonly string logPath;
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    readonly Label state = new Label();
    readonly Label details = new Label();
    readonly RichTextBox log = new RichTextBox();
    readonly CheckBox vr = new CheckBox();
    readonly List<Button> actions = new List<Button>();
    readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
    bool busy;
    bool closing;
    bool liveMode;
    bool monitor;
    Process wslHold;
    string lastError = "";
    public bool NoAutoStart;

    public LauncherForm(string directory)
    {
        baseDir = directory;
        cfg = json.Deserialize<LauncherConfig>(File.ReadAllText(Path.Combine(baseDir, "config.json")));
        Directory.CreateDirectory(Path.Combine(baseDir, "logs"));
        logPath = Path.Combine(baseDir, "logs", DateTime.Now.ToString("yyyy-MM-dd") + ".log");
        Text = "SURF 项目启动器 1.2";
        ClientSize = new Size(840, 642);
        MinimumSize = Size;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Color.FromArgb(245, 247, 250);
        var title = new Label { Text = "SURF  /  Piper VR 工作台", AutoSize = true,
            Location = new Point(24, 20), Font = new Font(Font.FontFamily, 20, FontStyle.Bold) };
        Controls.Add(title);
        var subtitle = new Label { Text = "双击先打开软件；点击“一键启动实机”自动挂载、连接并使能 Piper。",
            Location = new Point(26, 68), AutoSize = true };
        Controls.Add(subtitle);
        state.Location = new Point(26, 109); state.Size = new Size(780, 30);
        state.Font = new Font(Font.FontFamily, 13, FontStyle.Bold); state.Text = "准备就绪";
        Controls.Add(state);
        details.Location = new Point(26, 143); details.Size = new Size(780, 43);
        details.Text = "开发模式只打印目标，不发送机械臂命令。Unity Play 由你手动点击。";
        Controls.Add(details);
        vr.Text = "同时打开 VIVE Streaming 和 SteamVR"; vr.Checked = true;
        vr.Location = new Point(26, 192); vr.Size = new Size(490, 28); Controls.Add(vr);
        AddButton("一键启动实机…", 26, 233, async () => await StartLive());
        actions[0].BackColor = Color.FromArgb(206, 233, 230);
        AddButton("仅打开软件", 223, 233, async () => await StartProject());
        AddButton("USB-CAN 检查", 420, 233, async () => await AttachCan());
        AddButton("停止 ROS 服务", 617, 233, async () => {
            await Backend("stop"); monitor = false; liveMode = false;
            state.Text = "ROS 服务已停止"; details.Text = "Unity 和 VR 软件保持打开；机械臂已接收的目标不会被撤销。";
        });
        AddButton("真实反馈 RViz", 26, 285, async () => { await Backend("rviz"); Log("已请求打开真实反馈 RViz"); });
        AddButton("打开项目文件夹", 223, 285, () => { Open(cfg.Project); return Task.FromResult(0); });
        AddButton("查看服务日志", 420, 285, () => {
            Open("\\\\wsl.localhost\\" + cfg.Distro + cfg.WslHome.Replace('/', '\\') + "\\.local\\state\\surf-launcher"); return Task.FromResult(0);
        });
        AddButton("使用说明", 617, 285, () => { Open(Path.Combine(baseDir, "使用说明.txt")); return Task.FromResult(0); });
        AddButton("一键修复六轴不动", 26, 337, async () => await RepairArm());
        actions[8].BackColor = Color.FromArgb(255, 230, 183);
        Controls.Add(new Label { Text = "自动退出 Play、修复发布并重连实机（75%）；完成后手动 Play。",
            Location = new Point(223, 343), Size = new Size(590, 38) });
        log.Location = new Point(26, 402); log.Size = new Size(786, 176);
        log.ReadOnly = true; log.BackColor = Color.White; log.BorderStyle = BorderStyle.FixedSingle;
        log.Font = new Font("Microsoft YaHei UI", 9); Controls.Add(log);
        var footer = new Label { Text = "关闭此窗口会停止本启动器创建的 ROS 服务，不关闭 Unity。停止服务不是急停。",
            Location = new Point(26, 594), Size = new Size(790, 30), ForeColor = Color.DimGray };
        Controls.Add(footer);
        Shown += async (s, e) => { if (!NoAutoStart) await Guard(StartProject); };
        poll.Interval = 8000;
        poll.Tick += async (s, e) => {
            if (busy || !monitor) return;
            busy = true;
            try { UpdateStatus(await Backend("status")); }
            catch (Exception ex) { state.Text = "服务检查失败"; Log(ex.Message); }
            finally { busy = false; }
        };
        poll.Start();
        FormClosing += async (s, e) => {
            if (closing) return;
            if (busy) { e.Cancel = true; Log("当前操作尚未结束，请稍后关闭。"); return; }
            if (!monitor) { ReleaseWsl(); return; }
            e.Cancel = true;
            await Guard(async () => {
                await Backend("stop"); monitor = false; ReleaseWsl(); closing = true; Close();
            });
        };
    }

    void AddButton(string text, int x, int y, Func<Task> operation)
    {
        var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(187, 42),
            FlatStyle = FlatStyle.Flat, BackColor = Color.White };
        b.Click += async (s, e) => await Guard(operation);
        Controls.Add(b); actions.Add(b);
    }

    async Task Guard(Func<Task> action)
    {
        if (busy) return;
        lastError = "";
        busy = true; foreach (var b in actions) b.Enabled = false;
        try { await action(); }
        catch (Exception ex) { lastError = ex.Message; state.Text = "启动未完成 · 请查看下面的错误原因"; state.ForeColor = Color.DarkRed; Log(ex.Message); }
        finally { busy = false; foreach (var b in actions) b.Enabled = true; }
    }

    void Log(string value)
    {
        string line = DateTime.Now.ToString("HH:mm:ss") + "  " + value + Environment.NewLine;
        log.AppendText(line); log.ScrollToCaret(); File.AppendAllText(logPath, line, Encoding.UTF8);
    }

    async Task<Dictionary<string, object>> Backend(string action)
    {
        EnsureWsl();
        var r = await Task.Run(() => Runner.Run("wsl.exe", "-d " + Runner.Q(cfg.Distro) +
            " -- bash " + Runner.Q(cfg.Backend) + " " + action, 115));
        Dictionary<string, object> data;
        try { data = json.Deserialize<Dictionary<string, object>>(r.Output.Trim()); }
        catch { throw new Exception("WSL 未返回状态：" + r.Output + r.Error); }
        if (r.Code != 0 || !Convert.ToBoolean(data["ok"]))
            throw new Exception(data.ContainsKey("error") ? Convert.ToString(data["error"]) : r.Error);
        return data;
    }

    void EnsureWsl()
    {
        if (wslHold != null && !wslHold.HasExited) return;
        // Keep stdin open on a harmless cat process. This prevents idle WSL
        // teardown and USB detachment between short backend commands.
        wslHold = Process.Start(new ProcessStartInfo("wsl.exe", "-d " + Runner.Q(cfg.Distro) + " -- cat") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true });
        wslHold.BeginOutputReadLine(); wslHold.BeginErrorReadLine();
    }

    void ReleaseWsl()
    {
        if (wslHold == null) return;
        try {
            if (!wslHold.HasExited) {
                wslHold.StandardInput.Close();
                if (!wslHold.WaitForExit(2000)) wslHold.Kill();
            }
        } finally { wslHold.Dispose(); wslHold = null; }
    }

    void UpdateStatus(Dictionary<string, object> data)
    {
        string mode = Convert.ToString(data["mode"]);
        var services = (Dictionary<string, object>)data["services"];
        liveMode = mode == "live";
        bool ready = services["endpoint"] != null && services["bridge"] != null;
        bool driverOk = !liveMode || services["driver"] != null;
        state.Text = !ready || !driverOk ? "部分 ROS 服务未运行，请查看日志" : liveMode ? "实机控制已开启" : "仅软件已启动 · 机械臂不会锁死或运动";
        if (lastError.Length > 0) state.Text = "启动未完成 · 请查看下面的错误原因";
        state.ForeColor = liveMode ? Color.DarkRed : Color.FromArgb(22, 95, 92);
        details.Text = "WSL " + data["ip"] + ":10000  |  通信 " + (services["endpoint"] != null ? "运行中" : "未运行") +
            "  |  桥接 " + mode + "  |  驱动 " + (services["driver"] != null ? "运行中" : "未启动") +
            "\nCAN：" + (data.ContainsKey("can") ? data["can"] : "未检查") +
            (liveMode ? "；75% 速度，夹爪开/闭两档。" : "；要控制机械臂，请点左侧“一键启动实机”。");
    }

    async Task StartProject()
    {
        state.Text = "正在启动 WSL 和 ROS 服务…";
        Log("加载统一 ROS 环境，检查冲突和通信服务。");
        monitor = true; // Allow cleanup even if startup fails part-way.
        var data = await Backend("develop");
        string ip = Convert.ToString(data["ip"]);
        using (var socket = new TcpClient())
        {
            Task connect = socket.ConnectAsync(ip, 10000);
            if (await Task.WhenAny(connect, Task.Delay(4000)) != connect)
                throw new Exception("Windows 无法访问 WSL 通信端口；请查看防火墙/网络设置，日志已保留。");
            await connect;
        }
        File.WriteAllText(Path.Combine(cfg.Project, ".surf-ros-ip.txt"), ip, new UTF8Encoding(false));
        Log("通信已就绪，Unity 将在 Play 前使用地址 " + ip + ":10000。");
        LaunchUnity();
        if (vr.Checked)
        {
            LaunchOptional(cfg.Vive, "RRConsole", "VIVE Streaming");
            LaunchOptional(cfg.SteamVR, "vrmonitor", "SteamVR");
        }
        UpdateStatus(data);
        Log("启动完成。无需单独打开 Ubuntu 窗口；ROS 服务日志集中在“查看服务日志”。");
    }

    void LaunchUnity()
    {
        if (!File.Exists(cfg.Unity) || !Directory.Exists(cfg.Project)) throw new Exception("Unity 或项目路径不存在，请检查 config.json。");
        using (var search = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name='Unity.exe'"))
        foreach (ManagementObject row in search.Get())
            if (Convert.ToString(row["CommandLine"]).IndexOf(cfg.Project, StringComparison.OrdinalIgnoreCase) >= 0)
            { Log("Unity 项目已经打开，跳过重复启动。地址在下次进入 Play 时更新。"); return; }
        Process.Start(new ProcessStartInfo(cfg.Unity, "-projectPath " + Runner.Q(cfg.Project)) { UseShellExecute = true });
        Log("已打开 PIPER 项目，等待 Unity 完成加载。");
    }

    void LaunchOptional(string path, string process, string label)
    {
        if (!File.Exists(path)) { Log(label + " 路径不存在，可在 config.json 修改。"); return; }
        if (Process.GetProcessesByName(process).Length > 0) { Log(label + " 已在运行。"); return; }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        Log("已请求打开 " + label + "；头显连接状态请在该软件中确认。");
    }

    async Task AttachCan()
    {
        // Start and retain the distribution before asking usbipd to attach.
        await Backend("status");
        Log("查找已连接的 candleLight USB-CAN，不使用固定 BUSID。");
        var r = await Task.Run(() => Runner.Run(cfg.Usbipd, "state", 15));
        if (r.Code != 0) throw new Exception(r.Error);
        var data = json.Deserialize<Dictionary<string, object>>(r.Output);
        var candidates = new List<Dictionary<string, object>>();
        foreach (object item in (System.Collections.IEnumerable)data["Devices"])
        {
            var d = (Dictionary<string, object>)item;
            if (d["BusId"] != null && Convert.ToString(d["InstanceId"]).Contains("VID_1D50&PID_606F")) candidates.Add(d);
        }
        if (candidates.Count != 1) throw new Exception(candidates.Count == 0 ? "未检测到已连接的 USB-CAN。请插好转接器后重试。" : "检测到多个 USB-CAN，请只连接本项目需要的转接器。");
        var device = candidates[0]; string bus = Convert.ToString(device["BusId"]);
        if (device["PersistedGuid"] == null)
        {
            Log("首次共享该 USB-CAN 需要 Windows 管理员权限，即将显示系统 UAC。");
            using (var p = Process.Start(new ProcessStartInfo(cfg.Usbipd, "bind --busid " + bus) {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }))
            {
                await Task.Run(() => p.WaitForExit());
                if (p.ExitCode != 0) throw new Exception("USB-CAN 共享未完成。");
            }
        }
        if (device["ClientIPAddress"] == null)
        {
            r = await Task.Run(() => Runner.Run(cfg.Usbipd, "attach --wsl " + Runner.Q(cfg.Distro) + " --busid " + bus, 40));
            if (r.Code != 0) throw new Exception("USB 挂载失败：" + r.Output + r.Error);
        }
        r = await Task.Run(() => Runner.Run("wsl.exe", "-d " + Runner.Q(cfg.Distro) +
            " -u root -- bash " + Runner.Q(cfg.Backend.Substring(0, cfg.Backend.LastIndexOf('/') + 1) + "setup_can.sh"), 15));
        if (r.Code != 0) throw new Exception(r.Output + r.Error);
        await Backend("can"); Log("USB-CAN 已挂载，can0 已配置为 1 Mbps。未使能机械臂。");
    }

    async Task StartLive()
    {
        if (MessageBox.Show(this,
            "这将使能 Piper，并开启真实机械臂与夹爪控制。\n\n请先停止 Unity Play，确认物理环境和急停状态，再继续。\n启动后由你手动点击 Play。夹爪为开/闭两档，驱动速度为 75%。",
            "启动实机控制", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        monitor = true;
        var existing = await Backend("status");
        if (Convert.ToString(existing["mode"]) == "live") {
            UpdateStatus(await Backend("live")); Log("实机控制已经开启，不重复挂载或使能。"); return;
        }
        state.Text = "1/3 启动软件与 ROS 通信…";
        await StartProject();
        state.Text = "2/3 自动挂载并检查 USB-CAN…";
        await AttachCan();
        state.Text = "3/3 检查真实反馈并使能 Piper…";
        UpdateStatus(await Backend("live"));
        Log("实机服务已开启。请先确认 Unity 与实机姿态，再手动进入 Play。");
    }

    async Task RepairArm()
    {
        monitor = true;
        state.Text = "1/4 等待 Unity 退出 Play 并修复六轴配置…";
        Log("六轴修复：将停止 Play、恢复发布/IK 配置，重启通信与实机驱动并请求使能；不自动进入 Play。");
        LaunchUnity();
        string folder = Path.Combine(cfg.Project, "Library");
        Directory.CreateDirectory(folder);
        string request = Path.Combine(folder, "surf-arm-repair.request.json");
        string result = Path.Combine(folder, "surf-arm-repair.result.json");
        string id = Guid.NewGuid().ToString("N");
        var payload = new Dictionary<string, object> {
            { "id", id }, { "expiresUtcTicks", DateTime.UtcNow.AddSeconds(110).Ticks }
        };
        // Ignore previous results unless their request id matches this click.
        string temporary = request + ".tmp";
        File.WriteAllText(temporary, json.Serialize(payload), new UTF8Encoding(false));
        if (File.Exists(request)) File.Delete(request);
        File.Move(temporary, request);
        bool repaired = false;
        try {
            DateTime deadline = DateTime.UtcNow.AddSeconds(115);
            while (DateTime.UtcNow < deadline) {
                if (File.Exists(result)) {
                    Dictionary<string, object> reply = null;
                    try { reply = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(result)); }
                    catch (IOException) { }
                    catch (ArgumentException) { }
                    if (reply != null && Convert.ToString(reply["id"]) == id) {
                        if (!Convert.ToBoolean(reply["ok"])) throw new Exception("Unity 修复失败：" + reply["message"]);
                        Log(Convert.ToString(reply["message"])); repaired = true; break;
                    }
                }
                await Task.Delay(500);
            }
            if (!repaired) throw new Exception("Unity 未完成修复，未重连实机。请将 PIPER Unity 窗口切到前台，等待编译结束后再点修复；若有编译错误，先查看 Unity Console。");
        } finally {
            try {
                if (File.Exists(request)) {
                    var pending = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(request));
                    if (Convert.ToString(pending["id"]) == id) File.Delete(request);
                }
            } catch (IOException) { /* Unity may have just consumed the request. */ }
        }
        state.Text = "2/4 重建 ROS 通信和桥接…";
        await Backend("stop"); liveMode = false;
        await StartProject();
        state.Text = "3/4 重新挂载并检查 USB-CAN…";
        await AttachCan();
        state.Text = "4/4 检查真实六轴反馈并恢复使能…";
        UpdateStatus(await Backend("live"));
        state.Text = "六轴配置与实机服务已恢复 · 请手动进入 Play";
        details.Text = "已修复发布并检查实机反馈；Play 后自动对齐姿态。手柄追踪/目标不可达仍需现场确认。";
        Log("修复完成。已保持 75% 速度和夹爪设置；请手动 Play，小幅移动右手柄验证实际跟随。");
    }

    static void Open(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        if (!File.Exists(Path.Combine(root, "config.json")))
        {
            MessageBox.Show("首次使用请先在仓库根目录运行 scripts/setup-windows.ps1。\n它会生成本机配置并创建桌面快捷方式。", "SURF 首次安装");
            return;
        }
        if (args.Contains("--diagnostic"))
        {
            var config = new JavaScriptSerializer().Deserialize<LauncherConfig>(File.ReadAllText(Path.Combine(root, "config.json")));
            var result = Runner.Run("wsl.exe", "-d " + Runner.Q(config.Distro) + " -- uname -a", 30);
            File.WriteAllText(Path.Combine(root, "diagnostic.txt"), "User=" + Environment.UserName +
                " x64=" + Environment.Is64BitProcess + " distro=" + config.Distro +
                " code=" + result.Code + "\n" + result.Output + result.Error, Encoding.UTF8);
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--render"))
        {
            using (var form = new LauncherForm(root))
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.NoAutoStart = true;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.Show(); form.PerformLayout(); Application.DoEvents();
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(root, "launcher-preview.png"));
            }
            return;
        }
        bool created;
        using (var mutex = new Mutex(true, "Local\\SurfPiperLauncher", out created))
        {
            if (!created) { MessageBox.Show("SURF 启动器已经打开，请使用现有窗口。", "SURF"); return; }
            try { Application.Run(new LauncherForm(root) { NoAutoStart = args.Contains("--no-auto-start") }); }
            catch (Exception ex) { MessageBox.Show(ex.ToString(), "SURF 启动器错误"); }
        }
    }
}
