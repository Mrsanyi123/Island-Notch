using System;
using Microsoft.Win32;
namespace Island;
internal static class Startup
{
    private const string Run=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Enabled {get {using var key=Registry.CurrentUser.OpenSubKey(Run);return key?.GetValue("Island")!=null;}}
    public static void Set(bool enabled) {using var key=Registry.CurrentUser.CreateSubKey(Run);if(enabled){string path=Environment.ProcessPath??throw new InvalidOperationException();if(System.IO.Path.GetFileNameWithoutExtension(path).Equals("dotnet",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Publish Island before enabling startup.");key.SetValue("Island","\""+path+"\"");}else key.DeleteValue("Island",false);}
}
