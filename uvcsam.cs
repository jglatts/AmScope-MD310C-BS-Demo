using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Permissions;
using System.Runtime.ConstrainedExecution;
using System.Collections.Generic;
using System.Threading;

/*
    Version: @VERM@.$WCREV$.@WCNOW@
    For Microsoft dotNET Framework & dotNet Core
    We use P/Invoke to call into the uvcsam.dll API, the c# class Uvcsam is a thin wrapper class to the native api of uvcsam.dll
*/
internal class Uvcsam : IDisposable
{
    public enum eEVENT : uint
    {
        AWB        = 0x0001,
        FFC        = 0x0002,
        LEVELRANGE = 0x0004,
        IMAGE      = 0x0008,
        TRIGGER    = 0x0010,    /* user push the trigger button */
        FLIP       = 0x0020,    /* user push the flip button */
        EXPOTIME   = 0x0040,    /* auto exposure: exposure time changed */
        GAIN       = 0x0080,    /* auto exposure: gain changed */
        DISCONNECT = 0x0100,    /* camera disconnect */
        ERROR      = 0x0200     /* error */
    };

    /* command: get & put */
    public enum eCMD : uint
    {
        FWVERSION        = 0x00000001,    /* firmware version, such as: 1.2.3 */
        FWDATE           = 0x00000002,    /* firmware date, such as: 20191018 */
        HWVERSION        = 0x00000003,    /* hardware version, such as: 1.2 */
        REVISION         = 0x00000004,    /* revision */
        GAIN             = 0x00000005,    /* gain, percent, 100 means 100% */
        EXPOTIME         = 0x00000006,    /* exposure time, in microseconds */
        AE_ONOFF         = 0x00000007,    /* auto exposure: 0 = off, 1 = on */
        AE_TARGET        = 0x00000008,    /* auto exposure target, range = [UVCSAM_AE_TARGET_MIN, UVCSAM_AE_TARGET_MAX] */
        AE_ROILEFT       = 0x00000009,    /* auto exposure roi: left */
        AE_ROITOP        = 0x0000000a,    /* top */
        AE_ROIRIGHT      = 0x0000000b,    /* right */
        AE_ROIBOTTOM     = 0x0000000c,    /* bottom */
        AWB              = 0x0000000d,    /* white balance: 0 = manual, 1 = global auto, 2 = roi, 3 = roi mode 'once' */
        AWB_ROILEFT      = 0x0000000e,    /* white balance roi: left */
        AWB_ROITOP       = 0x0000000f,    /* top */
        AWB_ROIRIGHT     = 0x00000010,    /* right */
        AWB_ROIBOTTOM    = 0x00000011,    /* bottom */
        WBGAIN_RED       = 0x00000012,    /* white balance gain, range: [UVCSAM_WBGAIN_MIN, UVCSAM_WBGAIN_MAX] */
        WBGAIN_GREEN     = 0x00000013,
        WBGAIN_BLUE      = 0x00000014,
        VFLIP            = 0x00000015,    /* flip vertical */
        HFLIP            = 0x00000016,    /* flip horizontal */
        FFC              = 0x00000017,    /* flat field correction
                                                    put:
                                                        0: disable
                                                        1: enable
                                                        -1: reset
                                                        (0xff000000 | n): set the average number to n, [1~255]
                                                    get:
                                                        (val & 0xff): 0 -> disable, 1 -> enable, 2 -> inited
                                                        ((val & 0xff00) >> 8): sequence
                                                        ((val & 0xff0000) >> 8): average number
                                          */
        FFC_ONCE         = 0x00000018,    /* ffc (flat field correction) 'once' */
        LEVELRANGE_AUTO  = 0x00000019,    /* level range auto */
        LEVELRANGE_LOW   = 0x0000001a,    /* level range low */
        LEVELRANGE_HIGH  = 0x0000001b,    /* level range high */
        HISTOGRAM        = 0x0000001c,    /* histogram */
        CHROME           = 0x0000001d,    /* monochromatic mode */
        NEGATIVE         = 0x0000001e,    /* negative film */
        PAUSE            = 0x0000001f,    /* pause */
        SHARPNESS        = 0x00000020,
        SATURATION       = 0x00000021,
        GAMMA            = 0x00000022,
        CONTRAST         = 0x00000023,
        BRIGHTNESS       = 0x00000024,
        HZ               = 0x00000025,    /* 2 -> 60HZ AC;  1 -> 50Hz AC;  0 -> DC */
        HUE              = 0x00000026,
        LIGHT_SOURCE     = 0x00000027,    /* light source: 0~8 */
        REALTIME         = 0x00000028,    /* realtime: 1 => ON, 0 => OFF */
        FORMAT           = 0x000000fe,    /* output format: 0 => BGR888, 1 => BGRA8888, 2 => RGB888, 3 => RGBA8888; default: 0
                                             MUST be set before start
                                          */
        MAGIC            = 0x000000ff,
        
