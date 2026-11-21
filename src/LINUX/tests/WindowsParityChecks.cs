using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NOVORA.Linux;
using NOVORA.Linux.Control;
using NOVORA.Linux.VisionEngine;
using System.Net;
using System.Reflection;

internal static class WindowsParityChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "novora-parity-config-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", root);
        try
        {
            AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var home = (Avalonia.Controls.Control)((Dictionary<string,Avalonia.Controls.Control>)Field(window,"_pages"))["Inicio"];
            var buttons = home.GetVisualDescendants().OfType<Button>().ToArray();
            // Conexión dinámica: Inicio no tiene selector de ruta; USB/LAN se enlazan solos.
            if (buttons.Any(b => b.Content as string == "TIPO DE CONEXIÓN ▾"))
                throw new Exception("Home must not expose a manual USB/LAN route selector.");
            var screen = buttons.First(b => b.Content as string == "VER PANTALLA");
            screen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if ((string)Field(window,"_currentPage") != "Inicio")
                throw new Exception("VER PANTALLA must request capture on Home, not merely navigate away.");
            var cards = home.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("nv-card")).ToArray();
            if (cards.Length != 4) throw new Exception($"Windows Home has exactly four cards; found {cards.Length}.");
            var grid = cards[0].Child as Grid;
            if (grid?.ColumnDefinitions.Count != 2) throw new Exception("Windows connection card must have information left and actions right.");
            Pump(CheckConnectionUiAsync(window,root));
            string output = Path.Combine(Path.GetTempPath(), "novora-windows-parity");
            Directory.CreateDirectory(output);
            foreach(string route in new[]{"LAN","USB","LAN"})
            {
                var open=(Task)typeof(MainWindow).GetMethod("OpenConnectionSetupAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[route])!;
                Pump(open);
                var dialog=(Window)Field(window,"_connectionWindow");
                if(!dialog.IsVisible) throw new Exception("Connection setup did not open for " + route);
                Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout();
                using(var capture=new RenderTargetBitmap(new PixelSize(700,570)))
                { capture.Render(dialog); capture.Save(Path.Combine(output,$"Connection-{route}.png")); }
                dialog.Close(); Dispatcher.UIThread.RunJobs();
            }
            Set((LinuxControlHost)Field(window,"_host"),"_selectedTransport",null);
            Dispatcher.UIThread.RunJobs();
            typeof(MainWindow).GetMethod("SetMessage",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,["NOVORA listo.","#dbe5f1"]);
            RefreshConnection(window);
            foreach (string theme in new[]{LinuxTheme.Light,LinuxTheme.Dark})
            {
                LinuxTheme.Apply(theme);
                foreach (var size in new[]{(1180,800),(1000,610)})
                {
                    window.Width=size.Item1; window.Height=size.Item2;
                    using (var resized=new CancellationTokenSource())
                    {
                        DispatcherTimer.RunOnce(()=>resized.Cancel(),TimeSpan.FromMilliseconds(100));
                        Dispatcher.UIThread.MainLoop(resized.Token);
                    }
                    foreach (string page in new[]{"Inicio","VisionEngine","LinkEngine","ExInEngine","Integración","Configuración","Rendimiento"})
                    {
                        typeof(MainWindow).GetMethod("ShowPage",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[page]);
                        Dispatcher.UIThread.RunJobs();
                        window.UpdateLayout();
                        using var bitmap = new RenderTargetBitmap(new PixelSize(size.Item1,size.Item2));
                        bitmap.Render(window);
                        bitmap.Save(Path.Combine(output,$"{theme}-{page}-{size.Item1}.png"));
                    }
                }
            }
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine("Windows Home actions/composition, reusable USB/LAN dialogs and all-page Light/Dark rendering passed: " + output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
            if(Directory.Exists(root)) Directory.Delete(root,true);
        }
    }
    private static object Field(object target,string name) => target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target,string name,object? value) => target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(target,value);
    private static void RefreshConnection(MainWindow window) => typeof(MainWindow).GetMethod("UpdateDeviceStatus",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);

    private static async Task CheckConnectionUiAsync(MainWindow window,string root)
    {
        var host=(LinuxControlHost)Field(window,"_host");
        using var store=new LinuxTrustStore(Path.Combine(root,"ui-fixture"));
        var engine=new LinuxVisionEngine(new AdbBridge(),"B");
        var usb=new LinuxControlServer(IPAddress.Loopback,store,port:0,transport:"USB",visionEngine:engine,allowLoopbackForTests:true);
        var lan=new LinuxControlServer(IPAddress.Loopback,store,port:0,allowLoopbackForTests:true);
        Set(host,"_usbServer",usb); Set(host,"_lanServer",lan); Set(host,"_usbSerial","B");
        usb.Start(); lan.Start();
        try
        {
            using var usbClient=await ConnectionSelectionChecks.ConnectAsync(usb.UsbInvitation.Host,usb.UsbInvitation.Port,store.Fingerprint);
            await ConnectionSelectionChecks.PairAsync(usbClient,"pair",usb.UsbInvitation.Secret);
            using var lanClient=await ConnectionSelectionChecks.ConnectAsync(lan.Invitation.Host,lan.Invitation.Port,store.Fingerprint);
            await ConnectionSelectionChecks.PairAsync(lanClient,"pair.lan",lan.Invitation.Code);
            host.SelectTransport("USB"); host.RequestVision();
            // Reproduce a real engine cleanup failure without launching ADB or an Android session.
            Set(engine,"_sessionTask",Task.FromException(new InvalidOperationException("fixture recording finalization failed")));
            var stop=(Task)typeof(MainWindow).GetMethod("ToggleHomeVisionAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!;
            await stop;
            if(!((TextBlock)Field(window,"_message")).Text!.Contains("fixture recording finalization failed"))
                throw new Exception("A failed capture stop must remain visible in the UI.");
            Set(engine,"_sessionTask",Task.CompletedTask);

            var a=new AdbDevice("A","device","Phone A",true); var b=new AdbDevice("B","device","Phone B",true);
            var devices=(ComboBox)Field(window,"_adbDevices"); devices.ItemsSource=new[]{a,b}; devices.SelectedItem=a;
            RefreshConnection(window);
            if(((TextBlock)Field(window,"_homeDeviceName")).Text!="Phone B")
                throw new Exception("Home displayed the selected ADB device instead of the authorized serial B.");
            await usb.DisposeAsync();
            RefreshConnection(window);
            // Dynamic connectivity: losing USB moves to the still-authorized LAN route.
            if(((TextBlock)Field(window,"_deviceStatus")).Text != "Android conectado por LAN.")
                throw new Exception("The connection summary did not report the authorized LAN route.");
            if(((TextBlock)Field(window,"_homeConnectionStatus")).Text != "Conectado por LAN")
                throw new Exception("Home did not show the LAN connection after USB was lost.");
        }
        finally
        {
            Set(engine,"_sessionTask",Task.CompletedTask);
            await usb.DisposeAsync(); await lan.DisposeAsync();
            Set(host,"_usbServer",null); Set(host,"_lanServer",null); Set(host,"_usbSerial",null); Set(host,"_selectedTransport",null);
            RefreshConnection(window);
        }
    }
    private static void Pump(Task task)
    {
        var deadline=DateTime.UtcNow.AddSeconds(10);
        while(!task.IsCompleted && DateTime.UtcNow<deadline)
        {
            using var tick=new CancellationTokenSource();
            DispatcherTimer.RunOnce(()=>tick.Cancel(),TimeSpan.FromMilliseconds(25));
            Dispatcher.UIThread.MainLoop(tick.Token);
        }
        if(!task.IsCompleted) throw new Exception("Connection dialog did not finish preparing in 10 seconds.");
        task.GetAwaiter().GetResult();
    }
}
