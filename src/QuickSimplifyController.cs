using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Threading;

[assembly: System.Reflection.AssemblyTitle("真白速成轉簡")]
[assembly: System.Reflection.AssemblyProduct("真白速成轉簡")]
[assembly: System.Reflection.AssemblyDescription("微軟速成繁簡切換工具")]

// One controller per desktop session. Only attaches to the foreground 64-bit
// process when its keyboard layout is Traditional Chinese. No text is logged.
class QuickSimplifyController : ApplicationContext {
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate IntPtr LowHook(int code,IntPtr message,IntPtr info);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookEx(int type,IntPtr callback,IntPtr module,uint thread);
    [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr w,IntPtr l);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")]static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
    [DllImport("kernel32.dll")]static extern bool IsWow64Process(IntPtr process,out bool wow64);
    static readonly HashSet<string> excluded=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"QuickSimplifyController","NativeCommitProbe","ChromeKeyboardHarness","Codex","explorer","TextInputHost","ChtIME","Taskmgr","dwm","winlogon","SearchHost","StartMenuExperienceHost","ApplicationFrameHost","RuntimeBroker"};
    class Attachment:IDisposable {public uint pid;public IntPtr window,hook;public MemoryMappedFile mapping;public MemoryMappedViewAccessor view;public void Dispose(){if(hook!=IntPtr.Zero)UnhookWindowsHookEx(hook);if(view!=null)view.Dispose();if(mapping!=null)mapping.Dispose();}}
    readonly Dictionary<uint,Attachment> attachments=new Dictionary<uint,Attachment>();
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem toggle;
    readonly ToolStripMenuItem statusItem;
    readonly ToolStripMenuItem startupItem;
    readonly System.Windows.Forms.Timer timer;
    readonly EventWaitHandle toggleEvent;
    readonly System.Drawing.Icon simpleIcon;
    readonly System.Drawing.Icon traditionalIcon;
    readonly string testProfile;
    readonly bool testNormal;
    readonly string testStatus;
    readonly string testStop;
    readonly string testToggle;
    readonly DateTime started=DateTime.UtcNow;
    readonly IntPtr module,entry;
    readonly LowHook keyboardCallback;
    IntPtr keyboardHook;
    bool enabled=true;
    int tickCount;
    public QuickSimplifyController(string profile,bool normal){
        testProfile=profile;
        testNormal=normal;
        testStatus=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"controller-test-status.json");
        testStop=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"controller-test-stop.signal");
        testToggle=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"controller-test-toggle.signal");
        toggleEvent=new EventWaitHandle(false,EventResetMode.AutoReset,profile==null?"Local\\MashiroQuickSimplifyToggle":"Local\\MashiroQuickSimplifyTestToggle");
        string iconPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"QuickSimplifyIcon.ico");
        string traditionalIconPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"QuickSimplifyTraditional.ico");
        simpleIcon=File.Exists(iconPath)?new System.Drawing.Icon(iconPath):System.Drawing.SystemIcons.Application;
        traditionalIcon=File.Exists(traditionalIconPath)?new System.Drawing.Icon(traditionalIconPath):simpleIcon;
        string dll=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"QuickSimplifyHook.dll");
        if(!File.Exists(dll))throw new Exception("Native hook module missing");
        module=LoadLibrary(dll);if(module==IntPtr.Zero)throw new Exception("Native hook module could not load");
        entry=GetProcAddress(module,"ProbeMessageHook");if(entry==IntPtr.Zero)throw new Exception("Native hook export missing");
        keyboardCallback=ObserveKey;
        keyboardHook=SetWindowsHookEx(13,Marshal.GetFunctionPointerForDelegate(keyboardCallback),GetModuleHandle(null),0);
        if(keyboardHook==IntPtr.Zero)throw new Exception("Keyboard observer could not start");
        toggle=new ToolStripMenuItem("切換至繁體",null,(s,e)=>SetEnabled(!enabled));
        statusItem=new ToolStripMenuItem("簡體模式"){Enabled=false};
        startupItem=new ToolStripMenuItem("開機自動啟動",null,(s,e)=>SetAutoStart(!IsAutoStartEnabled())){Checked=IsAutoStartEnabled(),Enabled=profile==null};
        var menu=new ContextMenuStrip();menu.Items.Add(statusItem);menu.Items.Add(toggle);menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripMenuItem("顯示圖示設定",null,(s,e)=>OpenTaskbarSettings()));
        menu.Items.Add(new ToolStripMenuItem("結束",null,(s,e)=>ExitThread()));
        tray=new NotifyIcon{Icon=simpleIcon,Text="真白速成轉簡：簡體模式",Visible=true,ContextMenuStrip=menu};
        tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)SetEnabled(!enabled);};
        timer=new System.Windows.Forms.Timer{Interval=250};timer.Tick+=(s,e)=>Tick();timer.Start();
    }
    IntPtr ObserveKey(int code,IntPtr message,IntPtr info){
        if(code>=0&&message.ToInt64()==0x100&&enabled){
            uint pid;GetWindowThreadProcessId(GetForegroundWindow(),out pid);
            Attachment a;if(attachments.TryGetValue(pid,out a))a.view.Write(8,(uint)Marshal.ReadInt32(info));
        }
        return CallNextHookEx(keyboardHook,code,message,info);
    }
    void SetEnabled(bool value){enabled=value;foreach(var a in attachments.Values)a.view.Write(12,enabled?1U:0U);tray.Icon=enabled?simpleIcon:traditionalIcon;toggle.Text=enabled?"切換至繁體":"切換至簡體";tray.Text=enabled?"真白速成轉簡：簡體模式":"真白速成轉簡：繁體模式";statusItem.Text=enabled?"簡體模式":"繁體模式";}
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue="MashiroQuickSimplify";
    bool IsAutoStartEnabled(){try{using(var key=Registry.CurrentUser.OpenSubKey(RunKey)){return String.Equals(key==null?null:key.GetValue(RunValue) as string,"\""+Application.ExecutablePath+"\"",StringComparison.OrdinalIgnoreCase);}}catch{return false;}}
    void SetAutoStart(bool value){try{using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(value)key.SetValue(RunValue,"\""+Application.ExecutablePath+"\"");else key.DeleteValue(RunValue,false);}startupItem.Checked=value;}catch(Exception e){MessageBox.Show("無法更改開機自動啟動："+e.Message,"真白速成轉簡");startupItem.Checked=IsAutoStartEnabled();}}
    void OpenTaskbarSettings(){try{Process.Start(new ProcessStartInfo("ms-settings:taskbar"){UseShellExecute=true});}catch(Exception e){MessageBox.Show("無法開啟工作列設定："+e.Message,"真白速成轉簡");}}
    bool IsTestProcess(uint pid){
        if(testProfile==null)return true;
        try{using(var search=new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId="+pid))foreach(ManagementObject p in search.Get()){string cmd=p["CommandLine"] as string;return cmd!=null&&cmd.IndexOf(testProfile,StringComparison.OrdinalIgnoreCase)>=0;}}catch{}return false;
    }
    void Tick(){
        tickCount++;
        if(toggleEvent.WaitOne(0))SetEnabled(!enabled);
        if(testProfile!=null&&File.Exists(testToggle)){File.Delete(testToggle);SetEnabled(!enabled);}
        if(testProfile!=null&&tickCount%4==0){var rows=new List<object>();foreach(var a in attachments.Values){try{rows.Add(new {pid=a.pid,state=a.view.ReadUInt32(16),calls=a.view.ReadUInt32(20),conversions=a.view.ReadUInt32(24),activeQuick=a.view.ReadUInt32(572)});}catch{}}File.WriteAllText(testStatus,new JavaScriptSerializer().Serialize(rows),new UTF8Encoding(false));}
        if(testProfile!=null&&(File.Exists(testStop)||DateTime.UtcNow-started>TimeSpan.FromSeconds(40))){ExitThread();return;}
        if(!enabled)return;
        IntPtr window=GetForegroundWindow();if(window==IntPtr.Zero)return;
        uint pid;uint thread=GetWindowThreadProcessId(window,out pid);if(pid==0||pid==(uint)Process.GetCurrentProcess().Id)return;
        Attachment current;if(attachments.TryGetValue(pid,out current)){uint state=current.view.ReadUInt32(16);statusItem.Text=state==1?"目前程式：已轉簡":state==20?"目前程式：Windows 版本不支援":"目前程式：接入中";return;}
        if(testProfile==null&&(GetKeyboardLayout(thread).ToInt64()&0xffff)!=0x0404)return;
        if(!IsTestProcess(pid))return;
        Process process;try{process=Process.GetProcessById((int)pid);}catch{return;}
        if(excluded.Contains(process.ProcessName))return;
        bool wow64;try{if(!IsWow64Process(process.Handle,out wow64)||wow64)return;}catch{return;}
        Attachment added=null;
        try{
            added=new Attachment{pid=pid,window=window,mapping=MemoryMappedFile.CreateOrOpen("Local\\QuickNativeProbe_"+pid,1024)};
            added.view=added.mapping.CreateViewAccessor();for(long i=0;i<1024;i++)added.view.Write(i,(byte)0);
            added.view.Write(0,pid);added.view.Write(4,(uint)Process.GetCurrentProcess().Id);added.view.Write(12,1U);added.view.Write(596,testProfile==null||testNormal?1U:2U);
            added.hook=SetWindowsHookEx(3,entry,module,thread);if(added.hook==IntPtr.Zero){added.Dispose();return;}
            attachments.Add(pid,added);PostMessage(window,0,IntPtr.Zero,IntPtr.Zero);
        }catch{if(added!=null)added.Dispose();}
    }
    protected override void ExitThreadCore(){
        timer.Stop();tray.Visible=false;
        var cleanup=new List<object>();
        foreach(var a in attachments.Values){
            try{a.view.Write(12,0U);a.view.Write(544,1U);PostMessage(a.window,0,IntPtr.Zero,IntPtr.Zero);
                for(int i=0;i<20&&a.view.ReadUInt32(16)!=90;i++)System.Threading.Thread.Sleep(50);
                cleanup.Add(new {pid=a.pid,state=a.view.ReadUInt32(16),stopped=a.view.ReadUInt32(16)==90});
            }catch(Exception e){cleanup.Add(new {pid=a.pid,error=e.Message});}finally{a.Dispose();}
        }
        if(testProfile!=null)File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"controller-test-cleanup.json"),new JavaScriptSerializer().Serialize(cleanup),new UTF8Encoding(false));
        attachments.Clear();if(keyboardHook!=IntPtr.Zero)UnhookWindowsHookEx(keyboardHook);tray.Dispose();timer.Dispose();toggleEvent.Dispose();if(!Object.ReferenceEquals(simpleIcon,System.Drawing.SystemIcons.Application))simpleIcon.Dispose();if(!Object.ReferenceEquals(traditionalIcon,simpleIcon))traditionalIcon.Dispose();base.ExitThreadCore();
    }
    [STAThread]static void Main(string[] args){string profile=null;bool normal=false;bool toggleOnly=args.Length==1&&args[0]=="--toggle";bool testToggleOnly=args.Length==2&&args[0]=="--test-toggle";if(args.Length==2&&(args[0]=="--test-profile"||args[0]=="--test-profile-normal"||testToggleOnly)){profile=Path.GetFullPath(args[1]);normal=args[0]=="--test-profile-normal";}bool first;using(var single=new Mutex(true,profile==null?"Local\\MashiroQuickSimplify":"Local\\MashiroQuickSimplifyTest",out first)){if(!first){if(toggleOnly||testToggleOnly){try{using(var signal=EventWaitHandle.OpenExisting(profile==null?"Local\\MashiroQuickSimplifyToggle":"Local\\MashiroQuickSimplifyTestToggle"))signal.Set();}catch{}}return;}if(testToggleOnly)return;Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);try{Application.Run(new QuickSimplifyController(profile,normal));}catch(Exception e){MessageBox.Show(e.Message,"真白速成轉簡啟動失敗");}}}
}
