using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using NOVORA.Linux;
using System.Reflection;

internal static class VisionUiChecks
{
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"novora-ve-ui-"+Guid.NewGuid().ToString("N"));
        string? old=Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME",root);
        try
        {
            AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
            var window=new MainWindow { Width=1280,Height=960 };
            window.Show();
            typeof(MainWindow).GetMethod("ShowPage",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,["VisionEngine"]);
            Dispatcher.UIThread.RunJobs();
            string[] required={"VER PANTALLA","Iniciar grabación MKV","Enviar archivos","Enviar carpeta","Cancelar envío"};
            var labels=window.GetLogicalDescendants().OfType<Button>().Select(b=>b.Content as string).ToArray();
            foreach(string label in required) if(!labels.Contains(label)) throw new Exception("Missing VE action: "+label);
            using var bitmap=new RenderTargetBitmap(new PixelSize(1280,960)); bitmap.Render(window); bitmap.Save("/tmp/novora-ve-ui.png");
            window.Close(); Dispatcher.UIThread.RunJobs();
            Console.WriteLine("VisionEngine UI native construction, actions and rendering checks passed: /tmp/novora-ve-ui.png");
        }
        finally { Environment.SetEnvironmentVariable("XDG_CONFIG_HOME",old); if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
