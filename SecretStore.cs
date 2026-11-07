using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
namespace Island;
internal sealed class SecretStore
{
    private readonly Dictionary<string,string> values;
    private static string FileName=>Path.Combine(Settings.Folder,"secrets.dat");
    public string? LoadError { get; }
    public SecretStore() {
        try { values=File.Exists(FileName)?JsonSerializer.Deserialize<Dictionary<string,string>>(ProtectedData.Unprotect(File.ReadAllBytes(FileName),null,DataProtectionScope.CurrentUser))??new():new(); }
        catch { values=new();LoadError="Saved credentials could not be decrypted. Reconnect your accounts."; }
    }
    public string Get(string key)=>values.GetValueOrDefault(key,"");
    public void Set(string key,string value) { values[key]=value;Save(); }
    public void Remove(string key) { values.Remove(key);Save(); }
    private void Save() { Directory.CreateDirectory(Settings.Folder);var bytes=JsonSerializer.SerializeToUtf8Bytes(values);try { File.WriteAllBytes(FileName+".tmp",ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser));File.Move(FileName+".tmp",FileName,true); } finally { CryptographicOperations.ZeroMemory(bytes); } }
}
