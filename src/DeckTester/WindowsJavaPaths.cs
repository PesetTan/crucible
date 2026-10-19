using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DeckTester;

// Java 17 expands short names before loading jimage.dll. A directory junction
// keeps the whole JDK hierarchy on an ASCII path, including subsequent DLL loads.
// Nothing is copied, and the actual JDK and data directories retain their names.
[SupportedOSPlatform("windows")]
internal static class WindowsJavaPaths
{
    private static readonly object Gate=new();
    private static readonly Dictionary<string,string> Aliases=new(StringComparer.OrdinalIgnoreCase);
    static WindowsJavaPaths()=>AppDomain.CurrentDomain.ProcessExit+=(_,_)=>Cleanup();

    public static string Executable(string executable)
    {
        var full=Path.GetFullPath(executable);
        var bin=Path.GetDirectoryName(full)!;
        if(!Path.GetFileName(bin).Equals("bin",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The configured Java executable must be in its JDK bin folder, or use an ASCII-only path.");
        var home=Path.GetDirectoryName(bin)!;
        lock(Gate){
            if(!Aliases.TryGetValue(home,out var alias)){
                var parent=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Temp","CrucibleJava-"+Guid.NewGuid().ToString("N"));
                if(!parent.All(char.IsAscii))
                    throw new InvalidOperationException("Java 17 needs an ASCII runtime path. Choose an ASCII-only MTG_PROJECT_ROOT; saved data was not moved.");
                // Create the private container with its final DACL, before placing
                // an executable alias under the shared Windows temporary folder.
                using var identity=WindowsIdentity.GetCurrent();
                var security=new DirectorySecurity();
                var owner=identity.User??throw new InvalidOperationException("Windows user identity is unavailable.");
                security.SetOwner(owner);
                security.SetAccessRuleProtection(true,false);
                security.AddAccessRule(new FileSystemAccessRule(owner,FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                new DirectoryInfo(parent).Create(security);
                alias=Path.Combine(parent,"jdk");
                try{
                    Directory.CreateDirectory(alias);
                    Junction(alias,home);
                    Aliases.Add(home,alias);
                }catch{
                    Remove(alias);
                    throw;
                }
            }
            return Path.Combine(alias,"bin",Path.GetFileName(full));
        }
    }

    private static void Junction(string link,string target)
    {
        if(target.StartsWith(@"\\",StringComparison.Ordinal))
            throw new InvalidOperationException("Use a local JDK directory for Windows Java setup.");
        var substitute=Encoding.Unicode.GetBytes(@"\??\"+target.TrimEnd('\\'));
        var display=Encoding.Unicode.GetBytes(target.TrimEnd('\\'));
        using var payload=new MemoryStream();
        using(var writer=new BinaryWriter(payload,Encoding.Unicode,leaveOpen:true)){
            writer.Write(0xA0000003u); // IO_REPARSE_TAG_MOUNT_POINT (a directory junction)
            writer.Write(checked((ushort)(8+substitute.Length+2+display.Length+2)));
            writer.Write((ushort)0);
            writer.Write((ushort)0);writer.Write(checked((ushort)substitute.Length));
            writer.Write(checked((ushort)(substitute.Length+2)));writer.Write(checked((ushort)display.Length));
            writer.Write(substitute);writer.Write((ushort)0);writer.Write(display);writer.Write((ushort)0);
        }
        var bytes=payload.ToArray();
        if(bytes.Length>16384)throw new PathTooLongException("Java runtime path exceeds the Windows junction limit.");
        using var handle=CreateFile(link,0x40000000,0,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
        if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot open the private Java path alias.");
        if(!DeviceIoControl(handle,0x000900A4,bytes,(uint)bytes.Length,IntPtr.Zero,0,out _,IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot create the private Java path alias.");
    }

    private static void Remove(string alias)
    {
        // Never recurse through a junction into the user's actual JDK.
        try{Directory.Delete(alias);}catch(IOException){}catch(UnauthorizedAccessException){}
        try{Directory.Delete(Path.GetDirectoryName(alias)!);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    private static void Cleanup(){lock(Gate){foreach(var alias in Aliases.Values)Remove(alias);Aliases.Clear();}}

    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[] input,uint inputLength,IntPtr output,uint outputLength,out uint returned,IntPtr overlapped);
}