        GPIO             = 0x08000000,    /* GPIO: 0~7 bit corresponds to GPIO */
        RES              = 0x10000000,    /* resolution:
                                                Can be changed only when camera is not running.
                                                To get the number of the supported resolution, use: Uvcsam_range(h, UVCSAM_RES, nullptr, &num, nullptr)
                                          */
        CODEC            = 0x20000000,    /* 0: mjpeg, 1: YUY2; Can be changed only when camera is not running */
        WIDTH            = 0x40000000,    /* to get the nth width, use: Uvcsam_get(h, UVCSAM_WIDTH | n, &width) */
        HEIGHT           = 0x80000000
    };
    /********************************************************************/
    /* How to enum the resolutions:                                     */
    /*     cam_.range(eCMD.RES, null, &num, null);                      */
    /*     for (int i = 0; i <= num; ++i)                               */
    /*     {                                                            */
    /*         int width, height;                                       */
    /*         cam_.get(eCMD.WIDTH | i, out width);                     */
    /*         cam_.get(eCMD.HEIGHT | i, out height);                   */
    /*         Console.WriteLine("%d: %d x %d", i, width, height);      */
    /*     }                                                            */
    /********************************************************************/
    
    /********************************************************************/
    /* "Direct Mode" vs "Pull Mode"                                     */
    /* (1) Direct Mode:                                                 */
    /*     (a) cam_.start(h, pFrameBuffer, ...)                         */
    /*     (b) pros: simple, slightly more efficient                    */
    /* (2) Pull Mode:                                                   */
    /*     (a) cam_.start(h, null, ...)                                 */
    /*     (b) use cam_.pull(h, pFrameBuffer) to pull image             */
    /*     (c) pros: never frame confusion                              */
    /********************************************************************/

    public const int ROI_WIDTH_MIN    = 32;
    public const int ROI_HEIGHT_MIN   = 32;
    public const int AE_TARGET_MIN    = 16;      /* auto exposure target: minimum */
    public const int AE_TARGET_DEF    = 48;      /* auto exposure target: default */
    public const int AE_TARGET_MAX    = 208;     /* auto exposure target: maximum */

    public const int WBGAIN_MIN       = 1;
    public const int WBGAIN_MAX       = 255;
    public const int WBGAIN_RED_DEF   = 128;
    public const int WBGAIN_GREEN_DEF = 64;
    public const int WBGAIN_BLUE_DEF  = 128;

    public const int LEVELRANGE_MIN   = 0;
    public const int LEVELRANGE_MAX   = 255;

    public const int LIGHT_SOURCE_MIN = 0;
    public const int LIGHT_SOURCE_MAX = 8;
    public const int LIGHT_SOURCE_DEF = 5;

    public struct Device
    {
        public string displayname; /* display name */
        public string id;          /* unique and opaque id of a connected camera */
    };

    public delegate void DelegateCALLBACK(eEVENT nEvent);

    [DllImport("ntdll.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
    public static extern void memcpy(IntPtr dest, IntPtr src, IntPtr count);

    static public int TDIBWIDTHBYTES(int bits)
    {
        return ((bits + 31) & (~31)) / 8;
    }

    /* only for compatibility with .Net 4.0 and below */
    public static IntPtr IncIntPtr(IntPtr p, int offset)
    {
        return new IntPtr(p.ToInt64() + offset);
    }

    /* get the version of this dll, which is: @VERM@.$WCREV$.@WCNOW@ */
    public static string version()
    {
        return Uvcsam_version();
    }

    public void close()
    {
        Dispose();
    }

    public void Dispose()  // Follow the Dispose pattern - public nonvirtual.
    {
        Dispose(true);
        map_.Remove(id_.ToInt32());
        GC.SuppressFinalize(this);
    }

    /* enumerate the cameras that are currently connected to computer */
    public static Device[] Enum()
    {
        IntPtr p = Marshal.AllocHGlobal(512 * 16);
        IntPtr ti = p;
        uint cnt = Uvcsam_enum(p);
        Device[] arr = new Device[cnt];
        for (uint i = 0; i < cnt; ++i)
        {
            arr[i].displayname = Marshal.PtrToStringUni(p);
            p = IncIntPtr(p, sizeof(char) * 128);
            arr[i].id = Marshal.PtrToStringUni(p);
            p = IncIntPtr(p, sizeof(char) * 128);
        }
        Marshal.FreeHGlobal(ti);
        return arr;
    }

    /*
        the object of Uvcsam must be obtained by static mothod open, it cannot be obtained by obj = new Uvcsam (The constructor is private on purpose)
    */
    public static Uvcsam open(string camId)
    {
        SafeCamHandle h = Uvcsam_open(camId);
        if (h == null || h.IsInvalid || h.IsClosed)
            return null;
        return new Uvcsam(h);
    }

    public int start(IntPtr pFrameBuffer, DelegateCALLBACK dCallback)
    {
        dCallback_ = dCallback;
        pCallback_ = delegate (eEVENT nEvent, IntPtr pCtx)
        {
            Uvcsam pthis = null;
            if (map_.TryGetValue(pCtx.ToInt32(), out pthis) && (pthis != null) && (pthis.dCallback_ != null))
                pthis.dCallback_(nEvent);
        };
        return Uvcsam_start(handle_, pFrameBuffer, pCallback_, id_);
    }

    public int stop()
    {
        return Uvcsam_stop(handle_);
    }

    public int pull(IntPtr pFrameBuffer)
    {
        return Uvcsam_pull(handle_, pFrameBuffer);
    }

    /* filePath == null means to stop record.
        support file extension: *.asf, *.mp4, *.mkv
    */
    public int record(string filePath)
    {
        return Uvcsam_record(handle_, filePath);
    }

    public int put(eCMD nId, int val)
    {
        return Uvcsam_put(handle_, nId, val);
    }

    public int get(eCMD nId, out int pVal)
    {
        return Uvcsam_get(handle_, nId, out pVal);
    }

    public int range(eCMD nId, out int pMin, out int pMax, out int pDef)
    {
        return Uvcsam_range(handle_, nId, out pMin, out pMax, out pDef);
    }

    public int ffcimport(string filepath)
    {
        return Uvcsam_ffcimport(handle_, filepath);
    }

    public int ffcexport(string filepath)
    {
        return Uvcsam_ffcexport(handle_, filepath);
    }

    private static int sid_ = 0;
    private static Dictionary<int, Uvcsam> map_ = new Dictionary<int, Uvcsam>();

    private SafeCamHandle handle_;
    private IntPtr id_;
    private DelegateCALLBACK dCallback_;
    private CALLBACK pCallback_;

    /*
        the object of Uvcsam must be obtained by static mothod open, it cannot be obtained by obj = new Uvcsam (The constructor is private on purpose)
    */
    private Uvcsam(SafeCamHandle h)
    {
        handle_ = h;
        id_ = new IntPtr(Interlocked.Increment(ref sid_));
        map_.Add(id_.ToInt32(), this);
    }

    ~Uvcsam()
    {
        Dispose(false);
    }

    [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
    protected virtual void Dispose(bool disposing)
    {
        // Note there are three interesting states here:
        // 1) CreateFile failed, _handle contains an invalid handle
        // 2) We called Dispose already, _handle is closed.
        // 3) _handle is null, due to an async exception before
        //    calling CreateFile. Note that the finalizer runs
        //    if the constructor fails.
        if (handle_ != null && !handle_.IsInvalid)
        {
            // Free the handle
            handle_.Dispose();
        }
        // SafeHandle records the fact that we've called Dispose.
    }

    private class SafeCamHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern void Uvcsam_close(IntPtr h);

        public SafeCamHandle()
            : base(true)
        {
        }

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        override protected bool ReleaseHandle()
        {
            // Here, we must obey all rules for constrained execution regions.
            Uvcsam_close(handle);
            return true;
        }
    };

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CALLBACK(eEVENT nEvent, IntPtr pCtx);

    [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.LPWStr)]
    private static extern string Uvcsam_version();
    [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern uint Uvcsam_enum(IntPtr ti);
    /* camId == nullptr means the first device to open */
    [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern SafeCamHandle Uvcsam_open([MarshalAs(UnmanagedType.LPWStr)] string camId);
    [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int Uvcsam_start(SafeCamHandle h, IntPtr pFrameBuffer, CALLBACK pCallbackFun, IntPtr pCallbackCtx);
    [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int Uvcsam_stop(SafeCamHandle h);
    [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int Uvcsam_put(SafeCamHandle h, eCMD nId, int val);
    [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int Uvcsam_get(SafeCamHandle h, eCMD nId, out int pVal);
    [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int Uvcsam_range(SafeCamHandle h, eCMD nId, out int pMin, out int pMax, out int pDef);
    [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int Uvcsam_ffcimport(SafeCamHandle h, [MarshalAs(UnmanagedType.LPWStr)] string filepath);
    [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int Uvcsam_ffcexport(SafeCamHandle h, [MarshalAs(UnmanagedType.LPWStr)] string filepath);
    [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int Uvcsam_pull(SafeCamHandle h, IntPtr pFrameBuffer);
    [DllImport("uvcsam.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int Uvcsam_record(SafeCamHandle h, [MarshalAs(UnmanagedType.LPStr)] string filePath);
}
